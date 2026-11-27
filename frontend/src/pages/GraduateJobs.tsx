import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { clearToken } from "../lib/api";
import { gradFetch } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface JobMatch {
  id: number;
  title: string;
  company: string;
  location: string;
  workMode: "Remote" | "Hybrid" | "On-site";
  salaryRange: string;
  requiredSkills: string[];
  description: string;
  postedAtUtc: string;
  sourceUrl?: string;
  matchedSkills: string[];
  missingSkills: string[];
  matchPercent: number;
  applicationStatus: "Saved" | "Applied" | "Interview" | "Offer" | "Rejected" | "Withdrawn" | null;
}

const PAGE_SIZE = 5;

export function GraduateJobs() {
  const navigate = useNavigate();
  const [jobs, setJobs] = useState<JobMatch[]>([]);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);
  const [busyJobId, setBusyJobId] = useState<number | null>(null);

  const [mainTab, setMainTab] = useState<"all" | "matches" | "insights">("all");
  const [subFilter, setSubFilter] = useState<"all" | "saved">("all");

  // Draft filter inputs + applied filters (Search & Reset)
  const [keywordInput, setKeywordInput] = useState("");
  const [locationInput, setLocationInput] = useState("");
  const [workModeInput, setWorkModeInput] = useState<string>("all");
  const [sortInput, setSortInput] = useState<"match" | "newest" | "company">("match");

  const [appliedKeyword, setAppliedKeyword] = useState("");
  const [appliedLocation, setAppliedLocation] = useState("");
  const [appliedWorkMode, setAppliedWorkMode] = useState<string>("all");
  const [appliedSort, setAppliedSort] = useState<"match" | "newest" | "company">("match");

  const [page, setPage] = useState(1);
  const [openedJob, setOpenedJob] = useState<JobMatch | null>(null);

  useEffect(() => {
    document.title = "Jobs — UniSphere 360+";
  }, []);

  const fetchJobs = useCallback(async (): Promise<JobMatch[]> => {
    const [listed, matches, applications] = await Promise.all([
      gradFetch<{ jobs: any[] }>("grad/jobs?pageSize=50"),
      gradFetch<any[]>("grad/jobs/matches?limit=50&minScore=0"),
      gradFetch<{ jobListingId: number | null; status: JobMatch["applicationStatus"] }[]>("grad/applications"),
    ]);
    const matchBy = new Map<number, any>(matches.map((m) => [m.job.id, m]));
    const all = new Map<number, any>([...matches.map((m) => m.job), ...listed.jobs].map((j) => [j.id, j]));
    const statusBy = new Map(applications.map((a) => [a.jobListingId, a.status]));
    return [...all.values()].map((j) => ({
      id: j.id,
      title: j.title,
      company: j.companyName,
      location: j.location,
      workMode: j.remote ? "Remote" : "On-site",
      salaryRange: j.salary || "Salary not listed",
      requiredSkills: j.keySkills,
      description: j.snippet ?? "",
      postedAtUtc: j.publishedAtUtc,
      // Feed data: never put a non-http URL into an href.
      sourceUrl: /^https?:\/\//i.test(j.url) ? j.url : undefined,
      matchedSkills: matchBy.get(j.id)?.matchedSkills ?? [],
      missingSkills: matchBy.get(j.id)?.missingSkills ?? [],
      matchPercent: matchBy.get(j.id)?.scorePercent ?? 0,
      applicationStatus: statusBy.get(j.id) ?? (j.isSaved ? "Saved" : null),
    }));
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setJobs(await fetchJobs());
    } catch (e) {
      if ((e as Error).name === "Unauthorized") {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      setError("Could not load job listings.");
    } finally {
      setLoading(false);
    }
  }, [navigate, fetchJobs]);

  useEffect(() => {
    void load();
  }, [load]);

  async function syncJobs() {
    setSyncing(true);
    setToast(null);
    try {
      const result = await gradFetch<{ imported: number; throttled: boolean; error: string | null }>("grad/jobs/sync");
      if (result.error) {
        setToast({ text: result.error, isError: true });
        return;
      }
      setJobs(await fetchJobs());
      setPage(1);
      setToast({
        text: result.imported > 0 ? `Synced ${result.imported} new roles.` : "All live listings are up to date.",
        isError: false,
      });
    } catch {
      setToast({ text: "Could not sync jobs right now.", isError: true });
    } finally {
      setSyncing(false);
    }
  }

  async function applyOrSave(job: JobMatch, status: "Saved" | "Applied") {
    setBusyJobId(job.id);
    setToast(null);
    try {
      const unsave = status === "Saved" && job.applicationStatus === "Saved";
      if (status === "Applied") {
        if (job.applicationStatus && job.applicationStatus !== "Saved") return; // already tracked
        await gradFetch("grad/applications", { method: "POST", body: JSON.stringify({ jobListingId: job.id, status }) });
      } else if (job.applicationStatus && !unsave) {
        return;
      } else {
        await gradFetch(`grad/jobs/${job.id}/save`, { method: unsave ? "DELETE" : "POST" });
      }
      const next = unsave ? null : status;
      setJobs((prev) => prev.map((item) => (item.id === job.id ? { ...item, applicationStatus: next } : item)));
      setOpenedJob((prev) => (prev && prev.id === job.id ? { ...prev, applicationStatus: next } : prev));
      if (unsave) {
        setToast({ text: `Removed "${job.title}" from saved jobs.`, isError: false });
        return;
      }
      setToast({
        text:
          status === "Applied"
            ? `Marked "${job.title}" at ${job.company} as Applied. Track it anytime in My Applications.`
            : `Saved "${job.title}" at ${job.company}.`,
        isError: false,
      });
    } catch {
      setToast({ text: "Could not update job status.", isError: true });
    } finally {
      setBusyJobId(null);
    }
  }

  function handleSearch(e: FormEvent) {
    e.preventDefault();
    setAppliedKeyword(keywordInput.trim());
    setAppliedLocation(locationInput.trim());
    setAppliedWorkMode(workModeInput);
    setAppliedSort(sortInput);
    setPage(1);
  }

  function handleReset() {
    setKeywordInput("");
    setLocationInput("");
    setWorkModeInput("all");
    setSortInput("match");
    setAppliedKeyword("");
    setAppliedLocation("");
    setAppliedWorkMode("all");
    setAppliedSort("match");
    setSubFilter("all");
    setPage(1);
  }

  const filtered = useMemo(() => {
    const kw = appliedKeyword.toLowerCase();
    const loc = appliedLocation.toLowerCase();
    const list = jobs.filter((j) => {
      if (mainTab === "matches" && j.matchPercent < 50) return false;
      if (subFilter === "saved" && j.applicationStatus !== "Saved") return false;
      if (appliedWorkMode !== "all" && j.workMode !== appliedWorkMode) return false;
      if (loc && !j.location.toLowerCase().includes(loc)) return false;
      if (kw) {
        const hit =
          j.title.toLowerCase().includes(kw) ||
          j.company.toLowerCase().includes(kw) ||
          j.description.toLowerCase().includes(kw) ||
          j.requiredSkills.some((s) => s.toLowerCase().includes(kw));
        if (!hit) return false;
      }
      return true;
    });
    if (appliedSort === "company") {
      return [...list].sort((a, b) => a.company.localeCompare(b.company));
    }
    if (appliedSort === "newest") {
      return [...list].sort((a, b) => new Date(b.postedAtUtc).getTime() - new Date(a.postedAtUtc).getTime());
    }
    return [...list].sort((a, b) => b.matchPercent - a.matchPercent);
  }, [jobs, mainTab, subFilter, appliedKeyword, appliedLocation, appliedWorkMode, appliedSort]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const safePage = Math.min(page, totalPages);
  const paginated = filtered.slice((safePage - 1) * PAGE_SIZE, safePage * PAGE_SIZE);

  // Market Insights computed live from database jobs
  const marketInsights = useMemo(() => {
    const skillCounts = new Map<string, number>();
    const modeCounts: Record<string, number> = { Remote: 0, Hybrid: 0, "On-site": 0 };
    const locationCounts = new Map<string, number>();
    for (const j of jobs) {
      modeCounts[j.workMode] = (modeCounts[j.workMode] ?? 0) + 1;
      locationCounts.set(j.location, (locationCounts.get(j.location) ?? 0) + 1);
      for (const sk of j.requiredSkills) {
        skillCounts.set(sk, (skillCounts.get(sk) ?? 0) + 1);
      }
    }
    const topSkills = Array.from(skillCounts.entries())
      .map(([skill, count]) => ({ skill, count, pct: Math.round((count / Math.max(1, jobs.length)) * 100) }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 8);
    const topLocations = Array.from(locationCounts.entries())
      .map(([location, count]) => ({ location, count }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 5);
    const avgMatch = jobs.length ? Math.round(jobs.reduce((s, j) => s + j.matchPercent, 0) / jobs.length) : 0;
    return { topSkills, modeCounts, topLocations, avgMatch };
  }, [jobs]);

  const savedCount = jobs.filter((j) => j.applicationStatus === "Saved").length;
  const appliedCount = jobs.filter((j) => j.applicationStatus && j.applicationStatus !== "Saved").length;
  const bestMatch = Math.max(0, ...jobs.map((j) => j.matchPercent));

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Jobs</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Browse synced graduate openings, inspect AI skill-match scores, explore market insights, and save or apply.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            disabled={syncing}
            onClick={() => void syncJobs()}
            className="btn-luminous inline-flex cursor-pointer items-center gap-2 rounded-control border-0 px-3.5 py-2 text-xs font-semibold disabled:opacity-60"
          >
            <UiIcon name="refresh-cw" />
            {syncing ? "Syncing…" : "Sync jobs"}
          </button>
          <Button size="sm" variant="secondary" to="/graduate/applications">
            <UiIcon name="clipboard-check" />
            My Applications ({appliedCount})
          </Button>
          <Button size="sm" variant="secondary" to="/graduate/cv">
            <UiIcon name="file-text" />
            Tune CV
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading ? (
        <LoadingBlock label="Matching jobs with your skills…" />
      ) : (
        <>
          <div className="mb-6 grid grid-cols-2 gap-4 lg:grid-cols-4">
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="briefcase" /></div>
              <span className="dash-stat-label">Open Roles</span>
              <span className="dash-stat-value">{jobs.length}</span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="trending-up" /></div>
              <span className="dash-stat-label">Highest AI Match</span>
              <span className="dash-stat-value">{bestMatch}%</span>
            </div>
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="bookmark" /></div>
              <span className="dash-stat-label">Saved Jobs</span>
              <span className="dash-stat-value">{savedCount}</span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="check-circle" /></div>
              <span className="dash-stat-label">Applied</span>
              <span className="dash-stat-value">{appliedCount}</span>
            </div>
          </div>

          {/* Primary Tabs: All jobs | AI matches | Market insights */}
          <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
            <div className="inline-flex rounded-control border border-border bg-card p-1">
              {(
                [
                  { id: "all", label: `All jobs (${jobs.length})` },
                  { id: "matches", label: `AI matches (${jobs.filter((j) => j.matchPercent >= 50).length})` },
                  { id: "insights", label: "Market insights" },
                ] as const
              ).map((t) => (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => {
                    setMainTab(t.id);
                    setPage(1);
                  }}
                  className={`cursor-pointer rounded-md border-0 px-3.5 py-1.5 text-xs font-semibold transition ${
                    mainTab === t.id
                      ? "bg-primary text-white"
                      : "bg-transparent text-text-secondary hover:text-text"
                  }`}
                >
                  {t.label}
                </button>
              ))}
            </div>

            {mainTab !== "insights" && (
              <div className="inline-flex rounded-control border border-border bg-card p-1">
                <button
                  type="button"
                  onClick={() => {
                    setSubFilter("all");
                    setPage(1);
                  }}
                  className={`cursor-pointer rounded-md border-0 px-3 py-1 text-xs font-semibold transition ${
                    subFilter === "all"
                      ? "bg-primary-light text-primary"
                      : "bg-transparent text-text-secondary hover:text-text"
                  }`}
                >
                  All jobs
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setSubFilter("saved");
                    setPage(1);
                  }}
                  className={`cursor-pointer rounded-md border-0 px-3 py-1 text-xs font-semibold transition ${
                    subFilter === "saved"
                      ? "bg-primary-light text-primary"
                      : "bg-transparent text-text-secondary hover:text-text"
                  }`}
                >
                  Saved jobs ({savedCount})
                </button>
              </div>
            )}
          </div>

          {mainTab === "insights" ? (
            <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
              <Card className="lg:col-span-7">
                <CardHeader title="Most Requested Skills Across Live Openings" />
                <div className="flex flex-col gap-3 p-5">
                  {marketInsights.topSkills.map((row) => (
                    <div key={row.skill} className="text-sm">
                      <div className="mb-1 flex items-center justify-between text-xs">
                        <span className="font-semibold text-text">{row.skill}</span>
                        <span className="dash-num text-text-secondary">
                          {row.count} {row.count === 1 ? "role" : "roles"} ({row.pct}%)
                        </span>
                      </div>
                      <div className="h-2 w-full overflow-hidden rounded-full bg-border/60">
                        <div
                          className="h-full rounded-full bg-gradient-to-r from-primary to-accent"
                          style={{ width: `${Math.max(8, row.pct)}%` }}
                        />
                      </div>
                    </div>
                  ))}
                </div>
              </Card>

              <div className="flex flex-col gap-6 lg:col-span-5">
                <Card>
                  <CardHeader title="Work Mode Distribution" />
                  <div className="grid grid-cols-3 gap-3 p-5 text-center">
                    {(["Remote", "Hybrid", "On-site"] as const).map((mode) => (
                      <div key={mode} className="rounded-control border border-border bg-bg/70 p-3">
                        <div className="dash-num text-xl font-bold text-primary">
                          {marketInsights.modeCounts[mode] ?? 0}
                        </div>
                        <div className="mt-0.5 text-xs text-text-secondary">{mode}</div>
                      </div>
                    ))}
                  </div>
                </Card>

                <Card>
                  <CardHeader title="Top Hiring Hubs & Readiness" />
                  <div className="flex flex-col gap-2.5 p-5 text-xs">
                    <div className="flex items-center justify-between rounded-control bg-primary-light/40 px-3.5 py-2.5">
                      <span className="font-semibold text-text">Your average match across all roles</span>
                      <span className="dash-num text-sm font-bold text-primary">{marketInsights.avgMatch}%</span>
                    </div>
                    {marketInsights.topLocations.map((loc) => (
                      <div key={loc.location} className="flex items-center justify-between border-b border-border/60 py-1.5 last:border-0">
                        <span className="text-text">{loc.location}</span>
                        <span className="dash-num font-semibold text-text-secondary">{loc.count} openings</span>
                      </div>
                    ))}
                  </div>
                </Card>
              </div>
            </div>
          ) : (
            <>
              {/* Filter Bar: Keyword, Location, Work mode, Sort + Search / Reset */}
              <Card className="mb-6 p-4">
                <form onSubmit={handleSearch} className="grid grid-cols-1 gap-3 md:grid-cols-12">
                  <div className="relative md:col-span-4">
                    <span className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-text-secondary">
                      <UiIcon name="search" />
                    </span>
                    <input
                      type="text"
                      value={keywordInput}
                      onChange={(e) => setKeywordInput(e.target.value)}
                      placeholder="Keyword (title, company, skill)…"
                      className="w-full rounded-control border border-border bg-bg py-2 pr-3 pl-9 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div className="md:col-span-3">
                    <input
                      type="text"
                      value={locationInput}
                      onChange={(e) => setLocationInput(e.target.value)}
                      placeholder="Location (e.g., Cairo, Remote)…"
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div className="md:col-span-2">
                    <select
                      aria-label="Work mode"
                      value={workModeInput}
                      onChange={(e) => setWorkModeInput(e.target.value)}
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-xs font-medium text-text outline-none focus:border-primary"
                    >
                      <option value="all">All Work Modes</option>
                      <option value="Remote">Remote</option>
                      <option value="Hybrid">Hybrid</option>
                      <option value="On-site">On-site</option>
                    </select>
                  </div>
                  <div className="md:col-span-3">
                    <select
                      aria-label="Sort jobs"
                      value={sortInput}
                      onChange={(e) => setSortInput(e.target.value as "match" | "newest" | "company")}
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-xs font-medium text-text outline-none focus:border-primary"
                    >
                      <option value="match">Sort: Best AI Match</option>
                      <option value="newest">Sort: Newest First</option>
                      <option value="company">Sort: Company A–Z</option>
                    </select>
                  </div>
                  <div className="flex flex-wrap items-center justify-between gap-2 pt-1 md:col-span-12">
                    <span className="text-xs text-text-secondary">
                      Showing <strong>{filtered.length}</strong> matching {filtered.length === 1 ? "role" : "roles"}
                    </span>
                    <div className="flex gap-2">
                      <Button type="button" size="sm" variant="secondary" onClick={handleReset}>
                        Reset
                      </Button>
                      <Button type="submit" size="sm">
                        <UiIcon name="search" />
                        Search
                      </Button>
                    </div>
                  </div>
                </form>
              </Card>

              {paginated.length === 0 ? (
                <Card>
                  <EmptyState icon="briefcase" message="No jobs match your current filters. Try clicking Reset or Sync jobs." />
                </Card>
              ) : (
                <>
                  <div className="flex flex-col gap-4">
                    {paginated.map((job) => (
                      <Card key={job.id} className="p-5">
                        <div className="flex flex-wrap items-start justify-between gap-4">
                          <div>
                            <div className="flex flex-wrap items-center gap-2">
                              <h3 className="m-0 text-lg font-semibold text-text">{job.title}</h3>
                              <span className="dash-num rounded-md bg-primary-light px-2.5 py-0.5 text-xs font-bold text-primary">
                                {job.matchPercent}% AI Match
                              </span>
                              {job.applicationStatus && (
                                <StatusBadge
                                  status={
                                    job.applicationStatus === "Interview" || job.applicationStatus === "Offer"
                                      ? "Passed"
                                      : "In progress"
                                  }
                                />
                              )}
                            </div>
                            <div className="mt-1 text-xs text-text-secondary">
                              <strong className="text-text">{job.company}</strong> · {job.location} ({job.workMode}) ·{" "}
                              <span className="dash-num font-medium text-text">{job.salaryRange}</span>
                            </div>
                          </div>
                          <div className="flex flex-wrap gap-2">
                            <Button
                              size="sm"
                              variant="secondary"
                              onClick={() => setOpenedJob(job)}
                            >
                              <UiIcon name="external-link" />
                              Open
                            </Button>
                            <Button
                              size="sm"
                              variant="secondary"
                              disabled={busyJobId === job.id}
                              onClick={() => void applyOrSave(job, "Saved")}
                            >
                              {job.applicationStatus === "Saved" ? "Saved ✓" : "Save"}
                            </Button>
                            <Button
                              size="sm"
                              disabled={busyJobId === job.id}
                              onClick={() => void applyOrSave(job, "Applied")}
                            >
                              {job.applicationStatus && job.applicationStatus !== "Saved"
                                ? `${job.applicationStatus} ✓`
                                : "Mark as applied"}
                            </Button>
                          </div>
                        </div>
                        <p className="mt-3 mb-3 text-sm text-text-secondary">{job.description}</p>
                        <div className="flex flex-wrap gap-2 text-xs">
                          {job.matchedSkills.map((sk) => (
                            <span key={sk} className="rounded-md bg-success-light px-2 py-0.5 font-medium text-success">
                              ✓ {sk}
                            </span>
                          ))}
                          {job.missingSkills.map((sk) => (
                            <span key={sk} className="rounded-md bg-warning-light px-2 py-0.5 font-medium text-warning">
                              Skill Gap: {sk}
                            </span>
                          ))}
                        </div>
                      </Card>
                    ))}
                  </div>

                  {/* Pagination Controls */}
                  {totalPages > 1 && (
                    <div className="mt-6 flex flex-wrap items-center justify-between gap-3 rounded-card border border-border bg-card px-4 py-3 text-xs">
                      <span className="text-text-secondary">
                        Page <strong>{safePage}</strong> of <strong>{totalPages}</strong> ({filtered.length} total jobs)
                      </span>
                      <div className="flex items-center gap-2">
                        <Button
                          size="sm"
                          variant="secondary"
                          disabled={safePage <= 1}
                          onClick={() => setPage((p) => Math.max(1, p - 1))}
                        >
                          Previous
                        </Button>
                        <Button
                          size="sm"
                          variant="secondary"
                          disabled={safePage >= totalPages}
                          onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                        >
                          Next
                        </Button>
                      </div>
                    </div>
                  )}
                </>
              )}
            </>
          )}
        </>
      )}

      {/* Job Details Modal ("Open") */}
      {openedJob && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4 backdrop-blur-xs"
          role="dialog"
          aria-modal="true"
          onClick={() => setOpenedJob(null)}
        >
          <div
            className="w-full max-w-xl rounded-card border border-border bg-card p-6 shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-start justify-between gap-4">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="m-0 text-xl font-semibold text-text">{openedJob.title}</h2>
                  <span className="dash-num rounded bg-primary-light px-2 py-0.5 text-xs font-bold text-primary">
                    {openedJob.matchPercent}% Match
                  </span>
                </div>
                <p className="mt-1 text-xs text-text-secondary">
                  <strong className="text-text">{openedJob.company}</strong> · {openedJob.location} ({openedJob.workMode}) · {openedJob.salaryRange}
                </p>
              </div>
              <button
                type="button"
                onClick={() => setOpenedJob(null)}
                className="cursor-pointer rounded-md border border-border bg-bg p-1.5 text-text-secondary hover:text-text"
                aria-label="Close job details"
              >
                <UiIcon name="x" />
              </button>
            </div>

            <p className="mt-4 text-sm leading-relaxed text-text">{openedJob.description}</p>

            <div className="mt-4 rounded-control border border-border bg-bg/60 p-3.5">
              <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-text-secondary">
                Required Competencies
              </div>
              <div className="flex flex-wrap gap-1.5 text-xs">
                {openedJob.matchedSkills.map((sk) => (
                  <span key={sk} className="rounded-md bg-success-light px-2 py-0.5 font-medium text-success">
                    ✓ {sk}
                  </span>
                ))}
                {openedJob.missingSkills.map((sk) => (
                  <span key={sk} className="rounded-md bg-warning-light px-2 py-0.5 font-medium text-warning">
                    Missing: {sk}
                  </span>
                ))}
              </div>
            </div>

            {openedJob.sourceUrl && (
              <div className="mt-3 text-xs text-text-secondary">
                Source listing:{" "}
                <a
                  href={openedJob.sourceUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="font-semibold text-primary hover:underline"
                >
                  {openedJob.sourceUrl}
                </a>
              </div>
            )}

            <div className="mt-5 flex flex-wrap justify-end gap-2 border-t border-border pt-4">
              <Button
                size="sm"
                variant="secondary"
                disabled={busyJobId === openedJob.id}
                onClick={() => void applyOrSave(openedJob, "Saved")}
              >
                {openedJob.applicationStatus === "Saved" ? "Saved ✓" : "Save"}
              </Button>
              <Button
                size="sm"
                disabled={busyJobId === openedJob.id}
                onClick={() => void applyOrSave(openedJob, "Applied")}
              >
                {openedJob.applicationStatus && openedJob.applicationStatus !== "Saved"
                  ? `${openedJob.applicationStatus} ✓`
                  : "Mark as applied"}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
