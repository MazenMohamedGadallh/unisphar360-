import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface ExamRow {
  id: number;
  courseCode: string;
  courseTitle: string;
  title: string;
  questionCount: number;
  marks: number | null;
  createdAtUtc: string;
  publishedAtUtc: string | null;
}

interface ExamQuestionPreview {
  id: number;
  orderIndex?: number;
  questionType?: string;
  questionText: string;
  optionsJson: string | null;
  marks?: number;
}

interface ExamDetail extends ExamRow {
  questions: ExamQuestionPreview[];
}

interface AttemptRow {
  examId: number;
  submittedAtUtc: string | null;
  score: number | null;
  maxScore: number | null;
}

interface TakeQuestion {
  questionId: number;
  orderIndex?: number;
  questionType?: string;
  questionText: string;
  optionsJson: string | null;
  marks?: number;
}

interface ReviewQuestion {
  questionId: number;
  orderIndex?: number;
  questionType?: string;
  questionText: string;
  optionsJson: string | null;
  selectedOption: number | null;
  textAnswer: string | null;
  isCorrect: boolean | null;
  correctOption: number | null;
  modelAnswer: string | null;
  pendingGrading?: boolean;
}

interface AttemptState {
  examId: number;
  opensAtUtc: string | null;
  closesAtUtc: string | null;
  durationMinutes: number | null;
  showAnswersAfterSubmit: boolean;
  attempt: { startedAtUtc: string; submittedAtUtc: string | null; score: number | null; maxScore: number | null } | null;
  takeQuestions: TakeQuestion[] | null;
  review: ReviewQuestion[] | null;
}

function parseOptions(json: string | null): string[] {
  try {
    const value: unknown = JSON.parse(json ?? "[]");
    return Array.isArray(value) ? value.map(String) : [];
  } catch {
    return [];
  }
}

function isWritten(questionType?: string, optionsJson?: string | null): boolean {
  const t = (questionType ?? "").toLowerCase();
  if (t === "essay" || t === "definition" || t === "shortanswer") return true;
  if (t === "mcq" || t === "truefalse") return false;
  return parseOptions(optionsJson ?? null).length === 0;
}

function questionTypeLabel(questionType?: string): string {
  switch ((questionType ?? "MCQ").toLowerCase()) {
    case "truefalse":
      return "True / False";
    case "essay":
      return "Essay (Written)";
    case "definition":
      return "Definition (Written)";
    default:
      return "Multiple Choice";
  }
}

const fmt = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

function chipFor(a: AttemptRow | undefined): string {
  if (!a) return "Available";
  if (!a.submittedAtUtc) return "In progress";
  const pct = a.maxScore ? (100 * (a.score ?? 0)) / a.maxScore : 0;
  return pct >= 50 ? "Passed" : "Needs attention";
}

async function attemptError(res: Response, fallback: string): Promise<string> {
  const body = await res.text();
  if (res.status === 409) {
    if (/already submitted/i.test(body)) return "You already submitted this exam. A second attempt is not allowed.";
    if (/not open yet/i.test(body)) return "This exam is not open yet. Check the opens-at date.";
    return "This exam's submission window is closed or the attempt is already submitted.";
  }
  return fallback;
}

const Meta = ({ children }: { children: ReactNode }) => (
  <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-text-secondary">{children}</div>
);

