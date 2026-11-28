import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, errorMessage } from "../lib/api";
import { UiIcon } from "../UiIcon";
import { RecoveryDrill } from "../components/RecoveryDrill";
import "./dashboard.css";

type Category = "Technical" | "HR" | "Behavioral" | "System Design";
type Difficulty = "Junior" | "Mid-Level" | "Senior";

/* Mirrors InterviewQuestionDto / InterviewSessionDto. Scores are 0-10. */
interface Question {
  id: number;
  questionText: string;
  answerText: string | null;
  feedback: string | null;
  feedbackScore: number | null;
  strengths: string[];
  improvements: string[];
  rubric: { criterion: string; score: number }[];
  strongerAnswer?: string | null;
}

interface Session {
  id: number;
  targetRole: string;
  questionSource: "Ai" | "Curated";
  createdAtUtc: string;
  answeredCount: number;
  questions: Question[];
}

type Attempt = Question & {
  feedbackScore: number;
  roleTitle: string;
  source: string;
  createdAtUtc: string;
  sessionId: number;
  weakest: string | null;
};

/** Lowest average rubric criterion across the session; ties go alphabetically, as on the server. */
function weakestDimension(questions: Question[]): string | null {
  const scores = new Map<string, number[]>();
  for (const r of questions.flatMap((q) => q.rubric)) scores.set(r.criterion, [...(scores.get(r.criterion) ?? []), r.score]);
  const average = (list: number[]) => list.reduce((a, b) => a + b, 0) / list.length;
  return (
    [...scores].sort(([a, x], [b, y]) => average(x) - average(y) || (a < b ? -1 : 1))[0]?.[0] ?? null
  );
}

const toAttempt = (session: Session, q: Question): Attempt => ({
  ...q,
  feedbackScore: q.feedbackScore ?? 0,
  roleTitle: session.targetRole,
  source: session.questionSource === "Ai" ? "AI-Generated" : "Curated Bank",
  createdAtUtc: session.createdAtUtc,
  sessionId: session.id,
  weakest: weakestDimension(session.questions),
});

// Web Speech API: not in TypeScript's DOM lib, and only some browsers ship it.
const SpeechRecognitionImpl: any =
  (window as any).SpeechRecognition ?? (window as any).webkitSpeechRecognition;

const fmtDate = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: "short", timeStyle: "short" });

