import { useEffect } from "react";
import { Link, useNavigate } from "react-router-dom";
import { gradFetch, loadGradCareer, onboardingSkippedKey } from "../lib/grad";
import { Button } from "../components/ui";
import { Bullets, Meter, Page, Panel, Tag, Trail, fmtDate, useGradData, type Journey } from "../components/grad";

interface Overview {
  firstName: string;
  hasCompletedOnboarding: boolean;
  upcomingFollowUpsCount: number;
  applicationsCount: number;
  upcomingEvents: { id: number; title: string; startsAtUtc: string }[];
  recentActivity: { id: number; message: string; createdAtUtc: string }[];
}
interface Gap { target: string; skills: { skill: string; priority: string; evidence: string }[] }
interface Match { job: { id: number; title: string; companyName: string; source: string }; scorePercent: number }
interface Report { current: { dimensions: { name: string; percent: number | null }[] } }

async function load() {
  const [overview, journey, career, gap, matches, report, projects] = await Promise.all([
    gradFetch<Overview>("graduate/overview"),
    gradFetch<Journey>("graduate/journey"),
    loadGradCareer(),
    gradFetch<Gap>("graduate/skill-gap", { method: "POST", body: "{}" }),
    gradFetch<Match[]>("grad/jobs/matches?limit=3&minScore=1"),
    gradFetch<Report>("graduate/report"),
    gradFetch<{ complete: boolean }[]>("graduate/portfolio"),
  ]);
  return { overview, journey, career, gap, matches, report, projects };
}

