import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken } from "../lib/api";
import "./dashboard.css";

/*
 * My Exams: React port of Pages/Student/Exams.razor on top of
 * /api/student/exams (list, detail) and the attempt endpoints
 * (/attempts, /{id}/attempt, /{id}/start, /{id}/submit). Correct answers
 * only ever arrive from the server after submit, when the exam allows it.
 */

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
interface ExamDetail extends ExamRow {
  questions: { id: number; questionText: string; optionsJson: string | null }[];
}
interface AttemptRow { examId: number; submittedAtUtc: string | null; score: number | null; maxScore: number | null }
interface AttemptState {
  examId: number;
  opensAtUtc: string | null;
  closesAtUtc: string | null;
  durationMinutes: number | null;
  showAnswersAfterSubmit: boolean;
  attempt: { startedAtUtc: string; submittedAtUtc: string | null; score: number | null; maxScore: number | null } | null;
  takeQuestions: { questionId: number; questionText: string; optionsJson: string | null }[] | null;
  review: { questionId: number; questionText: string; optionsJson: string | null; selectedOption: number | null; isCorrect: boolean | null; correctOption: number | null }[] | null;
}

function parseOptions(json: string | null): string[] {
  try {
    const value: unknown = JSON.parse(json ?? "[]");
    return Array.isArray(value) ? value.map(String) : [];
  } catch {
    return [];
  }
}

const fmt = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

function chipFor(a: AttemptRow | undefined): string {
  if (!a) return "Available";
  if (!a.submittedAtUtc) return "In progress";
  const pct = a.maxScore ? (100 * (a.score ?? 0)) / a.maxScore : 0;
  return pct >= 50 ? "Passed" : "Needs attention";
}

