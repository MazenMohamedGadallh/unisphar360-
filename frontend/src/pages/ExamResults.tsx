import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Card, CardHeader, EmptyState, ErrorState, LoadingBlock } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken } from "../lib/api";
import "./dashboard.css";

/*
 * Doctor Exam Results: React port of Pages/Doctor/ExamResults.razor on top of
 * GET /api/doctor/exam-assistant/exams (published ones only) and
 * GET /api/doctor/exam-assistant/exams/{id}/results (404 = not yours / no data).
 */

interface ExamItem { id: number; courseCode: string; title: string; status: string }
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
  takers: { studentUserId: number; fullName: string; submittedAtUtc: string; score: number; maxScore: number; percent: number | null }[];
  nonTakers: { studentUserId: number; fullName: string; email: string | null }[];
}

const BASE = `${API_BASE_URL}/api/doctor/exam-assistant/exams`;
const fmt = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

export function ExamResults() {
  const navigate = useNavigate();
  const [exams, setExams] = useState<ExamItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState(0);
  // undefined = loading, null = no results for the selected exam
  const [results, setResults] = useState<Results | null | undefined>(null);

  const get = useCallback(
    async (url: string) => {
      const res = await fetch(url, { headers: authHeaders() });
      if (res.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        throw new DOMException("expired", "AbortError");
      }
      return res;
    },
    [navigate],
  );

  const load = useCallback(async () => {
    setError(null);
    try {
      const res = await get(BASE);
      if (res.status === 403) return setError("This page is only available to doctors.");
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      // Draft exams have no attempts, so only published ones are listed.
      setExams(((await res.json()) as ExamItem[]).filter((e) => e.status === "Published"));
    } catch (e) {
      if ((e as Error).name !== "AbortError") setError("Could not load your exams. Check your connection and try again.");
    }
  }, [get]);

  useEffect(() => { void load(); }, [load]);

  async function select(id: number) {
    setSelected(id);
    setResults(null);
    if (id <= 0) return;
    setResults(undefined);
    try {
      const res = await get(`${BASE}/${id}/results`);
      if (res.status === 404) return setResults(null);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setResults((await res.json()) as Results);
    } catch (e) {
      if ((e as Error).name === "AbortError") return;
      setResults(null);
      setError("Could not load results for this exam. Please try again.");
    }
  }

  return (
    <div className="dashboard">
      <div className="mb-6">
        <h1 className="m-0 text-2xl font-semibold">Exam Results</h1>
        <p className="mt-1 text-sm text-text-secondary">
          Who took each of your published exams — server-graded scores, percentages and submission dates — plus enrolled students who have not taken it yet.
        </p>
      </div>

      {error ? (
        <Card><ErrorState message={error} onRetry={() => void (selected > 0 ? select(selected) : load())} /></Card>
      ) : exams === null ? (
        <LoadingBlock label="Loading your exams…" />
      ) : exams.length === 0 ? (
        <Card><EmptyState icon="clipboard-check" message="No published exams yet. Publish an exam from the Exam Assistant or AI Assistant first." /></Card>
      ) : (
        <>
          <Card className="mb-6 overflow-hidden">
            <div className="flex flex-col gap-2 px-5 py-4 text-sm">
              <label htmlFor="exam-select" className="font-medium">Exam</label>
              <select
                id="exam-select"
                className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text"
                value={selected}
                onChange={(e) => void select(Number(e.target.value))}
              >
                <option value={0}>Select an exam…</option>
                {exams.map((e) => <option key={e.id} value={e.id}>{e.title} ({e.courseCode})</option>)}
              </select>
            </div>
          </Card>

          {results === undefined ? (
            <LoadingBlock label="Loading results…" />
          ) : results === null ? (
            selected > 0 && <Card><EmptyState icon="clipboard-check" message="No results available for this exam." /></Card>
          ) : (
            <>
              <div className="dash-stats">
                <div className="dash-stat courses">
                  <span className="dash-stat-label">Eligible students</span>
                  <span className="dash-stat-value">{results.eligibleCount}</span>
                </div>
                <div className="dash-stat gpa">
                  <span className="dash-stat-label">Submitted</span>
                  <span className="dash-stat-value">{results.takers.length}</span>
                </div>
                <div className="dash-stat attendance">
                  <span className="dash-stat-label">Not taken</span>
                  <span className="dash-stat-value">{results.nonTakers.length}</span>
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
                <CardHeader title={`Students who took the exam (${results.takers.length})`} />
                {results.takers.length === 0 ? (
                  <EmptyState icon="users" message="No submissions yet." />
                ) : (
                  <div className="dash-table-wrap">
                    <table className="dash-table">
                      <thead><tr><th>Student</th><th>Score</th><th>Percent</th><th>Submitted</th></tr></thead>
                      <tbody>
                        {results.takers.map((t) => (
                          <tr key={t.studentUserId}>
                            <td>{t.fullName}</td>
                            <td className="dash-num">{t.score} / {t.maxScore}</td>
                            <td className="dash-num">{t.percent == null ? "—" : `${Math.round(t.percent * 10) / 10}%`}</td>
                            <td className="dash-num">{fmt(t.submittedAtUtc)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </Card>

              <Card className="overflow-hidden">
                <CardHeader title={`Did not take the exam (${results.nonTakers.length})`} />
                {results.nonTakers.length === 0 ? (
                  <EmptyState icon="check" message="Every eligible student submitted this exam." />
                ) : (
                  <div className="dash-table-wrap">
                    <table className="dash-table">
                      <thead><tr><th>Student</th><th>Email</th></tr></thead>
                      <tbody>
                        {results.nonTakers.map((n) => (
                          <tr key={n.studentUserId}><td>{n.fullName}</td><td>{n.email ?? "—"}</td></tr>
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