export function GraduateInterview() {
  const navigate = useNavigate();
  const [history, setHistory] = useState<Attempt[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const [questionSource, setQuestionSource] = useState<"ai" | "curated">("ai");
  const [roleTitle, setRoleTitle] = useState("Full-Stack / Backend Software Engineer");
  const [category, setCategory] = useState<Category>("Technical");
  const [difficulty, setDifficulty] = useState<Difficulty>("Junior");
  const [session, setSession] = useState<Session | null>(null);
  const [questionIdx, setQuestionIdx] = useState(0);
  const [answer, setAnswer] = useState("");
  const [generating, setGenerating] = useState(false);
  const [evaluating, setEvaluating] = useState(false);
  const [latestResult, setLatestResult] = useState<Attempt | null>(null);
  const recognition = useRef<any>(null);
  const [listening, setListening] = useState(false);

  const current = session?.questions[questionIdx] ?? null;
  const question = current?.questionText ?? "";
  const hasNext = !!session && questionIdx < session.questions.length - 1;

  useEffect(() => {
    document.title = "AI Interview Coach — UniSphere 360+";
    return () => recognition.current?.abort();
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const get = (path: string) => fetch(`${API_BASE_URL}${path}`, { headers: authHeaders() });
      const [listRes, profileRes] = await Promise.all([
        get("/api/grad/interview/sessions"),
        get("/api/grad/profile"),
      ]);
      if (listRes.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (!listRes.ok) throw new Error();
      if (profileRes.ok) {
        // Career calibration answers set the starting defaults.
        const profile = (await profileRes.json()) as {
          preferredJobTitles?: string | null;
          targetRole?: string | null;
          experienceLevel?: string | null;
          primaryStruggle?: string | null;
        };
        const title = profile.targetRole ?? profile.preferredJobTitles?.split(",")[0]?.trim();
        if (title) setRoleTitle(title);
        if (profile.experienceLevel === "Mid") setDifficulty("Mid-Level");
        if (profile.experienceLevel === "Senior") setDifficulty("Senior");
        if (profile.primaryStruggle === "System Design") setCategory("System Design");
      }
      // The list omits questions, so fetch the recent answered sessions for their feedback.
      const answered = ((await listRes.json()) as Session[]).filter((s) => s.answeredCount > 0).slice(0, 10);
      const details = await Promise.all(
        answered.map(async (s) => {
          const res = await get(`/api/grad/interview/sessions/${s.id}`);
          return res.ok ? ((await res.json()) as Session) : null;
        }),
      );
      const attempts = details.flatMap((s) =>
        s ? s.questions.filter((q) => q.feedback).map((q) => toAttempt(s, q)).reverse() : [],
      );
      setHistory(attempts);
      setLatestResult(attempts[0] ?? null);
    } catch {
      setError("Could not load Interview Coach.");
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  function showQuestion(idx: number, from: Session | null = session) {
    recognition.current?.abort();
    setQuestionIdx(idx);
    setAnswer(from?.questions[idx]?.answerText ?? "");
  }

  async function startSession() {
    setToast(null);
    setGenerating(true);
    try {
      // The API takes a single targetRole, so level and track travel inside it for AI questions.
      const targetRole =
        questionSource === "ai" ? `${difficulty} ${roleTitle.trim()} (${category})` : roleTitle.trim();
      const res = await fetch(`${API_BASE_URL}/api/grad/interview/sessions`, {
        method: "POST",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify({
          targetRole: targetRole.slice(0, 80),
          useAi: questionSource === "ai",
          interviewType: category === "System Design" ? "Technical" : category,
          difficulty: difficulty === "Junior" ? "Easy" : difficulty === "Mid-Level" ? "Medium" : "Hard",
        }),
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Could not start an interview session."), isError: true });
        return;
      }
      const created = (await res.json()) as Session;
      setSession(created);
      showQuestion(0, created);
    } catch {
      setToast({ text: "Could not reach the server to start a session.", isError: true });
    } finally {
      setGenerating(false);
    }
  }

  function toggleVoice() {
    if (listening) {
      recognition.current?.stop();
      return;
    }
    const rec = new SpeechRecognitionImpl();
    rec.continuous = true;
    rec.onresult = (ev: any) => {
      for (let i = ev.resultIndex; i < ev.results.length; i++) {
        const text = ev.results[i][0].transcript.trim();
        if (ev.results[i].isFinal && text) setAnswer((prev) => (prev ? `${prev} ${text}` : text));
      }
    };
    rec.onerror = (ev: any) => {
      if (ev.error !== "aborted") setToast({ text: `Voice input failed (${ev.error}).`, isError: true });
    };
    rec.onend = () => setListening(false);
    recognition.current = rec;
    setListening(true);
    rec.start();
  }

  async function evaluateAnswer(e: FormEvent) {
    e.preventDefault();
    if (!session || !current || answer.trim().length < 10) {
      setToast({ text: "Load a question and write an answer (at least 10 characters) first.", isError: true });
      return;
    }
    recognition.current?.stop();
    setEvaluating(true);
    setToast(null);
    try {
      const res = await fetch(
        `${API_BASE_URL}/api/grad/interview/sessions/${session.id}/questions/${current.id}/answer`,
        {
          method: "POST",
          headers: { ...authHeaders(), "Content-Type": "application/json" },
          body: JSON.stringify({ answerText: answer }),
        },
      );
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Evaluation failed."), isError: true });
        return;
      }
      const graded = (await res.json()) as Question;
      const updated = { ...session, questions: session.questions.map((q) => (q.id === graded.id ? graded : q)) };
      setSession(updated);
      const attempt = toAttempt(updated, graded);
      setLatestResult(attempt);
      setHistory((prev) => [attempt, ...prev.filter((h) => h.id !== attempt.id)]);
      setToast({ text: `Answer evaluated — Score: ${attempt.feedbackScore}/10.`, isError: false });
    } catch {
      setToast({ text: "Could not evaluate your answer.", isError: true });
    } finally {
      setEvaluating(false);
    }
  }

  const avgScore = history.length
    ? (history.reduce((sum, h) => sum + h.feedbackScore, 0) / history.length).toFixed(1)
    : null;
  const bestScore = history.length ? Math.max(...history.map((h) => h.feedbackScore)) : null;

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">AI Interview Coach</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Choose AI-generated or curated bank questions for your target role and receive instant rubric scoring &amp; coaching.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" to="/graduate">
            <UiIcon name="briefcase" />
            Career Overview
          </Button>
          <Button size="sm" to="/graduate/assistant">
            <UiIcon name="sparkles" />
            Open AI Assistant
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading ? (
        <LoadingBlock label="Loading AI Interview Coach…" />
      ) : (
        <>
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="mic" /></div>
              <span className="dash-stat-label">Completed Drills</span>
              <span className="dash-stat-value">{history.length}</span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="trending-up" /></div>
              <span className="dash-stat-label">Average Score</span>
              <span className="dash-stat-value">{avgScore !== null ? `${avgScore}/10` : "—"}</span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="award" /></div>
              <span className="dash-stat-label">Best Score</span>
              <span className="dash-stat-value">{bestScore !== null ? `${bestScore}/10` : "—"}</span>
            </div>
          </div>

          <div className="mb-6 grid grid-cols-1 gap-6 lg:grid-cols-12">
            {/* Mock Interview Simulator */}
            <Card className="lg:col-span-7">
              <CardHeader
                title="Interactive Mock Interview Session"
                action={
                  <div className="inline-flex rounded-control border border-border bg-bg p-0.5">
                    <button
                      type="button"
                      onClick={() => setQuestionSource("ai")}
                      className={`cursor-pointer rounded-md border-0 px-2.5 py-1 text-[11px] font-semibold transition ${
                        questionSource === "ai" ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
                      }`}
                    >
                      AI-generated
                    </button>
                    <button
                      type="button"
                      onClick={() => setQuestionSource("curated")}
                      className={`cursor-pointer rounded-md border-0 px-2.5 py-1 text-[11px] font-semibold transition ${
                        questionSource === "curated" ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
                      }`}
                    >
                      Curated bank
                    </button>
                  </div>
                }
              />
              <div className="flex flex-col gap-4 p-5 text-sm">
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <div>
                    <label className="mb-1 block text-xs font-medium text-text-secondary">Target Role</label>
                    <input
                      type="text"
                      value={roleTitle}
                      onChange={(e) => setRoleTitle(e.target.value)}
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block text-xs font-medium text-text-secondary">Track</label>
                    <select
                      value={category}
                      onChange={(e) => setCategory(e.target.value as Category)}
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    >
                      <option value="Technical">Technical</option>
                      <option value="HR">HR</option>
                      <option value="System Design">System Design</option>
                      <option value="Behavioral">Behavioral (STAR)</option>
                    </select>
                  </div>
                  <div>
                    <label className="mb-1 block text-xs font-medium text-text-secondary">Level</label>
                    <select
                      value={difficulty}
                      onChange={(e) => setDifficulty(e.target.value as Difficulty)}
                      disabled={questionSource === "curated"}
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    >
                      <option value="Junior">Junior / Fresh Grad</option>
                      <option value="Mid-Level">Mid-Level</option>
                      <option value="Senior">Senior</option>
                    </select>
                  </div>
                </div>

                <div>
                  <button
                    type="button"
                    disabled={generating}
                    onClick={() => (hasNext ? showQuestion(questionIdx + 1) : void startSession())}
                    className="btn-luminous inline-flex cursor-pointer items-center gap-2 rounded-control border-0 px-4 py-2 text-sm font-semibold disabled:opacity-60"
                  >
                    <UiIcon name={questionSource === "ai" ? "sparkles" : "book-open"} />
                    {generating
                      ? "Starting Session…"
                      : hasNext
                        ? `Next Question (${questionIdx + 2}/${session.questions.length})`
                        : questionSource === "ai"
                          ? "Start AI Session"
                          : "Start Curated Bank Session"}
                  </button>
                </div>

                {question && (
                  <div className="rounded-control border border-primary/35 bg-primary-light/40 p-4">
                    <div className="text-[11px] font-semibold uppercase tracking-wide text-primary">
                      {session?.questionSource === "Ai" ? "AI-Generated" : "Curated Bank"} · Question {questionIdx + 1} of{" "}
                      {session?.questions.length} · {session?.targetRole}
                    </div>
                    <p className="m-0 mt-1.5 text-sm font-medium leading-relaxed text-text">{question}</p>
                  </div>
                )}

                <form onSubmit={(e) => void evaluateAnswer(e)} className="flex flex-col gap-3">
                  <div>
                    <label className="mb-1 block text-xs font-medium text-text-secondary">Your Answer</label>
                    <textarea
                      rows={5}
                      value={answer}
                      onChange={(e) => setAnswer(e.target.value)}
                      placeholder="Structure your answer clearly (trade-offs, complexity, or Situation-Task-Action-Result)…"
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2.5 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {SpeechRecognitionImpl && (
                      <Button variant="secondary" disabled={!question || evaluating} onClick={toggleVoice}>
                        <UiIcon name="mic" />
                        {listening ? "Stop Recording" : "Answer by Voice"}
                      </Button>
                    )}
                    <Button type="submit" disabled={evaluating || !question}>
                      <UiIcon name="check-circle" />
                      {evaluating ? "Evaluating Answer…" : "Score & Coach My Answer"}
                    </Button>
                  </div>
                </form>
              </div>
            </Card>

            {/* Latest Coaching Feedback */}
            <Card className="lg:col-span-5">
              <CardHeader title="Coaching Feedback & Rubric" />
              <div className="p-5">
                {!latestResult ? (
                  <EmptyState
                    icon="mic"
                    message="Start a session and submit your answer to receive a structured coaching rubric."
                  />
                ) : (
                  <div className="flex flex-col gap-4 text-sm">
                    <div className="flex items-center justify-between rounded-control border border-border bg-bg/70 p-4">
                      <div>
                        <div className="text-xs font-semibold text-primary">
                          {latestResult.source}
                        </div>
                        <div className="text-xs text-text-secondary">{latestResult.roleTitle}</div>
                      </div>
                      <div className="dash-num text-2xl font-bold text-primary">{latestResult.feedbackScore}/10</div>
                    </div>

                    <p className="m-0 text-sm leading-relaxed text-text">{latestResult.feedback}</p>

                    {latestResult.rubric.length > 0 && (
                      <div className="flex flex-col gap-2">
                        {latestResult.rubric.map((r) => (
                          <div key={r.criterion} className="text-xs text-text-secondary">
                            <div className="mb-1 flex justify-between">
                              <span>{r.criterion}</span>
                              <span className="dash-num font-semibold text-text">{r.score}/10</span>
                            </div>
                            <progress className="h-1.5 w-full accent-primary" value={r.score} max={10} aria-label={r.criterion} />
                          </div>
                        ))}
                      </div>
                    )}

                    {latestResult.weakest && (
                      <RecoveryDrill
                        key={latestResult.sessionId}
                        sessionId={latestResult.sessionId}
                        weakest={latestResult.weakest}
                      />
                    )}

                    <div>
                      <div className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-success">
                        Strengths
                      </div>
                      <ul className="m-0 list-disc space-y-1 pl-4 text-xs text-text-secondary">
                        {latestResult.strengths.map((s, idx) => (
                          <li key={idx}>{s}</li>
                        ))}
                      </ul>
                    </div>

                    <div>
                      <div className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-warning">
                        Next-Level Improvements
                      </div>
                      <ul className="m-0 list-disc space-y-1 pl-4 text-xs text-text-secondary">
                        {latestResult.improvements.map((s, idx) => (
                          <li key={idx}>{s}</li>
                        ))}
                      </ul>
                    </div>

                    {latestResult.strongerAnswer && (
                      <div>
                        <div className="mb-1.5 text-xs font-semibold text-primary">A stronger answer could be</div>
                        <p className="m-0 whitespace-pre-wrap text-xs text-text-secondary">{latestResult.strongerAnswer}</p>
                      </div>
                    )}
                    <p className="m-0 text-xs text-text-muted">AI feedback is an estimate, not an employer decision or a certified assessment.</p>
                  </div>
                )}
              </div>
            </Card>
          </div>

          {weakestDimension(history) && history.length >= 3 && (
            <Alert kind="info">
              Across {history.length} graded answers your weakest area is {weakestDimension(history)}. Practise it with a recovery drill, then check your Career Journey.
            </Alert>
          )}

          {/* Questions of the running session */}
          {session && (
            <Card className="mb-6 overflow-hidden">
              <CardHeader title={`Session Questions (${session.questions.length})`} />
              <div className="grid grid-cols-1 gap-3 p-5 md:grid-cols-2">
                {session.questions.map((q, idx) => (
                  <button
                    key={q.id}
                    type="button"
                    onClick={() => {
                      showQuestion(idx);
                      if (q.feedback) setLatestResult(toAttempt(session, q));
                      window.scrollTo({ top: 0, behavior: "smooth" });
                    }}
                    className="flex cursor-pointer flex-col gap-1.5 rounded-control border border-border bg-bg/70 p-4 text-left transition hover:border-primary/60 hover:bg-primary-light/25"
                  >
                    <div className="flex flex-wrap items-center gap-2 text-[11px] font-semibold">
                      <span className="rounded bg-primary-light px-2 py-0.5 text-primary">Q{idx + 1}</span>
                      <span className="text-text-secondary">
                        {q.feedbackScore === null ? "Not scored" : `${q.feedbackScore}/10`}
                      </span>
                    </div>
                    <p className="m-0 text-xs leading-relaxed text-text">{q.questionText}</p>
                  </button>
                ))}
              </div>
            </Card>
          )}

          {/* Past Interview Attempts */}
          <Card className="overflow-hidden">
            <CardHeader title={`Practice History (${history.length})`} />
            {history.length === 0 ? (
              <EmptyState icon="history" message="Your completed mock interview sessions will appear here." />
            ) : (
              <div className="divide-y divide-border">
                {history.map((item) => (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => setLatestResult(item)}
                    className="flex w-full cursor-pointer flex-wrap items-center justify-between gap-3 border-0 bg-transparent px-5 py-3.5 text-left transition hover:bg-primary-light/30"
                  >
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-sm font-semibold text-text">{item.questionText}</div>
                      <div className="mt-0.5 text-xs text-text-secondary">
                        {item.roleTitle} · {item.source} · {fmtDate(item.createdAtUtc)}
                      </div>
                    </div>
                    <span className="dash-num rounded-md bg-primary-light px-2.5 py-1 text-xs font-bold text-primary">
                      {item.feedbackScore}/10
                    </span>
                  </button>
                ))}
              </div>
            )}
          </Card>
        </>
      )}
    </div>
  );
}
