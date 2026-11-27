import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, ErrorState, LoadingBlock } from "../components/ui";
import { clearToken } from "../lib/api";
import { gradFetch } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

/* GET /api/grad/alumni */
interface AlumniResponse {
  universityName: string | null;
  department: string;
  graduationYear: string | null;
  degree: string | null;
  careerEvents: { id: number; title: string; location: string; startsAtUtc: string; description: string | null }[];
  announcements: { id: number; title: string; body: string; category: string | null; createdAtUtc: string }[];
  careerCenterTip: string;
  contacts: string[];
}

const fmtDate = (iso: string) => new Date(iso).toLocaleDateString(undefined, { dateStyle: "medium" });

export function GraduateAlumni() {
  const navigate = useNavigate();
  const [data, setData] = useState<AlumniResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  useEffect(() => {
    document.title = "Campus — UniSphere 360+";
  }, []);

  const load = useCallback(
    async (isManualRefresh = false) => {
      if (isManualRefresh) setRefreshing(true);
      else setLoading(true);
      setError(null);
      try {
        setData(await gradFetch<AlumniResponse>("grad/alumni"));
        if (isManualRefresh) {
          setToast({ text: "Campus events, announcements, and university info refreshed.", isError: false });
        }
      } catch (e) {
        if ((e as Error).name === "Unauthorized") {
          clearToken();
          navigate("/login?expired=1");
          return;
        }
        setError("Could not load Campus & Alumni Portal.");
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [navigate],
  );

  useEffect(() => {
    void load(false);
  }, [load]);

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Campus</h1>
          <p className="mt-1 text-sm text-text-secondary">
            {data
              ? `${data.universityName} Alumni & Career Center — live university info, career events, and announcements.`
              : "University info, career center tips, career events, and announcements."}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" disabled={refreshing} onClick={() => void load(true)}>
            <UiIcon name="refresh-cw" />
            {refreshing ? "Refreshing…" : "Refresh"}
          </Button>
          <Button size="sm" variant="secondary" to="/graduate/jobs">
            <UiIcon name="briefcase" />
            Jobs
          </Button>
          <Button size="sm" to="/graduate/interview">
            <UiIcon name="mic" />
            AI Interview Coach
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load(false)} />
        </Card>
      ) : loading || !data ? (
        <LoadingBlock label="Loading Campus & Alumni Portal…" />
      ) : (
        <>
          {/* Live University Info + Career Center Tip */}
          <div className="mb-6 grid grid-cols-1 gap-4 lg:grid-cols-12">
            <Card className="p-5 lg:col-span-5">
              <div className="flex items-start gap-3.5">
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-primary-light text-primary">
                  <UiIcon name="landmark" />
                </span>
                <div className="min-w-0 flex-1 text-xs">
                  <div className="text-base font-semibold text-text">{data.universityName}</div>
                  <div className="mt-1 text-text-secondary">
                    {[data.degree, data.department, data.graduationYear].filter(Boolean).join(" · ")}
                  </div>
                  <div className="mt-2 flex flex-col gap-1 text-text-secondary">
                    {data.contacts.map((contact) => (
                      <span key={contact}>{contact}</span>
                    ))}
                  </div>
                </div>
              </div>
            </Card>

            <Card className="flex items-center p-5 lg:col-span-7">
              <div className="flex items-start gap-3.5">
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-primary-light text-primary">
                  <UiIcon name="lightbulb" />
                </span>
                <div>
                  <div className="text-xs font-semibold uppercase tracking-wide text-primary">Career Center Tip</div>
                  <p className="m-0 mt-1 text-sm leading-relaxed text-text">{data.careerCenterTip}</p>
                </div>
              </div>
            </Card>
          </div>

          <div className="mb-6 grid grid-cols-2 gap-4">
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="megaphone" /></div>
              <span className="dash-stat-label">Announcements</span>
              <span className="dash-stat-value">{data.announcements.length}</span>
            </div>
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="calendar-days" /></div>
              <span className="dash-stat-label">Career Events</span>
              <span className="dash-stat-value">{data.careerEvents.length}</span>
            </div>
          </div>

          {data.announcements.length > 0 && (
            <Card className="mb-6 overflow-hidden">
              <CardHeader title={`University Announcements (${data.announcements.length})`} />
              <div className="divide-y divide-border">
                {data.announcements.map((a) => (
                  <div key={a.id} className="px-5 py-4 text-sm">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-semibold text-text">{a.title}</span>
                        <span className="rounded bg-primary-light px-2 py-0.5 text-[11px] font-semibold text-primary">
                          {a.category ?? "General"}
                        </span>
                      </div>
                      <span className="text-xs text-text-muted">{fmtDate(a.createdAtUtc)}</span>
                    </div>
                    <p className="m-0 mt-1.5 text-xs leading-relaxed text-text-secondary">{a.body}</p>
                  </div>
                ))}
              </div>
            </Card>
          )}

          <div className="mb-6">
            {/* Campus Career Events */}
            <Card className="overflow-hidden">
              <CardHeader title="Career Events" />
              <div className="divide-y divide-border">
                {data.careerEvents.map((ev) => (
                  <div key={ev.id} className="px-5 py-4">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs font-medium text-text-secondary">{fmtDate(ev.startsAtUtc)}</span>
                    </div>
                    <div className="mt-1.5 text-sm font-semibold text-text">{ev.title}</div>
                    <div className="mt-0.5 text-xs text-text-muted">{ev.location}</div>
                    <p className="m-0 mt-1.5 text-xs leading-relaxed text-text-secondary">{ev.description}</p>
                  </div>
                ))}
              </div>
            </Card>

          </div>
        </>
      )}
    </div>
  );
}
