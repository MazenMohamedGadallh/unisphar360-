import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, ConfirmDialog, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { clearToken } from "../lib/api";
import { gradFetch } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

type AppStatus = "Applied" | "Interview" | "Offer" | "Rejected" | "Withdrawn";

/* GET /api/grad/applications */
interface ApplicationItem {
  id: number;
  jobListingId: number | null;
  jobTitle: string;
  companyName: string;
  sourceUrl: string | null;
  followUpDate: string | null;
  status: AppStatus;
  notes: string | null;
  appliedAtUtc: string;
  updatedAtUtc: string;
}

const STAGES: AppStatus[] = ["Applied", "Interview", "Offer", "Rejected", "Withdrawn"];

const fmtDate = (iso: string) => new Date(iso).toLocaleDateString(undefined, { dateStyle: "medium" });

function isDueSoon(iso?: string | null): boolean {
  if (!iso) return false;
  const target = new Date(iso);
  if (Number.isNaN(target.getTime())) return false;
  const limit = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000);
  return target.getTime() <= limit.getTime();
}

export function GraduateApplications() {
  const navigate = useNavigate();
  const [apps, setApps] = useState<ApplicationItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);
  const [filter, setFilter] = useState<"all" | AppStatus>("all");
  const [busyId, setBusyId] = useState<number | null>(null);
  const [noteDrafts, setNoteDrafts] = useState<Record<number, string>>({});
  const [followUpDrafts, setFollowUpDrafts] = useState<Record<number, string>>({});
  const [pendingDelete, setPendingDelete] = useState<ApplicationItem | null>(null);

  // "Add an application" custom form
  const [company, setCompany] = useState("");
  const [jobTitle, setJobTitle] = useState("");
  const [sourceUrl, setSourceUrl] = useState("");
  const [newStatus, setNewStatus] = useState<AppStatus>("Applied");
  const [followUpDate, setFollowUpDate] = useState("");
  const [notes, setNotes] = useState("");
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    document.title = "My Applications — UniSphere 360+";
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await gradFetch<ApplicationItem[]>("grad/applications");
      setApps(list);
      const nDrafts: Record<number, string> = {};
      const fDrafts: Record<number, string> = {};
      for (const a of list) {
        nDrafts[a.id] = a.notes ?? "";
        fDrafts[a.id] = a.followUpDate ? a.followUpDate.slice(0, 10) : "";
      }
      setNoteDrafts(nDrafts);
      setFollowUpDrafts(fDrafts);
    } catch (e) {
      if ((e as Error).name === "Unauthorized") {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      setError("Could not load your applications.");
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  async function addCustomApplication(e: FormEvent) {
    e.preventDefault();
    if (!company.trim() || !jobTitle.trim()) {
      setToast({ text: "Company and Job title are required.", isError: true });
      return;
    }
    setAdding(true);
    setToast(null);
    try {
      await gradFetch("grad/applications", {
        method: "POST",
        body: JSON.stringify({
          companyName: company.trim(),
          jobTitle: jobTitle.trim(),
          sourceUrl: sourceUrl.trim() || null,
          status: newStatus,
          followUpDate: followUpDate || null,
          notes: notes.trim() || null,
        }),
      });
      setToast({ text: `Added "${jobTitle.trim()}" at ${company.trim()} to My Applications.`, isError: false });
      setCompany("");
      setJobTitle("");
      setSourceUrl("");
      setNewStatus("Applied");
      setFollowUpDate("");
      setNotes("");
      await load();
    } catch (e) {
      setToast({ text: e instanceof Error ? e.message : "Could not add application.", isError: true });
    } finally {
      setAdding(false);
    }
  }

  async function updateStatus(appItem: ApplicationItem, status: AppStatus) {
    setBusyId(appItem.id);
    setToast(null);
    try {
      await gradFetch(`grad/applications/${appItem.id}`, { method: "PUT", body: JSON.stringify({ status }) });
      setApps((prev) => prev.map((a) => (a.id === appItem.id ? { ...a, status } : a)));
      setToast({ text: `Updated ${appItem.jobTitle} (${appItem.companyName}) to ${status}.`, isError: false });
    } catch {
      setToast({ text: "Could not update application status.", isError: true });
    } finally {
      setBusyId(null);
    }
  }

  async function saveDetails(appItem: ApplicationItem) {
    setBusyId(appItem.id);
    setToast(null);
    try {
      const nextNotes = noteDrafts[appItem.id] ?? "";
      const nextFollowUp = followUpDrafts[appItem.id] || null;
      await gradFetch(`grad/applications/${appItem.id}`, {
        method: "PUT",
        body: JSON.stringify({ notes: nextNotes, followUpDate: nextFollowUp }),
      });
      // The API keeps the old date when none is sent, so only a new date is reflected locally.
      setApps((prev) =>
        prev.map((a) =>
          a.id === appItem.id ? { ...a, notes: nextNotes, followUpDate: nextFollowUp ?? a.followUpDate } : a,
        ),
      );
      setToast({ text: `Saved follow-up & notes for ${appItem.companyName}.`, isError: false });
    } catch {
      setToast({ text: "Failed to save application details.", isError: true });
    } finally {
      setBusyId(null);
    }
  }

  async function deleteApplication(appItem: ApplicationItem) {
    setBusyId(appItem.id);
    setToast(null);
    try {
      await gradFetch(`grad/applications/${appItem.id}`, { method: "DELETE" });
      setApps((prev) => prev.filter((a) => a.id !== appItem.id));
      setToast({ text: `Deleted application for ${appItem.jobTitle} at ${appItem.companyName}.`, isError: false });
    } catch {
      setToast({ text: "Could not delete application.", isError: true });
    } finally {
      setBusyId(null);
    }
  }

  const countBy = (stage: AppStatus) => apps.filter((a) => a.status === stage).length;
  const followUpsDueCount = apps.filter((a) => isDueSoon(a.followUpDate)).length;
  const visible = filter === "all" ? apps : apps.filter((a) => a.status === filter);

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">My Applications</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Track job board and external applications, schedule follow-up dates, update stages, and manage notes.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" to="/graduate/interview">
            <UiIcon name="mic" />
            Prep for Interviews
          </Button>
          <Button size="sm" to="/graduate/jobs">
            <UiIcon name="briefcase" />
            Browse Jobs
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading ? (
        <LoadingBlock label="Loading your applications…" />
      ) : (
        <>
          {/* Live KPI Cards: Total, Applied, Interview, Follow-ups due */}
          <div className="mb-6 grid grid-cols-2 gap-4 lg:grid-cols-4">
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="clipboard-check" /></div>
              <span className="dash-stat-label">Total</span>
              <span className="dash-stat-value">{apps.length}</span>
            </div>
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="send" /></div>
              <span className="dash-stat-label">Applied</span>
              <span className="dash-stat-value">{countBy("Applied")}</span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="mic" /></div>
              <span className="dash-stat-label">Interview</span>
              <span className="dash-stat-value">{countBy("Interview")}</span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="calendar-clock" /></div>
              <span className="dash-stat-label">Follow-ups due</span>
              <span className="dash-stat-value">{followUpsDueCount}</span>
            </div>
          </div>

          {/* Add an application Form */}
          <Card className="mb-6">
            <CardHeader title="Add an application" />
            <form onSubmit={(e) => void addCustomApplication(e)} className="grid grid-cols-1 gap-3.5 p-5 text-sm md:grid-cols-12">
              <div className="md:col-span-4">
                <label className="mb-1 block text-xs font-semibold text-text-secondary">Company *</label>
                <input
                  type="text"
                  required
                  value={company}
                  onChange={(e) => setCompany(e.target.value)}
                  placeholder="e.g., Fawry, Valeo, Instabug"
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                />
              </div>
              <div className="md:col-span-4">
                <label className="mb-1 block text-xs font-semibold text-text-secondary">Job title *</label>
                <input
                  type="text"
                  required
                  value={jobTitle}
                  onChange={(e) => setJobTitle(e.target.value)}
                  placeholder="e.g., Backend Software Engineer"
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                />
              </div>
              <div className="md:col-span-4">
                <label className="mb-1 block text-xs font-semibold text-text-secondary">Source URL (optional)</label>
                <input
                  type="url"
                  value={sourceUrl}
                  onChange={(e) => setSourceUrl(e.target.value)}
                  placeholder="https://careers.company.com/role"
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                />
              </div>
              <div className="md:col-span-3">
                <label className="mb-1 block text-xs font-semibold text-text-secondary">Status</label>
                <select
                  value={newStatus}
                  onChange={(e) => setNewStatus(e.target.value as AppStatus)}
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                >
                  {STAGES.map((st) => (
                    <option key={st} value={st}>
                      {st}
                    </option>
                  ))}
                </select>
              </div>
              <div className="md:col-span-3">
                <label className="mb-1 block text-xs font-semibold text-text-secondary">Follow-up date</label>
                <input
                  type="date"
                  value={followUpDate}
                  onChange={(e) => setFollowUpDate(e.target.value)}
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                />
              </div>
              <div className="md:col-span-6">
                <label className="mb-1 block text-xs font-semibold text-text-secondary">Notes</label>
                <input
                  type="text"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  placeholder="Recruiter name, referral, interview prep reminders…"
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                />
              </div>
              <div className="md:col-span-12">
                <button
                  type="submit"
                  disabled={adding}
                  className="btn-luminous inline-flex cursor-pointer items-center gap-2 rounded-control border-0 px-5 py-2 text-sm font-semibold disabled:opacity-60"
                >
                  <UiIcon name="plus" />
                  {adding ? "Adding…" : "Add Application"}
                </button>
              </div>
            </form>
          </Card>

          <Card className="overflow-hidden">
            <CardHeader
              title={`Tracked Applications (${visible.length})`}
              action={
                <div className="flex flex-wrap gap-1.5">
                  {(["all", ...STAGES] as const).map((st) => (
                    <button
                      key={st}
                      type="button"
                      onClick={() => setFilter(st)}
                      className={`cursor-pointer rounded-md border px-2.5 py-1 text-xs font-semibold transition ${
                        filter === st
                          ? "border-primary bg-primary text-white"
                          : "border-border bg-bg text-text-secondary hover:text-text"
                      }`}
                    >
                      {st === "all" ? `All (${apps.length})` : st}
                    </button>
                  ))}
                </div>
              }
            />

            {visible.length === 0 ? (
              <EmptyState
                icon="clipboard-check"
                message={
                  apps.length === 0
                    ? "You haven't added or applied to any jobs yet. Use the form above or browse Jobs."
                    : `No applications in the "${filter}" stage.`
                }
              />
            ) : (
              <div className="divide-y divide-border">
                {visible.map((item) => {
                  const dueSoon = isDueSoon(item.followUpDate);
                  return (
                    <div key={item.id} className="flex flex-col gap-3 px-5 py-4">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="text-base font-semibold text-text">{item.jobTitle}</span>
                              <StatusBadge
                              status={
                                item.status === "Offer" || item.status === "Interview"
                                  ? "Passed"
                                  : item.status === "Applied"
                                    ? "In progress"
                                    : item.status === "Rejected" || item.status === "Withdrawn"
                                      ? "Needs Attention"
                                      : "Upcoming"
                              }
                            />
                            {dueSoon && (
                              <span className="rounded bg-warning-light px-2 py-0.5 text-[11px] font-semibold text-warning">
                                Follow-up due
                              </span>
                            )}
                          </div>
                          <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-text-secondary">
                            <strong className="text-text">{item.companyName}</strong>
                            <span>· Updated {fmtDate(item.updatedAtUtc)}</span>
                            {item.sourceUrl && (
                              <a
                                href={item.sourceUrl}
                                target="_blank"
                                rel="noreferrer"
                                className="font-semibold text-primary hover:underline"
                              >
                                Source link ↗
                              </a>
                            )}
                          </div>
                        </div>

                        <div className="flex flex-wrap items-center gap-2">
                          <select
                            aria-label={`Status for ${item.jobTitle}`}
                            disabled={busyId === item.id}
                            value={item.status}
                            onChange={(e) => void updateStatus(item, e.target.value as AppStatus)}
                            className="rounded-control border border-border bg-bg px-3 py-1.5 text-xs font-semibold text-text outline-none focus:border-primary"
                          >
                            {STAGES.map((st) => (
                              <option key={st} value={st}>
                                {st}
                              </option>
                            ))}
                          </select>
                          <Button
                            size="sm"
                            variant="secondary"
                            disabled={busyId === item.id}
                            onClick={() => setPendingDelete(item)}
                          >
                            <UiIcon name="trash-2" />
                            Delete
                          </Button>
                        </div>
                      </div>

                      <div className="flex flex-wrap items-center gap-2">
                        <label className="flex items-center gap-1.5 text-xs text-text-secondary">
                          <span>Follow-up:</span>
                          <input
                            type="date"
                            value={followUpDrafts[item.id] ?? ""}
                            onChange={(e) =>
                              setFollowUpDrafts((prev) => ({ ...prev, [item.id]: e.target.value }))
                            }
                            className="rounded-control border border-border bg-bg px-2.5 py-1 text-xs text-text outline-none focus:border-primary"
                          />
                        </label>
                        <input
                          type="text"
                          value={noteDrafts[item.id] ?? ""}
                          onChange={(e) => setNoteDrafts((prev) => ({ ...prev, [item.id]: e.target.value }))}
                          placeholder="Add interview dates, recruiter contact, or prep notes…"
                          className="min-w-[200px] flex-1 rounded-control border border-border bg-bg px-3 py-1.5 text-xs text-text outline-none focus:border-primary"
                        />
                        <Button
                          size="sm"
                          variant="secondary"
                          disabled={busyId === item.id}
                          onClick={() => void saveDetails(item)}
                        >
                          Save Update
                        </Button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </Card>
        </>
      )}

      <ConfirmDialog
        open={Boolean(pendingDelete)}
        title="Delete Application?"
        confirmLabel="Delete"
        danger
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => {
          if (pendingDelete) {
            const target = pendingDelete;
            setPendingDelete(null);
            void deleteApplication(target);
          }
        }}
      >
        Are you sure you want to delete your application for{" "}
        <strong>{pendingDelete?.jobTitle}</strong> at <strong>{pendingDelete?.companyName}</strong>?
      </ConfirmDialog>
    </div>
  );
}
