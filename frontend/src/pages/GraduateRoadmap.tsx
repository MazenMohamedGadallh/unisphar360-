import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock } from "../components/ui";
import { clearToken } from "../lib/api";
import { gradFetch } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import { CareerQuest } from "../components/CareerQuest";
import "./dashboard.css";

interface GraduateProfile {
  headline: string;
  summary: string;
  targetRole: string;
  location: string;
  linkedinUrl: string;
  githubUrl: string;
  skills: string[];
}

interface Milestone {
  id: number;
  phase: string;
  title: string;
  detail: string;
  completed: boolean;
  targetDate: string | null;
  sortOrder: number;
}

const toMilestone = (item: any): Milestone => ({
  id: item.id,
  phase: item.category ?? "",
  title: item.title,
  detail: item.notes ?? "",
  completed: item.status === "Completed",
  targetDate: item.targetDate,
  sortOrder: item.sortOrder,
});

interface SkillGapAnalysis {
  targetRole: string;
  coveragePercent: number;
  totalBenchmarkSkills: number;
  skillsYouHave: string[];
  skillsToLearnNext: Array<{ skill: string; jobsRequiring: string; priority: string }>;
}

export function GraduateRoadmap() {
  const navigate = useNavigate();
  const [activeTab, setActiveTab] = useState<"quest" | "skill-gap" | "roadmap">(
    window.location.pathname.includes("skill-gap") ? "skill-gap" : "quest",
  );

  const [profile, setProfile] = useState<GraduateProfile | null>(null);
  const [milestones, setMilestones] = useState<Milestone[]>([]);
  const [analysis, setAnalysis] = useState<SkillGapAnalysis | null>(null);
  const [selectedRole, setSelectedRole] = useState("");
  const [roles, setRoles] = useState<string[]>([]);
  const [analyzing, setAnalyzing] = useState(false);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const [phase, setPhase] = useState("Phase 3 — Specialization");
  const [title, setTitle] = useState("");
  const [detail, setDetail] = useState("");
  const [adding, setAdding] = useState(false);
  const [generatingAi, setGeneratingAi] = useState(false);

  useEffect(() => {
    document.title = "Quizzes and Plan - UniSphere 360+";
  }, []);

  const runSkillGapAnalyze = useCallback(async (roleOverride?: string, silent = false) => {
    setAnalyzing(true);
    if (!silent) setToast(null);
    try {
      const gap = await gradFetch<any>(`grad/jobs/skill-gap?role=${encodeURIComponent(roleOverride ?? "")}`);
      const result: SkillGapAnalysis = {
        targetRole: gap.targetRole,
        coveragePercent: Math.round(gap.coveragePercent),
        totalBenchmarkSkills: gap.matched.length + gap.missing.length,
        skillsYouHave: gap.matched.map((m: any) => m.skill),
        skillsToLearnNext: gap.missing,
      };
      setAnalysis(result);
      setSelectedRole(result.targetRole);
      if (!silent) {
        setToast({
          text: `Analyzed "${result.targetRole}" — ${result.coveragePercent}% skill coverage (${result.skillsYouHave.length}/${result.totalBenchmarkSkills} skills matched).`,
          isError: false,
        });
      }
    } catch {
      if (!silent) setToast({ text: "Could not analyze skill gap right now.", isError: true });
    } finally {
      setAnalyzing(false);
    }
  }, []);

  const fetchSkills = useCallback(async () => {
    const server = await gradFetch<any>("grad/profile");
    const view: GraduateProfile = {
      headline: server.headline ?? "",
      summary: server.careerSummary ?? "",
      targetRole: server.targetRole ?? server.preferredJobTitles?.split(",")[0]?.trim() ?? "",
      location: server.preferredLocations ?? "",
      linkedinUrl: server.linkedInUrl ?? "",
      githubUrl: server.gitHubUrl ?? "",
      skills: server.skills.map((s: any) => s.name),
    };
    setProfile(view);
    return view;
  }, []);

  const fetchMilestones = useCallback(async () => {
    setMilestones((await gradFetch<any[]>("grad/roadmap")).map(toMilestone));
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [view, calibration] = await Promise.all([
        fetchSkills(),
        gradFetch<{ roles: string[]; targetRole: string | null }>("graduate/profile/onboarding"),
        fetchMilestones(),
      ]);
      setRoles(calibration.roles);
      // Only catalog roles can be benchmarked; anything else lets the server pick its default.
      const wanted = calibration.targetRole ?? view.targetRole;
      const role = calibration.roles.includes(wanted) ? wanted : "";
      setSelectedRole(role);
      await runSkillGapAnalyze(role, true);
    } catch (e) {
      if ((e as Error).name === "Unauthorized") {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      setError("Could not load Skills & Roadmap.");
    } finally {
      setLoading(false);
    }
  }, [navigate, runSkillGapAnalyze, fetchSkills, fetchMilestones]);

  useEffect(() => {
    void load();
  }, [load]);

  async function toggleMilestone(id: number) {
    try {
      const item = milestones.find((m) => m.id === id)!;
      const completed = !item.completed;
      await gradFetch(`grad/roadmap/${id}`, {
        method: "PUT",
        body: JSON.stringify({
          title: item.title,
          category: item.phase || null,
          notes: item.detail || null,
          targetDate: item.targetDate,
          sortOrder: item.sortOrder,
          status: completed ? "Completed" : "NotStarted",
        }),
      });
      setMilestones((prev) => prev.map((m) => (m.id === id ? { ...m, completed } : m)));
    } catch {
      setToast({ text: "Could not update milestone.", isError: true });
    }
  }

  async function addMilestone(e: FormEvent) {
    e.preventDefault();
    if (!title.trim()) return;
    setAdding(true);
    setToast(null);
    try {
      const created = toMilestone(
        await gradFetch("grad/roadmap", {
          method: "POST",
          body: JSON.stringify({ category: phase.slice(0, 40), title: title.trim(), notes: detail.trim() || null }),
        }),
      );
      setMilestones((prev) => [...prev, created]);
      setTitle("");
      setDetail("");
      setToast({ text: `Added "${created.title}" to your learning roadmap.`, isError: false });
    } catch {
      setToast({ text: "Failed to add roadmap milestone.", isError: true });
    } finally {
      setAdding(false);
    }
  }

  async function deleteMilestone(id: number) {
    setToast(null);
    try {
      await gradFetch(`grad/roadmap/${id}`, { method: "DELETE" });
      setMilestones((prev) => prev.filter((m) => m.id !== id));
      setToast({ text: "Milestone removed from roadmap.", isError: false });
    } catch {
      setToast({ text: "Could not remove milestone.", isError: true });
    }
  }

  async function addSkillToRoadmap(skill: string) {
    setToast(null);
    try {
      await gradFetch("grad/roadmap", {
        method: "POST",
        body: JSON.stringify({
          category: "Skill Gap Sprint",
          title: `Master ${skill} for ${selectedRole}`.slice(0, 200),
          notes: `Close the ${skill} gap for ${selectedRole} by shipping a tested portfolio project to GitHub.`,
        }),
      });
      await fetchMilestones();
      setToast({ text: `Added "${skill}" learning sprint to your Roadmap tab.`, isError: false });
    } catch {
      setToast({ text: "Could not create milestone.", isError: true });
    }
  }

  async function markSkillAcquired(skill: string) {
    if (!profile) return;
    try {
      await gradFetch("grad/profile/skills", { method: "POST", body: JSON.stringify({ name: skill }) });
      await fetchSkills();
      await runSkillGapAnalyze(selectedRole, true);
      setToast({ text: `Added "${skill}" to your verified skills! Coverage updated.`, isError: false });
    } catch {
      setToast({ text: "Could not update profile skills.", isError: true });
    }
  }

  async function generateAiRoadmap() {
    setGeneratingAi(true);
    setToast(null);
    try {
      const { suggestions } = await gradFetch<{ suggestions: unknown[] }>("grad/roadmap/suggest");
      await gradFetch("grad/roadmap/apply-suggestions", { method: "POST", body: JSON.stringify(suggestions) });
      await fetchMilestones();
      setActiveTab("roadmap");
      setToast({ text: `Generated ${suggestions.length} tailored roadmap milestones.`, isError: false });
    } catch (e) {
      setToast({ text: e instanceof Error ? e.message : "Could not generate AI roadmap milestones.", isError: true });
    } finally {
      setGeneratingAi(false);
    }
  }

  const completedCount = milestones.filter((m) => m.completed).length;
  const progressPct = milestones.length ? Math.round((completedCount / milestones.length) * 100) : 0;
  const roleOptions = Array.from(new Set([...roles, selectedRole].filter(Boolean)));

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Quizzes and Plan</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Benchmark your skills against target roles, see exact coverage %, and track your interactive learning roadmap.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            disabled={generatingAi}
            onClick={() => void generateAiRoadmap()}
            className="btn-luminous inline-flex cursor-pointer items-center gap-2 rounded-control border-0 px-3.5 py-2 text-xs font-semibold disabled:opacity-60"
          >
            <UiIcon name="sparkles" />
            {generatingAi ? "Generating…" : "AI Generate Plan"}
          </button>
          <Button size="sm" variant="secondary" to="/graduate/jobs">
            <UiIcon name="briefcase" />
            Compare with Jobs
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading || !profile ? (
        <LoadingBlock label="Analyzing skill gaps & roadmap…" />
      ) : (
        <>
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="target" /></div>
              <span className="dash-stat-label">Role Skill Coverage</span>
              <span className="dash-stat-value">{analysis?.coveragePercent ?? 0}%</span>
            </div>
            <div className="dash-stat recommended">
              <div className="dash-stat-icon"><UiIcon name="award" /></div>
              <span className="dash-stat-label">Skills You Have</span>
              <span className="dash-stat-value">{analysis?.skillsYouHave.length ?? profile.skills.length}</span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="map" /></div>
              <span className="dash-stat-label">Roadmap Progress</span>
              <span className="dash-stat-value">{progressPct}%</span>
            </div>
          </div>

          {/* Tabs: Career Quest | Skill gap | Roadmap */}
          <div className="mb-5 inline-flex flex-wrap rounded-control border border-border bg-card p-1">
            <button
              type="button"
              onClick={() => setActiveTab("quest")}
              className={`cursor-pointer rounded-md border-0 px-4 py-1.5 text-xs font-semibold transition ${
                activeTab === "quest" ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
              }`}
            >
              Career Quest
            </button>
            <button
              type="button"
              onClick={() => navigate("/graduate/skill-gap")}
              className={`cursor-pointer rounded-md border-0 px-4 py-1.5 text-xs font-semibold transition ${
                activeTab === "skill-gap"
                  ? "bg-primary text-white"
                  : "bg-transparent text-text-secondary hover:text-text"
              }`}
            >
              Skill gap
            </button>
            <button
              type="button"
              onClick={() => setActiveTab("roadmap")}
              className={`cursor-pointer rounded-md border-0 px-4 py-1.5 text-xs font-semibold transition ${
                activeTab === "roadmap"
                  ? "bg-primary text-white"
                  : "bg-transparent text-text-secondary hover:text-text"
              }`}
            >
              Roadmap ({completedCount}/{milestones.length})
            </button>
          </div>

          {activeTab === "quest" ? (
            <CareerQuest />
          ) : activeTab === "skill-gap" ? (
            <div className="flex flex-col gap-6">
              {/* Target Role Selector + Analyze Button */}
              <Card className="p-5">
                <div className="flex flex-wrap items-end gap-3">
                  <div className="min-w-[240px] flex-1">
                    <label htmlFor="target-role-select" className="mb-1 block text-xs font-semibold text-text-secondary">
                      Target Role
                    </label>
                    <select
                      id="target-role-select"
                      value={selectedRole}
                      onChange={(e) => setSelectedRole(e.target.value)}
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    >
                      {roleOptions.map((r) => (
                        <option key={r} value={r}>
                          {r}
                        </option>
                      ))}
                    </select>
                  </div>
                  <Button
                    size="sm"
                    disabled={analyzing}
                    onClick={() => void runSkillGapAnalyze(selectedRole)}
                  >
                    <UiIcon name="sparkles" />
                    {analyzing ? "Analyzing…" : "Analyze"}
                  </Button>
                </div>

                {analysis && (
                  <div className="mt-4">
                    <div className="mb-1.5 flex items-center justify-between text-xs">
                      <span className="font-semibold text-text">
                        Coverage for {analysis.targetRole}
                      </span>
                      <span className="dash-num font-bold text-primary">
                        {analysis.coveragePercent}% ({analysis.skillsYouHave.length}/{analysis.totalBenchmarkSkills} skills)
                      </span>
                    </div>
                    <div className="h-2.5 w-full overflow-hidden rounded-full bg-border/60">
                      <div
                        className="h-full rounded-full bg-gradient-to-r from-primary to-accent transition-all duration-300"
                        style={{ width: `${analysis.coveragePercent}%` }}
                      />
                    </div>
                  </div>
                )}
              </Card>

              <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
                {/* Skills You Have */}
                <Card className="lg:col-span-5">
                  <CardHeader title={`Skills you have (${analysis?.skillsYouHave.length ?? 0})`} />
                  <div className="p-5">
                    {!analysis?.skillsYouHave.length ? (
                      <p className="m-0 text-xs text-text-secondary">
                        None of the benchmark skills for {selectedRole} are on your profile yet. Add skills in CV Studio or mark them below.
                      </p>
                    ) : (
                      <div className="flex flex-wrap gap-2">
                        {analysis.skillsYouHave.map((sk) => (
                          <span
                            key={sk}
                            className="rounded-md bg-success-light px-3 py-1 text-xs font-semibold text-success"
                          >
                            ✓ {sk}
                          </span>
                        ))}
                      </div>
                    )}
                    <div className="mt-5 border-t border-border pt-4">
                      <div className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-text-secondary">
                        All Verified Profile Skills ({profile.skills.length})
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        {profile.skills.map((sk) => (
                          <span key={sk} className="rounded-md border border-border bg-bg px-2.5 py-0.5 text-xs text-text-secondary">
                            {sk}
                          </span>
                        ))}
                      </div>
                    </div>
                  </div>
                </Card>

                {/* Skills to Learn Next */}
                <Card className="lg:col-span-7">
                  <CardHeader title={`Skills to learn next (${analysis?.skillsToLearnNext.length ?? 0})`} />
                  <div className="p-5">
                    {!analysis?.skillsToLearnNext.length ? (
                      <EmptyState icon="award" message={`You already have 100% of the core skills for ${selectedRole}!`} />
                    ) : (
                      <div className="flex flex-col gap-2.5">
                        {analysis.skillsToLearnNext.map((item) => (
                          <div
                            key={item.skill}
                            className="flex flex-wrap items-center justify-between gap-2 rounded-control border border-border bg-bg/70 px-4 py-3"
                          >
                            <div>
                              <div className="flex items-center gap-2">
                                <span className="text-sm font-semibold text-text">{item.skill}</span>
                                <span className="rounded bg-warning-light px-2 py-0.5 text-[10px] font-semibold text-warning">
                                  {item.priority}
                                </span>
                              </div>
                              <div className="text-[11px] text-text-secondary">
                                Required by: {item.jobsRequiring}
                              </div>
                            </div>
                            <div className="flex gap-1.5">
                              <Button size="sm" variant="secondary" onClick={() => void addSkillToRoadmap(item.skill)}>
                                + Roadmap
                              </Button>
                              <Button size="sm" onClick={() => void markSkillAcquired(item.skill)}>
                                I Know This
                              </Button>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </Card>
              </div>
            </div>
          ) : (
            <Card className="overflow-hidden">
              <CardHeader
                title={`Interactive Learning Roadmap (${completedCount}/${milestones.length})`}
                action={<span className="dash-num text-xs font-bold text-primary">{progressPct}% done</span>}
              />
              <div className="px-5 pt-4">
                <div className="h-2 w-full overflow-hidden rounded-full bg-border/60">
                  <div
                    className="h-full rounded-full bg-gradient-to-r from-primary to-accent transition-all duration-300"
                    style={{ width: `${progressPct}%` }}
                  />
                </div>
              </div>

              {milestones.length === 0 ? (
                <EmptyState icon="map" message="No roadmap milestones yet. Add one below or click AI Generate Plan." />
              ) : (
                <div className="mt-2 divide-y divide-border">
                  {milestones.map((m) => (
                    <div key={m.id} className="flex items-start justify-between gap-3 px-5 py-4">
                      <div className="flex items-start gap-3.5">
                        <button
                          type="button"
                          onClick={() => void toggleMilestone(m.id)}
                          className={`dash-plan-check mt-0.5 ${m.completed ? "on" : ""}`}
                          aria-label={`Toggle ${m.title}`}
                        >
                          <UiIcon name="check" />
                        </button>
                        <div>
                          <div className="text-[11px] font-semibold uppercase tracking-wide text-primary">{m.phase}</div>
                          <div
                            className={`mt-0.5 text-sm font-semibold ${
                              m.completed ? "line-through text-text-secondary" : "text-text"
                            }`}
                          >
                            {m.title}
                          </div>
                          <p className="m-0 mt-1 text-xs text-text-secondary">{m.detail}</p>
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={() => void deleteMilestone(m.id)}
                        className="cursor-pointer rounded-md border border-border bg-transparent p-1.5 text-text-muted transition hover:border-danger/50 hover:text-danger"
                        title="Remove milestone"
                      >
                        <UiIcon name="trash-2" />
                      </button>
                    </div>
                  ))}
                </div>
              )}

              <form onSubmit={(e) => void addMilestone(e)} className="border-t border-border bg-bg/50 p-5 text-sm">
                <div className="mb-3 text-xs font-semibold uppercase tracking-wide text-primary">
                  Add Custom Learning Milestone
                </div>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <input
                    type="text"
                    value={phase}
                    onChange={(e) => setPhase(e.target.value)}
                    placeholder="Phase"
                    className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                  <input
                    type="text"
                    required
                    value={title}
                    onChange={(e) => setTitle(e.target.value)}
                    placeholder="Milestone title (e.g., Deploy Dockerized API)"
                    className="rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary sm:col-span-2"
                  />
                </div>
                <div className="mt-3 flex flex-wrap gap-3">
                  <input
                    type="text"
                    value={detail}
                    onChange={(e) => setDetail(e.target.value)}
                    placeholder="Actionable goal or resource link (optional)"
                    className="min-w-[200px] flex-1 rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                  <button
                    type="submit"
                    disabled={adding}
                    className="btn-luminous inline-flex cursor-pointer items-center gap-2 rounded-control border-0 px-4 py-2 text-sm font-semibold disabled:opacity-60"
                  >
                    <UiIcon name="plus" />
                    {adding ? "Adding…" : "Add Milestone"}
                  </button>
                </div>
              </form>
            </Card>
          )}
        </>
      )}
    </div>
  );
}
