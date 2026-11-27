import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface ExamItem {
  id: number;
  courseCode: string;
  title: string;
  status: string;
}

interface TakerRow {
  studentUserId: number;
  fullName: string;
  submittedAtUtc: string;
  score: number;
  maxScore: number;
  percent: number | null;
  pendingGrading: number;
}

interface Results {
  examTitle: string;
  courseCode: string;
  courseTitle: string;
  questionCount: number;
  marks: number | null;
  status: string;
  opensAtUtc: string | null;
  closesAtUtc: string | null;
  durationMinutes: number | null;
  eligibleCount: number;
  takers: TakerRow[];
  nonTakers: { studentUserId: number; fullName: string; email: string | null }[];
}

interface WrittenAnswerItem {
  answerId: number;
  studentUserId: number;
  studentName: string;
  questionId: number;
  orderIndex: number;
  questionType: string;
  questionText: string;
  modelAnswer: string | null;
  textAnswer: string;
  isCorrect: boolean;
  graded: boolean;
}

const BASE = `${API_BASE_URL}/api/doctor/exam-assistant/exams`;
const fmt = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

export function ExamResults() {
  const navigate = useNavigate();
  const [exams, setExams] = useState<ExamItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState(0);
  const [results, setResults] = useState<Results | null | undefined>(null);
  const [writtenAnswers, setWrittenAnswers] = useState<WrittenAnswerItem[]>([]);
  const [gradingId, setGradingId] = useState<number | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const req = useCallback(
    async (url: string, init?: RequestInit) => {
      const res = await fetch(url, {
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

  const select = useCallback(
    async (id: number) => {
      setSelected(id);
      setResults(null);
      setWrittenAnswers([]);
      if (id <= 0) return;
      setResults(undefined);
      try {
        const [res, wRes] = await Promise.all([
          req(`${BASE}/${id}/results`),
          req(`${BASE}/${id}/written-answers`),
        ]);
        if (res.status === 404) {
          setResults(null);
          return;
        }
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        setResults((await res.json()) as Results);
        if (wRes.ok) {
          setWrittenAnswers((await wRes.json()) as WrittenAnswerItem[]);
        }
      } catch (e) {
        if ((e as Error).name === "AbortError") return;
        setResults(null);
        setError("Could not load results for this exam. Please try again.");
      }
    },
    [req],
  );

  const load = useCallback(async () => {
    setError(null);
    try {
      const res = await req(BASE);
      if (res.status === 403) return setError("This page is only available to doctors.");
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const published = ((await res.json()) as ExamItem[]).filter((e) => e.status === "Published");
      setExams(published);
      if (published.length > 0 && selected === 0) {
        // Default to the first exam that has submissions (Exam 2 in seed has a pending written answer)
        const preferred = published.find((e) => e.id === 2) ?? published[0];
        void select(preferred.id);
      }
    } catch (e) {
      if ((e as Error).name !== "AbortError") setError("Could not load your exams. Check your connection and try again.");
    }
  }, [req, select, selected]);

  useEffect(() => {
    void load();
  }, [load]);

  async function gradeWritten(answerId: number, isCorrect: boolean) {
    if (selected <= 0) return;
    setGradingId(answerId);
    setToast(null);
    try {
      const res = await req(`${BASE}/${selected}/written-answers/${answerId}/grade`, {
        method: "POST",
        body: JSON.stringify({ isCorrect }),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const updated = (await res.json()) as WrittenAnswerItem;
      setWrittenAnswers((prev) => prev.map((w) => (w.answerId === answerId ? updated : w)));
      // Refresh results so the student's live score, percent, and To grade column update immediately
      const refreshed = await req(`${BASE}/${selected}/results`);
      if (refreshed.ok) setResults((await refreshed.json()) as Results);
      setToast(`Marked ${updated.studentName}'s written answer as ${isCorrect ? "Correct" : "Incorrect"}. Score recalculated.`);
    } catch (e) {
      if ((e as Error).name !== "AbortError") setError("Failed to save written answer grade.");
    } finally {
      setGradingId(null);
    }
  }

  const pendingTotal = writtenAnswers.filter((w) => !w.graded).length;

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Exam Results &amp; Written Grading</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Review student scores, submission coverage, and manually grade Essay &amp; Definition answers with live score recalculation.
          </p>
        </div>
        <Button variant="secondary" to="/doctor/exam-assistant">
          <UiIcon name="sparkles" />
          Open Exam Assistant
        </Button>
      </div>

      {toast && <Alert kind="success">{toast}</Alert>}

      {error ? (
        <Card><ErrorState message={error} onRetry={() => void (selected > 0 ? select(selected) : load())} /></Card>
      ) : exams === null ? (
        <LoadingBlock label="Loading your exams…" />
      ) : exams.length === 0 ? (
        <Card><EmptyState icon="clipboard-check" message="No published exams yet. Publish an exam from the Exam Assistant first." /></Card>
      ) : (
        <>
          <Card className="mb-6 overflow-hidden">
            <div className="flex flex-col gap-2 px-5 py-4 text-sm sm:flex-row sm:items-center sm:justify-between">
              <div className="flex flex-1 flex-col gap-1.5">
                <label htmlFor="exam-select" className="font-medium text-text">Select Published Exam</label>
                <select
                  id="exam-select"
                  className="max-w-xl rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                  value={selected}
                  onChange={(e) => void select(Number(e.target.value))}
                >
                  <option value={0}>Select an exam…</option>
                  {exams.map((e) => (
                    <option key={e.id} value={e.id}>
                      {e.title} ({e.courseCode})
                    </option>
                  ))}
                </select>
              </div>
              {pendingTotal > 0 && (
                <div className="rounded-control border border-warning/40 bg-warning-light px-3.5 py-2 text-xs font-semibold text-warning">
                  {pendingTotal} written {pendingTotal === 1 ? "answer" : "answers"} to grade
                </div>
              )}
            </div>
          </Card>

          {results === undefined ? (
            <LoadingBlock label="Loading results…" />
          ) : results === null ? (
            selected > 0 && <Card><EmptyState icon="clipboard-check" message="No results available for this exam." /></Card>
          ) : (
            <>
              <div className="grid grid-cols-1 gap-4 mb-6 sm:grid-cols-2 lg:grid-cols-4">
                <div className="dash-stat courses">
                  <span className="dash-stat-label">Eligible Students</span>
                  <span className="dash-stat-value">{results.eligibleCount}</span>
                </div>
                <div className="dash-stat gpa">
                  <span className="dash-stat-label">Submitted</span>
                  <span className="dash-stat-value">{results.takers.length}</span>
                </div>
                <div className="dash-stat attendance">
                  <span className="dash-stat-label">Not Taken</span>
                  <span className="dash-stat-value">{results.nonTakers.length}</span>
                </div>
                <div className="dash-stat recommended">
                  <span className="dash-stat-label">Pending Grading</span>
                  <span className="dash-stat-value">{pendingTotal}</span>
                </div>
              </div>

              <p className="mb-4 mt-0 text-xs text-text-secondary">
                {results.courseTitle} ({results.courseCode}) · {results.questionCount} questions
                {results.marks != null && ` · Marks: ${results.marks}`} · Status: {results.status}
                {results.opensAtUtc && ` · Opens: ${fmt(results.opensAtUtc)}`}
                {results.closesAtUtc && ` · Closes: ${fmt(results.closesAtUtc)}`}
                {results.durationMinutes != null && ` · Duration: ${results.durationMinutes} min`}
              </p>

              <Card className="mb-6 overflow-hidden">
                <CardHeader title={`Students Who Took the Exam (${results.takers.length})`} />
                {results.takers.length === 0 ? (
                  <EmptyState icon="users" message="No submissions yet." />
                ) : (
                  <div className="dash-table-wrap">
                    <table className="dash-table">
                      <thead>
                        <tr>
                          <th>Student</th>
                          <th>Score</th>
                          <th>Percent</th>
                          <th>To Grade</th>
                          <th>Submitted</th>
                        </tr>
                      </thead>
                      <tbody>
                        {results.takers.map((t) => (
                          <tr key={t.studentUserId}>
                            <td className="font-medium text-text">{t.fullName}</td>
                            <td className="dash-num font-semibold text-text">
                              {t.score} / {t.maxScore}
                            </td>
                            <td className="dash-num">
                              {t.percent == null ? "—" : `${Math.round(t.percent * 10) / 10}%`}
                            </td>
                            <td>
                              {t.pendingGrading > 0 ? (
                                <StatusBadge status={`${t.pendingGrading} to grade`} />
                              ) : (
                                <StatusBadge status="Passed" />
                              )}
                            </td>
                            <td className="dash-num text-text-secondary">{fmt(t.submittedAtUtc)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </Card>

              <Card className="mb-6 overflow-hidden">
                <CardHeader
                  title={`Written Answers Grading (${writtenAnswers.length})`}
                  action={
                    pendingTotal > 0 ? (
                      <span className="rounded-md bg-warning-light px-2.5 py-1 text-xs font-semibold text-warning">
                        {pendingTotal} pending
                      </span>
                    ) : (
                      <span className="rounded-md bg-success-light px-2.5 py-1 text-xs font-semibold text-success">
                        All graded
                      </span>
                    )
                  }
                />
                {writtenAnswers.length === 0 ? (
                  <EmptyState icon="clipboard-check" message="No written (Essay / Definition) answers submitted for this exam yet." />
                ) : (
                  <div className="divide-y divide-border">
                    {writtenAnswers.map((item) => (
                      <div key={item.answerId} className="flex flex-col gap-3 px-5 py-4">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-semibold text-text">{item.studentName}</span>
                            <span className="rounded bg-primary-light px-2 py-0.5 text-[11px] font-semibold text-primary">
                              Q{item.orderIndex + 1} · {item.questionType}
                            </span>
                          </div>
                          {item.graded ? (
                            <StatusBadge status={item.isCorrect ? "Passed" : "Needs attention"} />
                          ) : (
                            <StatusBadge status="In progress" />
                          )}
                        </div>
                        <div className="text-sm font-medium text-text">{item.questionText}</div>
                        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                          <div className="rounded-control border border-border bg-bg/70 p-3 text-sm">
                            <div className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-text-secondary">
                              Student Response
                            </div>
                            <div className="whitespace-pre-wrap text-text">{item.textAnswer}</div>
                          </div>
                          {item.modelAnswer && (
                            <div className="rounded-control border border-primary/20 bg-primary-light/40 p-3 text-sm">
                              <div className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-primary">
                                Model Answer
                              </div>
                              <div className="whitespace-pre-wrap text-text">{item.modelAnswer}</div>
                            </div>
                          )}
                        </div>
                        <div className="flex flex-wrap items-center gap-2.5">
                          <Button
                            size="sm"
                            variant={item.graded && item.isCorrect ? "primary" : "secondary"}
                            disabled={gradingId === item.answerId}
                            onClick={() => void gradeWritten(item.answerId, true)}
                          >
                            <UiIcon name="check" />
                            Mark Correct
                          </Button>
                          <Button
                            size="sm"
                            variant={item.graded && !item.isCorrect ? "danger" : "secondary"}
                            disabled={gradingId === item.answerId}
                            onClick={() => void gradeWritten(item.answerId, false)}
                          >
                            <UiIcon name="x" />
                            Mark Incorrect
                          </Button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Card>

              <Card className="overflow-hidden">
                <CardHeader title={`Did Not Take the Exam (${results.nonTakers.length})`} />
                {results.nonTakers.length === 0 ? (
                  <EmptyState icon="check" message="Every eligible student submitted this exam." />
                ) : (
                  <div className="dash-table-wrap">
                    <table className="dash-table">
                      <thead>
                        <tr>
                          <th>Student</th>
                          <th>Email</th>
                        </tr>
                      </thead>
                      <tbody>
                        {results.nonTakers.map((n) => (
                          <tr key={n.studentUserId}>
                            <td className="font-medium text-text">{n.fullName}</td>
                            <td className="text-text-secondary">{n.email ?? "—"}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </Card>
            </>
          )}
        </>
      )}
    </div>
  );
}
