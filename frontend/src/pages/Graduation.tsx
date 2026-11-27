import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, errorMessage } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface EligibilityCheck {
  id: string;
  label: string;
  met: boolean;
  detail: string;
}

/* GET /api/graduation/overview */
interface Overview {
  role: string;
  department: string;
  universityName: string;
  gpa: number;
  coursesCompleted: number;
  coursesRequired: number;
  completionPercent: number;
  isEligible: boolean;
  checklist: { key: string; title: string; detail: string; isMet: boolean }[];
  application: {
    id: number;
    status: "Pending" | "InReview" | "Approved" | "Rejected" | "Withdrawn";
    submittedAtUtc: string;
    reviewedAtUtc: string | null;
    decisionNote: string | null;
    conferredGraduationYear: number | null;
  } | null;
}

/* GET /api/graduation/certificate (404 until conferred) */
interface Certificate {
  certificateId: string;
  holder: string;
  degree: string;
  university: string;
  graduationYear: number;
  finalGpa: number;
  honors: string;
  conferredOnUtc: string | null;
}

const fmtDate = (iso: string) => new Date(iso).toLocaleDateString(undefined, { dateStyle: "long" });

export function Graduation() {
  const navigate = useNavigate();
  const [data, setData] = useState<Overview | null>(null);
  const [certificate, setCertificate] = useState<Certificate | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);
  const [notes, setNotes] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    document.title = "Graduation & Honors — UniSphere 360+";
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/graduation/overview`, { headers: authHeaders() });
      if (res.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const overview = (await res.json()) as Overview & { isGraduated: boolean };
      setData(overview);
      // The certificate endpoint is a 404 until the degree is conferred.
      if (overview.isGraduated || overview.application?.status === "Approved") {
        const cert = await fetch(`${API_BASE_URL}/api/graduation/certificate`, { headers: authHeaders() });
        setCertificate(cert.ok ? ((await cert.json()) as Certificate) : null);
      }
    } catch {
      setError("Could not load your graduation eligibility record.");
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  async function submitApplication(e: FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/graduation/apply`, {
        method: "POST",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify({ note: notes.trim() }),
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Could not submit graduation application."), isError: true });
        return;
      }
      setNotes("");
      setToast({
        text: "Graduation application submitted! Administrators have been notified for review.",
        isError: false,
      });
      await load();
    } catch {
      setToast({ text: "Could not submit graduation application.", isError: true });
    } finally {
      setSubmitting(false);
    }
  }

  async function withdrawApplication() {
    setSubmitting(true);
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/graduation/application/withdraw`, {
        method: "POST",
        headers: authHeaders(),
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Could not withdraw application."), isError: true });
        return;
      }
      setToast({ text: "Your pending graduation application has been withdrawn.", isError: false });
      await load();
    } catch {
      setToast({ text: "Could not withdraw application.", isError: true });
    } finally {
      setSubmitting(false);
    }
  }

  const app = data?.application ?? null;
  const canApply = Boolean(data?.isEligible && (!app || app.status === "Rejected" || app.status === "Withdrawn"));

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Graduation &amp; Honors</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Verify graduation eligibility, preview your academic honors tier, submit your graduation application, and print your Honors Certificate once approved.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" to="/courses">
            <UiIcon name="book-open" />
            Courses &amp; Grades
          </Button>
          <Button size="sm" variant="secondary" to="/academic-advisor">
            <UiIcon name="graduation-cap" />
            Academic Advisor
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading || !data ? (
        <LoadingBlock label="Checking your graduation eligibility…" />
      ) : (
        <>
          {/* KPI Strip */}
          <div className="mb-6 grid grid-cols-2 gap-4 lg:grid-cols-4">
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="award" /></div>
              <span className="dash-stat-label">Cumulative GPA</span>
              <span className="dash-stat-value">{data.gpa.toFixed(2)}</span>
            </div>
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="book-open" /></div>
              <span className="dash-stat-label">Completed Courses</span>
              <span className="dash-stat-value">
                {data.coursesCompleted}/{data.coursesRequired}
              </span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="graduation-cap" /></div>
              <span className="dash-stat-label">Degree Completion</span>
              <span className="dash-stat-value text-base">{data.completionPercent}%</span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="shield-check" /></div>
              <span className="dash-stat-label">Application Status</span>
              <span className="dash-stat-value text-base">{app ? app.status : data.isEligible ? "Ready" : "In Progress"}</span>
            </div>
          </div>

          {/* Official Honors Certificate when Approved */}
          {certificate && (
            <Card className="mb-6 overflow-hidden border-2 border-primary/40 bg-gradient-to-br from-card via-card to-primary-light/30">
              <div className="p-8 text-center">
                <div className="mx-auto mb-3 inline-flex h-12 w-12 items-center justify-center rounded-full bg-primary-light text-primary">
                  <UiIcon name="award" />
                </div>
                <div className="text-xs font-bold tracking-[0.2em] text-primary uppercase">
                  {certificate.university}
                </div>
                <h2 className="mt-2 text-2xl font-bold text-text">Official Certificate of Graduation &amp; Honors</h2>
                <p className="mx-auto mt-3 max-w-xl text-sm text-text-secondary">
                  This certifies that <strong className="text-text">{certificate.holder}</strong> has fulfilled all
                  academic requirements in <strong className="text-text">{certificate.degree}</strong> and is
                  awarded the standing of:
                </p>
                <div className="my-4 inline-block rounded-control border border-primary/30 bg-primary-light px-6 py-2 text-lg font-bold text-primary">
                  {certificate.honors}
                </div>
                <div className="mx-auto mt-2 flex max-w-md flex-wrap justify-center gap-6 text-xs text-text-secondary">
                  <span>Final GPA: <strong className="dash-num text-text">{certificate.finalGpa.toFixed(2)}</strong></span>
                  <span>Class of <strong className="dash-num text-text">{certificate.graduationYear}</strong></span>
                  {certificate.conferredOnUtc && <span>Approved: <strong className="text-text">{fmtDate(certificate.conferredOnUtc)}</strong></span>}
                </div>
                <p className="mx-auto mt-3 text-xs text-text-muted">Certificate {certificate.certificateId}</p>
                <div className="mt-5 flex justify-center">
                  <Button size="sm" onClick={() => window.print()}>
                    <UiIcon name="file-text" />
                    Print Certificate
                  </Button>
                </div>
              </div>
            </Card>
          )}

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            {/* Eligibility Checklist */}
            <Card className="lg:col-span-6">
              <CardHeader
                title="Graduation Eligibility Checklist"
                action={<StatusBadge status={data.isEligible ? "Passed" : "Needs Attention"} />}
              />
              <div className="divide-y divide-border">
                {data.checklist.map((chk) => (
                  <div key={chk.key} className="flex items-start justify-between gap-3 px-5 py-4 text-sm">
                    <div>
                      <div className="font-semibold text-text">{chk.title}</div>
                      <div className="mt-0.5 text-xs text-text-secondary">{chk.detail}</div>
                    </div>
                    <span
                      className={`shrink-0 rounded-md px-2.5 py-1 text-xs font-semibold ${
                        chk.isMet ? "bg-success-light text-success" : "bg-warning-light text-warning"
                      }`}
                    >
                      {chk.isMet ? "Met" : "Pending"}
                    </span>
                  </div>
                ))}
              </div>
            </Card>

            {/* Application Form / Current Application Card */}
            <Card className="lg:col-span-6">
              <CardHeader title="Graduation Application" />
              <div className="p-5 text-sm">
                {app && (
                  <div className="mb-5 rounded-control border border-border bg-bg p-4">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-semibold text-text">Application #{app.id}</span>
                      <StatusBadge
                        status={
                          app.status === "Approved"
                            ? "Passed"
                            : app.status === "Pending" || app.status === "InReview"
                              ? "In progress"
                              : "Needs Attention"
                        }
                      />
                    </div>
                    <div className="mt-2 grid grid-cols-2 gap-2 text-xs text-text-secondary">
                      <div>Submitted: <strong className="text-text">{fmtDate(app.submittedAtUtc)}</strong></div>
                      {app.reviewedAtUtc && <div>Reviewed: <strong className="text-text">{fmtDate(app.reviewedAtUtc)}</strong></div>}
                    </div>
                    {app.decisionNote && (
                      <div className="mt-2.5 rounded-md border border-border bg-card p-2.5 text-xs text-text">
                        <strong>Admin Review:</strong> {app.decisionNote}
                      </div>
                    )}
                    {app.status === "Pending" && (
                      <div className="mt-3">
                        <Button
                          size="sm"
                          variant="secondary"
                          disabled={submitting}
                          onClick={() => void withdrawApplication()}
                        >
                          Withdraw Pending Application
                        </Button>
                      </div>
                    )}
                  </div>
                )}

                {canApply ? (
                  <form onSubmit={(e) => void submitApplication(e)} className="flex flex-col gap-3.5">
                    <p className="m-0 text-xs leading-relaxed text-text-secondary">
                      You meet all academic criteria for graduation.
                      Submit your formal application below for Registrar / Admin review.
                    </p>
                    <div>
                      <label className="mb-1 block text-xs font-semibold text-text-secondary">
                        Graduation Ceremony / Thesis Note (optional)
                      </label>
                      <textarea
                        rows={3}
                        value={notes}
                        onChange={(e) => setNotes(e.target.value)}
                        placeholder="e.g., Senior capstone project title, preferred name spelling on certificate…"
                        className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                      />
                    </div>
                    <div>
                      <button
                        type="submit"
                        disabled={submitting}
                        className="btn-luminous inline-flex cursor-pointer items-center gap-2 rounded-control border-0 px-5 py-2.5 text-sm font-semibold disabled:opacity-60"
                      >
                        <UiIcon name="graduation-cap" />
                        {submitting ? "Submitting…" : "Apply for Graduation"}
                      </button>
                    </div>
                  </form>
                ) : !app ? (
                  <div className="rounded-control border border-warning/30 bg-warning-light/40 p-4 text-xs text-text">
                    Complete all remaining checklist items to unlock graduation application submission.
                  </div>
                ) : null}
              </div>
            </Card>
          </div>
        </>
      )}
    </div>
  );
}