export function GraduateDashboard() {
  const navigate = useNavigate();
  const { data, error, reload } = useGradData(load);

  // First visit only: onboarding is offered until it is completed or explicitly skipped.
  useEffect(() => {
    if (data && !data.overview.hasCompletedOnboarding && !localStorage.getItem(onboardingSkippedKey(data.career.profile.userId)))
      navigate("/graduate/onboarding", { replace: true });
  }, [data, navigate]);

  const gaps = data?.gap.skills.filter((s) => s.priority === "High" || s.priority === "Medium").slice(0, 4) ?? [];
  const next = data?.journey.nextMission;
  const earned = data?.journey.levels.filter((l) => l.status === "completed") ?? [];

  return (
    <Page
      title={data ? `Welcome back, ${data.overview.firstName}` : "Dashboard"}
      description="Where you are, what you have proven, and the one thing to do next."
      loading={!data} error={error} onRetry={() => void reload()}
    >
      {data && (
        <div className="grid gap-5">
          <Panel>
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <p className="m-0 text-sm text-text-secondary">
                  Level {data.journey.careerLevel} of 8. {data.journey.completedMissions} of {data.journey.totalMissions} missions done.
                </p>
                <p className="m-0 mt-1 text-2xl font-bold text-text tabular-nums">{data.journey.xp} XP</p>
              </div>
              {next ? (
                <div className="flex max-w-xl flex-wrap items-center gap-4 rounded-card bg-primary-light px-4 py-3">
                  <div>
                    <p className="m-0 text-sm font-semibold text-text">Next: {next.title} (+{next.xp} XP)</p>
                    <p className="m-0 text-sm text-text-secondary">{next.criteria}</p>
                  </div>
                  <Button size="sm" to={next.route}>Start</Button>
                </div>
              ) : (
                <Tag tone="good">Every mission is complete</Tag>
              )}
            </div>
            <div className="mt-6"><Trail journey={data.journey} /></div>
          </Panel>

          <div className="grid gap-5 lg:grid-cols-3">
            <Panel title="Readiness" aside={<Link to="/graduate/reports" className="text-sm text-primary">Full report</Link>} className="lg:col-span-2">
              <div className="grid gap-4 sm:grid-cols-2">
                {data.report.current.dimensions.map((d) => <Meter key={d.name} name={d.name} value={d.percent} />)}
              </div>
            </Panel>

            <Panel title="Latest CV analysis" aside={<Link to="/graduate/cv" className="text-sm text-primary">Open</Link>}>
              {data.career.analysis ? (
                <>
                  <p className="m-0 text-3xl font-bold text-primary tabular-nums">{data.career.analysis.scorePercent}/100</p>
                  <p className="mt-2 mb-0 text-sm text-text-secondary">{data.career.analysis.overallAssessment}</p>
                </>
              ) : (
                <p className="m-0 text-sm text-text-secondary">
                  {data.career.cv ? "Your CV is uploaded. Open CV Analysis and select Analyze CV." : "Upload your CV to see how a screening system reads it."}
                </p>
              )}
            </Panel>

            <Panel title={`Skill gaps for ${data.gap.target}`} aside={<Link to="/graduate/skill-gap" className="text-sm text-primary">Compare</Link>}>
              {gaps.length === 0 ? (
                <p className="m-0 text-sm text-text-muted">No priority gaps for this role.</p>
              ) : (
                <ul className="m-0 list-none space-y-2.5 p-0 text-sm">
                  {gaps.map((g) => (
                    <li key={g.skill} className="flex justify-between gap-2">
                      <span className="text-text">{g.skill}</span>
                      <Tag tone={g.priority === "High" ? "warn" : "plain"}>{g.evidence}</Tag>
                    </li>
                  ))}
                </ul>
              )}
            </Panel>

            <Panel title="Best matching jobs" aside={<Link to="/graduate/jobs" className="text-sm text-primary">All jobs</Link>}>
              {data.matches.length === 0 ? (
                <p className="m-0 text-sm text-text-muted">No matches yet. Add skills or upload a CV.</p>
              ) : (
                <ul className="m-0 list-none space-y-3 p-0 text-sm">
                  {data.matches.map((m) => (
                    <li key={m.job.id} className="flex items-start justify-between gap-3">
                      <span className="min-w-0">
                        <span className="block text-text">{m.job.title}</span>
                        <span className="text-text-muted">{m.job.companyName}{m.job.source === "Demo" ? " (sample listing)" : ""}</span>
                      </span>
                      <Tag tone="info">{m.scorePercent}%</Tag>
                    </li>
                  ))}
                </ul>
              )}
            </Panel>

            <Panel title="Portfolio and practice">
              <p className="m-0 text-sm text-text-secondary">
                {data.projects.length === 0 ? "No projects yet." : `${data.projects.filter((p) => p.complete).length} of ${data.projects.length} projects pass the checks.`}{" "}
                {data.overview.applicationsCount} application(s) recorded
                {data.overview.upcomingFollowUpsCount > 0 ? `, ${data.overview.upcomingFollowUpsCount} follow-up(s) due.` : "."}
              </p>
              <div className="mt-4 flex flex-wrap gap-2">
                <Button size="sm" variant="secondary" to="/graduate/portfolio">Portfolio</Button>
                <Button size="sm" variant="secondary" to="/graduate/interview">Practise an interview</Button>
              </div>
            </Panel>

            <Panel title="Achievements">
              {earned.length === 0 ? (
                <p className="m-0 text-sm text-text-muted">Complete level 1 to earn your first badge.</p>
              ) : (
                <div className="flex flex-wrap gap-2">{earned.map((l) => <Tag key={l.level} tone="good">{l.badge}</Tag>)}</div>
              )}
            </Panel>

            <Panel title="Recent activity" className="lg:col-span-2">
              <Bullets items={data.overview.recentActivity.map((a) => `${a.message} (${fmtDate(a.createdAtUtc)})`)} empty="Your progress will appear here." />
              {data.overview.upcomingEvents.length > 0 && (
                <p className="mt-4 mb-0 text-sm text-text-muted">
                  Next campus event: {data.overview.upcomingEvents[0].title}, {fmtDate(data.overview.upcomingEvents[0].startsAtUtc)}.
                </p>
              )}
            </Panel>
          </div>
        </div>
      )}
    </Page>
  );
}