/** Same mapping as AttemptErrorAsync in the Razor page. */
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

  useEffect(() => { void load(); }, [load]);

  async function open(id: number) {
    setBusy(true);
    setToast(null);
    setSelections({});
    try {
      const res = await api(`/${id}`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setExam((await res.json()) as ExamDetail);
      // A missing attempt (404) just means "not started yet".
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
      setToast({ text: "Attempt started — pick your answers and submit before the exam closes.", isError: false });
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
      // Only the option CHOICE is sent; the server grades it.
      const answers = state.takeQuestions.map((q) => ({ questionId: q.questionId, selectedOption: selections[q.questionId] ?? null }));
      const res = await api(`/${state.examId}/submit`, { method: "POST", body: JSON.stringify({ answers }) });
      if (!res.ok) return setToast({ text: await attemptError(res, "The submission failed. Try again before the exam closes."), isError: true });
      const next = (await res.json()) as AttemptState;
      setState(next);
      setToast({ text: `Submitted. Score: ${next.attempt?.score ?? 0}/${next.attempt?.maxScore ?? answers.length}`, isError: false });
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
    setToast(null);
  }

  const header = (
    <div className="mb-6">
      <h1 className="m-0 text-2xl font-semibold">My Exams</h1>
      <p className="mt-1 text-sm text-text-secondary">Published exams for your currently registered courses.</p>
    </div>
  );
  const toastEl = toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>;

  if (exam) {
    const attempt = state?.attempt ?? null;
    return (
      <div className="dashboard">
        {header}
        {toastEl}
        <Card className="overflow-hidden">
          <CardHeader title={exam.title} action={<Button size="sm" variant="secondary" onClick={close}>Back</Button>} />
          <div className="flex flex-col gap-4 px-5 py-4">
            <Meta>
              <span>{exam.courseTitle} ({exam.courseCode})</span>
              <span>{exam.questionCount} questions</span>
              {exam.marks != null && <span>Marks: {exam.marks}</span>}
              {state?.opensAtUtc && <span>Opens: {fmt(state.opensAtUtc)}</span>}
              {state?.closesAtUtc && <span>Closes: {fmt(state.closesAtUtc)}</span>}
              {state?.durationMinutes != null && <span>Duration: {state.durationMinutes} min</span>}
            </Meta>

            {!attempt ? (
              <>
                {exam.questions.map((q, i) => (
                  <div key={q.id} className="rounded-control border border-border p-3 text-sm">
                    <div className="font-medium">{i + 1}. {q.questionText}</div>
                    <ul className="mb-0 mt-2 text-text-secondary">
                      {parseOptions(q.optionsJson).map((o, oi) => <li key={oi}>{o}</li>)}
                    </ul>
                  </div>
                ))}
                <p className="m-0 text-xs text-text-secondary">Answers are not shown for published exams — start the attempt to record your choices.</p>
                <div><Button onClick={() => void start()} disabled={busy}>{busy ? "Starting…" : "Start attempt"}</Button></div>
              </>
            ) : !attempt.submittedAtUtc ? (
              <>
                <p className="m-0 text-xs text-text-secondary">
                  Attempt started {fmt(attempt.startedAtUtc)}.{state?.closesAtUtc && ` Submit before ${fmt(state.closesAtUtc)}.`}
                </p>
                {(state?.takeQuestions ?? []).map((q, i) => (
                  <fieldset key={q.questionId} className="m-0 rounded-control border border-border p-3 text-sm">
                    <legend className="px-1 font-medium">{i + 1}. {q.questionText}</legend>
                    {parseOptions(q.optionsJson).map((o, oi) => (
                      <label key={oi} className="flex cursor-pointer items-center gap-2 py-1">
                        <input
                          type="radio"
                          name={`q_${q.questionId}`}
                          checked={selections[q.questionId] === oi}
                          onChange={() => setSelections((s) => ({ ...s, [q.questionId]: oi }))}
                        />
                        {o}
                      </label>
                    ))}
                  </fieldset>
                ))}
                <div><Button onClick={() => void submit()} disabled={busy}>{busy ? "Submitting…" : "Submit answers"}</Button></div>
              </>
            ) : (
              <>
                <div>
                  <h4 className="m-0 text-lg font-semibold">Score: {attempt.score ?? 0} / {attempt.maxScore ?? exam.questionCount}</h4>
                  <p className="m-0 mt-1 text-xs text-text-secondary">Submitted {fmt(attempt.submittedAtUtc)}</p>
                </div>
                {!state?.showAnswersAfterSubmit ? (
                  <p className="m-0 text-sm text-text-secondary">Correct answers are not shown for this exam.</p>
                ) : (
                  (state.review ?? []).map((r) => {
                    const options = parseOptions(r.optionsJson);
                    const status = r.isCorrect ? "Passed" : r.selectedOption == null ? "Unanswered" : "Needs attention";
                    return (
                      <div key={r.questionId} className="rounded-control border border-border p-3 text-sm">
                        <div className="mb-2 flex items-start justify-between gap-3">
                          <span className="font-medium">{r.questionText}</span>
                          <StatusBadge status={status} />
                        </div>
                        <div>Your answer: {r.selectedOption != null ? options[r.selectedOption] ?? "?" : "— unanswered —"}</div>
                        {!r.isCorrect && r.correctOption != null && <div>Correct answer: {options[r.correctOption] ?? "?"}</div>}
                      </div>
                    );
                  })
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
              <thead><tr><th>Exam</th><th>Course</th><th>Questions</th><th>Published</th><th>Status</th><th /></tr></thead>
              <tbody>
                {[...exams]
                  .sort((a, b) => (b.publishedAtUtc ?? "").localeCompare(a.publishedAtUtc ?? ""))
                  .map((e) => {
                    const a = attempts.find((x) => x.examId === e.id);
                    return (
                      <tr key={e.id}>
                        <td className="font-medium">{e.title}</td>
                        <td>{e.courseTitle}</td>
                        <td className="dash-num">{e.questionCount}{e.marks != null && ` · ${e.marks} marks`}</td>
                        <td className="dash-num">{fmt(e.publishedAtUtc ?? e.createdAtUtc)}</td>
                        <td>
                          <StatusBadge status={chipFor(a)} />
                          {a?.submittedAtUtc && <span className="dash-num ml-2 text-xs text-text-secondary">{a.score ?? 0}/{a.maxScore}</span>}
                        </td>
                        <td><Button size="sm" variant="secondary" onClick={() => void open(e.id)} disabled={busy}>Open</Button></td>
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