export function Exams() {
  const navigate = useNavigate();
  const [exams, setExams] = useState<ExamRow[] | null>(null);
  const [attempts, setAttempts] = useState<AttemptRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [exam, setExam] = useState<ExamDetail | null>(null);
  const [state, setState] = useState<AttemptState | null>(null);
  const [selections, setSelections] = useState<Record<number, number>>({});
  const [writtenAnswers, setWrittenAnswers] = useState<Record<number, string>>({});
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const api = useCallback(
    async (path: string, init?: RequestInit) => {
      const res = await fetch(`${API_BASE_URL}/api/student/exams${path}`, {
        ...init,
        headers: { ...authHeaders(), ...(init?.body ? { "Content-Type": "application/json" } : {}) },
      });
      if (res.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        throw new DOMException("expired", "AbortError");
      }
      return res;
    },
    [navigate],
  );

  const loadAttempts = useCallback(async () => {
    try {
      const res = await api("/attempts");
      setAttempts(res.ok ? ((await res.json()) as AttemptRow[]) : []);
    } catch {
      // List chips are best-effort.
    }
  }, [api]);

  const load = useCallback(async () => {
    setError(null);
    try {
      const res = await api("");
      if (res.status === 403) return setError("My Exams is available for students only.");
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setExams((await res.json()) as ExamRow[]);
      await loadAttempts();
    } catch (e) {
      if ((e as Error).name !== "AbortError") setError("Could not load your exams. Check your connection and try again.");
    }
  }, [api, loadAttempts]);

  useEffect(() => {
    void load();
  }, [load]);

  async function open(id: number) {
    setBusy(true);
    setToast(null);
    setSelections({});
    setWrittenAnswers({});
    try {
      const res = await api(`/${id}`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setExam((await res.json()) as ExamDetail);
      const st = await api(`/${id}/attempt`);
      setState(st.ok ? ((await st.json()) as AttemptState) : null);
    } catch (e) {
      if ((e as Error).name !== "AbortError") setToast({ text: "Could not open this exam. Please try again.", isError: true });
    } finally {
      setBusy(false);
    }
  }

  async function start() {
    if (!exam) return;
    setBusy(true);
    setToast(null);
    try {
      const res = await api(`/${exam.id}/start`, { method: "POST", body: "{}" });
      if (!res.ok) return setToast({ text: await attemptError(res, "The attempt could not be started."), isError: true });
      setState((await res.json()) as AttemptState);
      setToast({ text: "Attempt started — answer all objective & written questions, then submit.", isError: false });
      void loadAttempts();
    } catch (e) {
      if ((e as Error).name !== "AbortError") setToast({ text: "The attempt could not be started.", isError: true });
    } finally {
      setBusy(false);
    }
  }

  async function submit() {
    if (!state?.takeQuestions?.length) return;
    setBusy(true);
    setToast(null);
    try {
      const answers = state.takeQuestions.map((q) => {
        if (isWritten(q.questionType, q.optionsJson)) {
          return {
            questionId: q.questionId,
            selectedOption: null,
            textAnswer: (writtenAnswers[q.questionId] ?? "").trim() || null,
          };
        }
        return {
          questionId: q.questionId,
          selectedOption: selections[q.questionId] ?? null,
          textAnswer: null,
        };
      });
      const res = await api(`/${state.examId}/submit`, { method: "POST", body: JSON.stringify({ answers }) });
      if (!res.ok) return setToast({ text: await attemptError(res, "The submission failed. Try again before the exam closes."), isError: true });
      const next = (await res.json()) as AttemptState;
      setState(next);
      const hasPending = (next.review ?? []).some((r) => r.pendingGrading);
      setToast({
        text: hasPending
          ? `Submitted! Auto-graded score: ${next.attempt?.score ?? 0}/${next.attempt?.maxScore ?? answers.length}. Written answers await instructor review.`
          : `Submitted. Score: ${next.attempt?.score ?? 0}/${next.attempt?.maxScore ?? answers.length}`,
        isError: false,
      });
      void loadAttempts();
    } catch (e) {
      if ((e as Error).name !== "AbortError") setToast({ text: "The submission failed. Try again before the exam closes.", isError: true });
    } finally {
      setBusy(false);
    }
  }

  function close() {
    setExam(null);
    setState(null);
    setSelections({});
    setWrittenAnswers({});
    setToast(null);
  }

  const header = (
    <div className="mb-6">
      <h1 className="m-0 text-2xl font-semibold text-text">My Exams</h1>
      <p className="mt-1 text-sm text-text-secondary">
        Published mixed-format exams (MCQ, True/False, Definition &amp; Essay) for your registered courses.
      </p>
    </div>
  );
  const toastEl = toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>;

  if (exam) {
    const attempt = state?.attempt ?? null;
    const pendingCount = (state?.review ?? []).filter((r) => r.pendingGrading).length;

    return (
      <div className="dashboard">
        {header}
        {toastEl}
        <Card className="overflow-hidden">
          <CardHeader title={exam.title} action={<Button size="sm" variant="secondary" onClick={close}>Back to Exams</Button>} />
          <div className="flex flex-col gap-4 px-5 py-5">
            <Meta>
              <span className="font-medium text-text">{exam.courseTitle} ({exam.courseCode})</span>
              <span>{exam.questionCount} questions</span>
              {exam.marks != null && <span className="dash-num">Total Marks: {exam.marks}</span>}
              {state?.opensAtUtc && <span>Opens: {fmt(state.opensAtUtc)}</span>}
              {state?.closesAtUtc && <span>Closes: {fmt(state.closesAtUtc)}</span>}
              {state?.durationMinutes != null && <span className="dash-num">Duration: {state.durationMinutes} min</span>}
            </Meta>

            {!attempt ? (
              <>
                <div className="flex flex-col gap-3">
                  {exam.questions.map((q, i) => {
                    const written = isWritten(q.questionType, q.optionsJson);
                    return (
                      <div key={q.id} className="rounded-control border border-border bg-bg/60 p-4 text-sm">
                        <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                          <span className="font-semibold text-text">
                            {i + 1}. {q.questionText}
                          </span>
                          <span className="rounded-md bg-primary-light px-2 py-0.5 text-[11px] font-semibold text-primary">
                            {questionTypeLabel(q.questionType)}{q.marks ? ` · ${q.marks} pts` : ""}
                          </span>
                        </div>
                        {written ? (
                          <p className="m-0 text-xs italic text-text-secondary">
                            Written response question — start the attempt to type your answer.
                          </p>
                        ) : (
                          <ul className="mb-0 mt-1 list-disc pl-5 text-text-secondary">
                            {parseOptions(q.optionsJson).map((o, oi) => (
                              <li key={oi}>{o}</li>
                            ))}
                          </ul>
                        )}
                      </div>
                    );
                  })}
                </div>
                <p className="m-0 text-xs text-text-secondary">
                  Correct answers are sealed until submission. Objective items are auto-graded; written answers are reviewed by your doctor.
                </p>
                <div>
                  <Button onClick={() => void start()} disabled={busy}>
                    <UiIcon name="play-circle" />
                    {busy ? "Starting…" : "Start Exam Attempt"}
                  </Button>
                </div>
              </>
            ) : !attempt.submittedAtUtc ? (
              <>
                <div className="flex flex-wrap items-center justify-between gap-2 rounded-control border border-primary/25 bg-primary-light/60 px-4 py-2.5 text-xs text-primary">
                  <span>Attempt in progress — started {fmt(attempt.startedAtUtc)}.</span>
                  {state?.closesAtUtc && <span>Closes {fmt(state.closesAtUtc)}</span>}
                </div>
                <div className="flex flex-col gap-3.5">
                  {(state?.takeQuestions ?? []).map((q, i) => {
                    const written = isWritten(q.questionType, q.optionsJson);
                    return (
                      <fieldset key={q.questionId} className="m-0 rounded-control border border-border bg-card p-4 text-sm">
                        <div className="mb-2.5 flex flex-wrap items-start justify-between gap-2">
                          <legend className="p-0 font-semibold text-text">
                            {i + 1}. {q.questionText}
                          </legend>
                          <span className="shrink-0 rounded-md bg-primary-light px-2 py-0.5 text-[11px] font-semibold text-primary">
                            {questionTypeLabel(q.questionType)}{q.marks ? ` · ${q.marks} pts` : ""}
                          </span>
                        </div>
                        {written ? (
                          <textarea
                            rows={q.questionType === "Essay" ? 5 : 3}
                            className="w-full rounded-control border border-border bg-bg px-3.5 py-2.5 text-sm text-text outline-none transition focus:border-primary"
                            placeholder={
                              q.questionType === "Definition"
                                ? "Write a clear, concise academic definition…"
                                : "Write your full structured explanation…"
                            }
                            value={writtenAnswers[q.questionId] ?? ""}
                            onChange={(e) =>
                              setWrittenAnswers((prev) => ({ ...prev, [q.questionId]: e.target.value }))
                            }
                          />
                        ) : (
                          <div className="flex flex-col gap-1.5">
                            {parseOptions(q.optionsJson).map((o, oi) => {
                              const active = selections[q.questionId] === oi;
                              return (
                                <label
                                  key={oi}
                                  className={`flex cursor-pointer items-center gap-2.5 rounded-control border px-3 py-2 transition ${
                                    active
                                      ? "border-primary bg-primary-light/70 font-medium text-text"
                                      : "border-transparent bg-bg/50 text-text-secondary hover:border-border hover:text-text"
                                  }`}
                                >
                                  <input
                                    type="radio"
                                    name={`q_${q.questionId}`}
                                    checked={active}
                                    onChange={() => setSelections((s) => ({ ...s, [q.questionId]: oi }))}
                                  />
                                  <span>{o}</span>
                                </label>
                              );
                            })}
                          </div>
                        )}
                      </fieldset>
                    );
                  })}
                </div>
                <div className="pt-1">
                  <Button onClick={() => void submit()} disabled={busy}>
                    <UiIcon name="check-circle" />
                    {busy ? "Submitting…" : "Submit Answers"}
                  </Button>
                </div>
              </>
            ) : (
              <>
                <div className="flex flex-wrap items-center justify-between gap-4 rounded-control border border-border bg-bg/70 px-5 py-4">
                  <div>
                    <div className="text-xs font-semibold uppercase tracking-wide text-text-secondary">Exam Result</div>
                    <h4 className="dash-num m-0 mt-0.5 text-2xl font-bold text-text">
                      {attempt.score ?? 0} / {attempt.maxScore ?? exam.questionCount}
                    </h4>
                    <p className="m-0 mt-1 text-xs text-text-secondary">Submitted {fmt(attempt.submittedAtUtc)}</p>
                  </div>
                  {pendingCount > 0 && (
                    <div className="rounded-control border border-warning/40 bg-warning-light px-3.5 py-2 text-xs font-medium text-warning">
                      {pendingCount} written {pendingCount === 1 ? "answer" : "answers"} awaiting doctor grading
                    </div>
                  )}
                </div>

                {!state?.showAnswersAfterSubmit ? (
                  <p className="m-0 text-sm text-text-secondary">Correct answers are hidden for this exam.</p>
                ) : (
                  <div className="flex flex-col gap-3">
                    {(state.review ?? []).map((r, idx) => {
                      const options = parseOptions(r.optionsJson);
                      const written = isWritten(r.questionType, r.optionsJson);
                      const status = r.pendingGrading
                        ? "In progress"
                        : r.isCorrect
                          ? "Passed"
                          : written
                            ? r.textAnswer
                              ? "Needs attention"
                              : "Unanswered"
                            : r.selectedOption == null
                              ? "Unanswered"
                              : "Needs attention";
                      const badgeLabel = r.pendingGrading ? "Pending grading" : status;
                      return (
                        <div key={r.questionId} className="rounded-control border border-border bg-card p-4 text-sm">
                          <div className="mb-2 flex flex-wrap items-start justify-between gap-3">
                            <div>
                              <span className="mr-2 rounded bg-primary-light px-1.5 py-0.5 text-[11px] font-semibold text-primary">
                                {questionTypeLabel(r.questionType)}
                              </span>
                              <span className="font-semibold text-text">
                                {idx + 1}. {r.questionText}
                              </span>
                            </div>
                            <StatusBadge status={badgeLabel} />
                          </div>
                          {written ? (
                            <div className="flex flex-col gap-2 text-sm">
                              <div className="rounded-control bg-bg/70 p-3">
                                <div className="text-[11px] font-semibold uppercase tracking-wide text-text-secondary">
                                  Your written response
                                </div>
                                <div className="mt-1 whitespace-pre-wrap text-text">
                                  {r.textAnswer || "— No answer submitted —"}
                                </div>
                              </div>
                              {r.modelAnswer && (
                                <div className="rounded-control border border-primary/20 bg-primary-light/40 p-3">
                                  <div className="text-[11px] font-semibold uppercase tracking-wide text-primary">
                                    Reference / Model Answer
                                  </div>
                                  <div className="mt-1 whitespace-pre-wrap text-text">{r.modelAnswer}</div>
                                </div>
                              )}
                            </div>
                          ) : (
                            <div className="flex flex-col gap-1 text-sm">
                              <div className="text-text">
                                <span className="text-text-secondary">Your answer: </span>
                                {r.selectedOption != null ? options[r.selectedOption] ?? "?" : "— unanswered —"}
                              </div>
                              {!r.isCorrect && r.correctOption != null && (
                                <div className="font-medium text-success">
                                  Correct answer: {options[r.correctOption] ?? "?"}
                                </div>
                              )}
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                )}
              </>
            )}
          </div>
        </Card>
      </div>
    );
  }

  return (
    <div className="dashboard">
      {header}
      {toastEl}
      {error ? (
        <Card><ErrorState message={error} onRetry={() => void load()} /></Card>
      ) : exams === null ? (
        <LoadingBlock label="Loading your exams…" />
      ) : exams.length === 0 ? (
        <Card><EmptyState icon="clipboard-check" message="No exams available yet." /></Card>
      ) : (
        <Card className="overflow-hidden">
          <div className="dash-table-wrap">
            <table className="dash-table">
              <thead>
                <tr>
                  <th>Exam</th>
                  <th>Course</th>
                  <th>Questions</th>
                  <th>Published</th>
                  <th>Status</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {[...exams]
                  .sort((a, b) => (b.publishedAtUtc ?? "").localeCompare(a.publishedAtUtc ?? ""))
                  .map((e) => {
                    const a = attempts.find((x) => x.examId === e.id);
                    return (
                      <tr key={e.id}>
                        <td className="font-medium text-text">{e.title}</td>
                        <td>
                          <span className="font-medium text-primary">{e.courseCode}</span>
                          <span className="ml-1.5 text-text-secondary">{e.courseTitle}</span>
                        </td>
                        <td className="dash-num">
                          {e.questionCount}
                          {e.marks != null && ` · ${e.marks} marks`}
                        </td>
                        <td className="dash-num text-text-secondary">{fmt(e.publishedAtUtc ?? e.createdAtUtc)}</td>
                        <td>
                          <StatusBadge status={chipFor(a)} />
                          {a?.submittedAtUtc && (
                            <span className="dash-num ml-2 text-xs font-semibold text-text-secondary">
                              {a.score ?? 0}/{a.maxScore}
                            </span>
                          )}
                        </td>
                        <td className="text-right">
                          <Button size="sm" variant="secondary" onClick={() => void open(e.id)} disabled={busy}>
                            {a?.submittedAtUtc ? "Review" : "Open"}
                          </Button>
                        </td>
                      </tr>
                    );
                  })}
              </tbody>
            </table>
          </div>
        </Card>
      )}
    </div>
  );
}
