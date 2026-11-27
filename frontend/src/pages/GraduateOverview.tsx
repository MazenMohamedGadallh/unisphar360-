import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { clearToken } from "../lib/api";
import { gradFetch, loadGradCareer } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface GraduateProfile {
  headline: string;
  targetRole: string;
  location: string;
  skills: string[];
  cvFileName: string | null;
  cvUploadedAtUtc: string | null;
  cvScore?: number | null;
  cvSummary: string | null;
}

interface OverviewKpis {
  profileCompletionPercent: number;
  matchingJobsCount: number;
  savedJobsCount: number;
  activeApplicationsCount: number;
  followUpsDueCount: number;
  roadmapProgressPercent: number;
}

interface NextActionItem {
  id: string;
  title: string;
  detail: string;
  link: string;
  cta: string;
  done: boolean;
}

interface SkillGapItem {
  skill: string;
  jobsRequiring: string;
}

interface UpcomingEventItem {
  id: number;
  title: string;
  dateLabel: string;
  location: string;
}

interface ActivityItem {
  id: number;
  text: string;
  createdAt: string;
}

interface NotificationItem {
  id: number;
  title: string;
  body: string;
  isRead: boolean;
  createdAtUtc: string;
}


interface GradOverviewResponse {
  profile: GraduateProfile;
  kpis: OverviewKpis;
  nextActions: NextActionItem[];
  topSkillGaps: SkillGapItem[];
  upcomingEvents: UpcomingEventItem[];
  recentActivity: ActivityItem[];
  careerNotifications: NotificationItem[];
  milestones: Array<{ id: number; phase: string; title: string; completed: boolean }>;
  interviewCount?: number;
  interviewAvgScore?: number | null;
}

const fmtDateTime = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

export function GraduateOverview() {
  const navigate = useNavigate();
  const [data, setData] = useState<GradOverviewResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    document.title = "Career Overview — UniSphere 360+";
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [overview, career, notifications, roadmap] = await Promise.all([
        gradFetch<any>("grad/overview"),
        loadGradCareer(),
        gradFetch<NotificationItem[]>("notifications"),
        gradFetch<{ id: number; title: string; category: string | null; status: string }[]>("grad/roadmap"),
      ]);
      const { profile, cv, analysis } = career;
      // Next actions arrive as plain sentences; route each to the page that handles it.
      const linkFor = (text: string) =>
        /cv|resume/i.test(text) ? "/graduate/cv"
        : /job|match|appl/i.test(text) ? "/graduate/jobs"
        : /roadmap|skill|milestone/i.test(text) ? "/graduate/roadmap"
        : /interview/i.test(text) ? "/graduate/interview"
        : "/graduate/cv";
      setData({
        profile: {
          headline: profile.headline ?? "",
          targetRole: profile.preferredJobTitles ?? "",
          location: profile.preferredLocations ?? "",
          skills: profile.skills.map((s) => s.name),
          cvFileName: cv?.fileName ?? null,
          cvUploadedAtUtc: cv?.uploadedAtUtc ?? null,
          cvScore: analysis?.scorePercent ?? null,
          cvSummary: analysis?.overallAssessment ?? null,
        },
        kpis: {
          profileCompletionPercent: overview.profileCompletionPercent,
          matchingJobsCount: overview.matchingJobsCount,
          savedJobsCount: overview.savedJobsCount,
          activeApplicationsCount: overview.applicationsInProgress,
          followUpsDueCount: overview.upcomingFollowUpsCount,
          roadmapProgressPercent: overview.roadmapProgressPercent,
        },
        nextActions: overview.nextActions.map((title: string) => ({
          id: title,
          title,
          detail: "",
          link: linkFor(title),
          cta: "Open",
          done: false,
        })),
        topSkillGaps: overview.topSkillGaps,
        upcomingEvents: overview.upcomingEvents.map((e: any) => ({ ...e, dateLabel: fmtDateTime(e.startsAtUtc) })),
        recentActivity: overview.recentActivity.map((a: any) => ({ id: a.id, text: a.message, createdAt: a.createdAtUtc })),
        careerNotifications: notifications.slice(0, 5),
        milestones: roadmap.map((m) => ({ id: m.id, title: m.title, phase: m.category ?? "", completed: m.status === "Completed" })),
      });
    } catch (e) {
      if ((e as Error).name === "Unauthorized") {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      setError("Could not load Career Overview.");
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Career Overview</h1>
          <p className="mt-1 text-sm text-text-secondary">
            {data
              ? `${data.profile.headline || data.profile.targetRole} · ${data.profile.location || "Open to opportunities"}`
              : "Live graduate career command center — profile completion, job matches, follow-ups, and next actions."}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" onClick={() => void load()}>
            <UiIcon name="refresh-cw" />
            Refresh
          </Button>
          <Button size="sm" variant="secondary" to="/graduate/cv">
            <UiIcon name="file-text" />
            CV &amp; Resume Studio
          </Button>
          <Button size="sm" to="/graduate/jobs">
            <UiIcon name="briefcase" />
            Explore Jobs
          </Button>
        </div>
      </div>

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading || !data ? (
        <LoadingBlock label="Loading your Career Overview…" />
      ) : (
        <>
          {/* 6 Live KPI Counters */}
          <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
            <button
              type="button"
              onClick={() => navigate("/graduate/cv")}
              className="dash-stat gpa cursor-pointer text-left"
            >
              <div className="dash-stat-icon"><UiIcon name="user-round" /></div>
              <span className="dash-stat-label">Profile completion</span>
              <span className="dash-stat-value">{data.kpis.profileCompletionPercent}%</span>
              <span className="mt-1 text-[11px] text-text-secondary">{data.profile.skills.length} skills listed</span>
            </button>

            <button
              type="button"
              onClick={() => navigate("/graduate/jobs")}
              className="dash-stat attendance cursor-pointer text-left"
            >
              <div className="dash-stat-icon"><UiIcon name="briefcase" /></div>
              <span className="dash-stat-label">Matching jobs</span>
              <span className="dash-stat-value">{data.kpis.matchingJobsCount}</span>
              <span className="mt-1 text-[11px] text-text-secondary">50%+ skill match</span>
            </button>

            <button
              type="button"
              onClick={() => navigate("/graduate/jobs")}
              className="dash-stat courses cursor-pointer text-left"
            >
              <div className="dash-stat-icon"><UiIcon name="bookmark" /></div>
              <span className="dash-stat-label">Saved jobs</span>
              <span className="dash-stat-value">{data.kpis.savedJobsCount}</span>
              <span className="mt-1 text-[11px] text-text-secondary">Ready to apply</span>
            </button>

            <button
              type="button"
              onClick={() => navigate("/graduate/applications")}
              className="dash-stat recommended cursor-pointer text-left"
            >
              <div className="dash-stat-icon"><UiIcon name="clipboard-check" /></div>
              <span className="dash-stat-label">Applications</span>
              <span className="dash-stat-value">{data.kpis.activeApplicationsCount}</span>
              <span className="mt-1 text-[11px] text-text-secondary">In progress</span>
            </button>

            <button
              type="button"
              onClick={() => navigate("/graduate/applications")}
              className="dash-stat gpa cursor-pointer text-left"
            >
              <div className="dash-stat-icon"><UiIcon name="calendar-clock" /></div>
              <span className="dash-stat-label">Follow-ups due</span>
              <span className="dash-stat-value">{data.kpis.followUpsDueCount}</span>
              <span className="mt-1 text-[11px] text-text-secondary">This week</span>
            </button>

            <button
              type="button"
              onClick={() => navigate("/graduate/roadmap")}
              className="dash-stat attendance cursor-pointer text-left"
            >
              <div className="dash-stat-icon"><UiIcon name="map" /></div>
              <span className="dash-stat-label">Roadmap progress</span>
              <span className="dash-stat-value">{data.kpis.roadmapProgressPercent}%</span>
              <span className="mt-1 text-[11px] text-text-secondary">
                {data.milestones.filter((m) => m.completed).length}/{data.milestones.length} done
              </span>
            </button>
          </div>

          {/* Row 1: Next Actions + CV Status */}
          <div className="mb-6 grid grid-cols-1 gap-6 lg:grid-cols-12">
            <Card className="overflow-hidden lg:col-span-7">
              <CardHeader
                title="Next Actions"
              />
              <div className="divide-y divide-border">
                {data.nextActions.map((act) => (
                  <div key={act.id} className="flex flex-wrap items-center justify-between gap-3 px-5 py-3.5">
                    <div className="flex min-w-0 flex-1 items-start gap-3">
                      <span
                        className={`mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs ${
                          act.done
                            ? "bg-success-light text-success"
                            : "bg-primary-light text-primary"
                        }`}
                      >
                        <UiIcon name={act.done ? "check" : "arrow-right"} />
                      </span>
                      <div className="min-w-0">
                        <div className="text-sm font-semibold text-text">{act.title}</div>
                        <div className="mt-0.5 text-xs text-text-secondary">{act.detail}</div>
                      </div>
                    </div>
                    <Button size="sm" variant={act.done ? "secondary" : "primary"} to={act.link}>
                      {act.cta}
                    </Button>
                  </div>
                ))}
              </div>
            </Card>

            <Card className="flex flex-col justify-between overflow-hidden lg:col-span-5">
              <div>
                <CardHeader
                  title="CV Status"
                  action={
                    <Button size="sm" variant="secondary" to="/graduate/cv">
                      Manage CV
                    </Button>
                  }
                />
                <div className="flex flex-col gap-3 p-5 text-sm">
                  <div className="flex items-center justify-between rounded-control border border-border bg-bg/70 p-4">
                    <div className="min-w-0">
                      <div className="truncate font-semibold text-text">
                        {data.profile.cvFileName ?? "No CV uploaded yet"}
                      </div>
                      <div className="mt-0.5 text-xs text-text-secondary">
                        {data.profile.cvUploadedAtUtc
                          ? `Updated ${fmtDateTime(data.profile.cvUploadedAtUtc)}`
                          : "Upload PDF, DOCX, or TXT up to 10 MB"}
                      </div>
                    </div>
                    <div className="dash-num shrink-0 rounded-md bg-primary-light px-3 py-1.5 text-base font-bold text-primary">
                      {data.profile.cvScore !== null && data.profile.cvScore !== undefined
                        ? `${data.profile.cvScore}/100`
                        : "—"}
                    </div>
                  </div>
                  {data.profile.cvSummary ? (
                    <p className="m-0 text-xs leading-relaxed text-text-secondary">{data.profile.cvSummary}</p>
                  ) : (
                    <p className="m-0 text-xs text-text-secondary">
                      Run AI CV analysis in CV &amp; Resume Studio to receive an ATS readiness score, keyword matches, and tailored improvement suggestions.
                    </p>
                  )}
                </div>
              </div>
              <div className="flex flex-wrap items-center justify-between gap-2 border-t border-border bg-bg/50 px-5 py-3 text-xs text-text-secondary">
                <span>Target role: <strong className="text-text">{data.profile.targetRole}</strong></span>
                <Button size="sm" variant="secondary" to="/graduate/interview">
                  <UiIcon name="mic" />
                  Practice Interview
                </Button>
              </div>
            </Card>
          </div>

          {/* Row 2: Top Skill Gaps + Upcoming Events */}
          <div className="mb-6 grid grid-cols-1 gap-6 lg:grid-cols-12">
            <Card className="overflow-hidden lg:col-span-6">
              <CardHeader
                title="Top Skill Gaps"
                action={
                  <Button size="sm" variant="secondary" to="/graduate/roadmap">
                    Open Skill Gap
                  </Button>
                }
              />
              {data.topSkillGaps.length === 0 ? (
                <EmptyState icon="award" message="Great work — you match all required skills across open roles!" />
              ) : (
                <div className="divide-y divide-border">
                  {data.topSkillGaps.map((gap) => (
                    <div key={gap.skill} className="flex items-center justify-between gap-3 px-5 py-3.5 text-sm">
                      <div>
                        <div className="font-semibold text-text">{gap.skill}</div>
                        <div className="text-xs text-text-secondary">
                          Required by: {gap.jobsRequiring}
                        </div>
                      </div>
                      <Button size="sm" variant="secondary" to="/graduate/roadmap">
                        Close Gap
                      </Button>
                    </div>
                  ))}
                </div>
              )}
            </Card>

            <Card className="overflow-hidden lg:col-span-6">
              <CardHeader
                title="Upcoming Campus Events"
                action={
                  <Button size="sm" variant="secondary" to="/graduate/alumni">
                    Open Campus
                  </Button>
                }
              />
              {data.upcomingEvents.length === 0 ? (
                <EmptyState icon="calendar-days" message="No upcoming campus career events scheduled." />
              ) : (
                <div className="divide-y divide-border">
                  {data.upcomingEvents.map((ev) => (
                    <div key={ev.id} className="flex flex-wrap items-center justify-between gap-3 px-5 py-3.5 text-sm">
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-semibold text-text">{ev.title}</span>
                        </div>
                        <div className="mt-0.5 text-xs text-text-secondary">
                          {ev.dateLabel} · {ev.location}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </Card>
          </div>

          {/* Row 3: Recent Activity + Career Notifications */}
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <Card className="overflow-hidden lg:col-span-6">
              <CardHeader
                title="Recent Activity"
                action={
                  <Button size="sm" variant="secondary" to="/graduate/applications">
                    View Applications
                  </Button>
                }
              />
              {data.recentActivity.length === 0 ? (
                <EmptyState icon="history" message="Your recent CV, job, roadmap, and interview actions will appear here." />
              ) : (
                <div className="divide-y divide-border">
                  {data.recentActivity.map((item) => (
                    <div key={item.id} className="flex items-start justify-between gap-3 px-5 py-3 text-sm">
                      <div className="flex items-start gap-2.5">
                        <span className="mt-1 h-2 w-2 shrink-0 rounded-full bg-primary" />
                        <span className="text-text">{item.text}</span>
                      </div>
                      <span className="shrink-0 text-[11px] text-text-muted">{fmtDateTime(item.createdAt)}</span>
                    </div>
                  ))}
                </div>
              )}
            </Card>

            <Card className="overflow-hidden lg:col-span-6">
              <CardHeader
                title="Career Notifications"
                action={
                  <Button size="sm" variant="secondary" to="/notifications">
                    All Notifications
                  </Button>
                }
              />
              {data.careerNotifications.length === 0 ? (
                <EmptyState icon="bell" message="You're all caught up on career notifications." />
              ) : (
                <div className="divide-y divide-border">
                  {data.careerNotifications.map((n) => (
                    <div key={n.id} className="px-5 py-3 text-sm">
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-semibold text-text">{n.title}</span>
                        <span className="text-[11px] text-text-muted">{fmtDateTime(n.createdAtUtc)}</span>
                      </div>
                      <p className="m-0 mt-0.5 text-xs text-text-secondary">{n.body}</p>
                    </div>
                  ))}
                </div>
              )}
            </Card>
          </div>
        </>
      )}
    </div>
  );
}
