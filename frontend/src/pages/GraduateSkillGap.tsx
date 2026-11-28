import { useCallback, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { gradFetch } from "../lib/grad";
import { Alert, Button } from "../components/ui";
import { Page, Panel, Tag, field, label, useAction, useGradData } from "../components/grad";

interface Row { skill: string; evidence: string; level: string | null; jobDemand: number; priority: string; reason: string; action: string; verified: boolean }
interface Gap { target: string; evidencedPercent: number; skills: Row[]; roles: string[] }
interface PlanItem { id: number; title: string; category: string | null; status: string; notes: string | null }

export function GraduateSkillGap() {
  const [params, setParams] = useSearchParams();
  const jobId = Number(params.get("jobId")) || null;
  const [query, setQuery] = useState<{ role?: string; jobDescription?: string }>({});
  const [description, setDescription] = useState("");
  const [notes, setNotes] = useState<Record<number, string>>({});
  const { busy, message, run } = useAction();

  const load = useCallback(async () => {
    const [gap, plan] = await Promise.all([
      gradFetch<Gap>("graduate/skill-gap", { method: "POST", body: JSON.stringify(jobId ? { jobId } : query) }),
      gradFetch<PlanItem[]>("grad/roadmap"),
    ]);
    return { gap, plan: plan.filter((p) => p.category === "Skill") };
  }, [jobId, query]);
  const { data, error, reload } = useGradData(load);

  function compare(next: typeof query) {
    setParams({});
    setQuery(next);
  }

  return (
    <Page title="Skill Gap Analysis" description="What a role asks for, set against what you can show. A skill missing from your CV is only not evidenced, not proven absent."
      loading={!data && !error} error={error} onRetry={() => { setDescription(""); compare({}); void reload(); }}
      actions={<Button variant="secondary" to="/graduate/profile">Update my skills</Button>}>
      {data && (
        <div className="grid gap-5">
          <Panel title="Compare against">
            <div className="grid gap-3 md:grid-cols-[240px_minmax(0,1fr)]">
              <label className={label}>A target role
                <select className={`${field} mt-1`} value={query.role ?? ""} onChange={(e) => compare({ role: e.target.value || undefined })}>
                  <option value="">My target role</option>
                  {data.gap.roles.map((r) => <option key={r}>{r}</option>)}
                </select>
              </label>
              <label className={label}>Or a real job description
                <textarea className={`${field} mt-1`} rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
              </label>
            </div>
            <div className="mt-3">
              <Button size="sm" variant="secondary" disabled={!description.trim()} onClick={() => compare({ jobDescription: description })}>Compare description</Button>
            </div>
          </Panel>

          {message && <Alert kind={message.kind}>{message.text}</Alert>}

          <Panel title={`${data.gap.target}: ${data.gap.evidencedPercent}% of required skills evidenced`}>
            <ul className="m-0 list-none p-0">
              {data.gap.skills.map((s) => (
                <li key={s.skill} className="grid gap-2 border-t border-border py-3 md:grid-cols-[200px_minmax(0,1fr)_auto] md:items-center">
                  <div>
                    <p className="m-0 font-medium text-text">{s.skill}</p>
                    <Tag tone={s.verified ? "good" : s.evidence === "Not evidenced" ? "warn" : "info"}>{s.evidence}</Tag>
                    {s.level && <span className="ml-2 text-xs text-text-muted">you say: {s.level}</span>}
                  </div>
                  <div className="text-sm">
                    <p className="m-0 text-text-secondary">{s.priority === "Done" ? "" : `${s.priority} priority. `}{s.reason}</p>
                    <p className="m-0 text-text">{s.action}</p>
                  </div>
                  {!s.verified && (
                    <div className="flex gap-2">
                      <Button size="sm" variant="secondary" disabled={busy !== null || data.plan.some((p) => p.title === `Learn ${s.skill}`)}
                        onClick={() => void run("plan", async () => {
                          await gradFetch("grad/roadmap", { method: "POST", body: JSON.stringify({ title: `Learn ${s.skill}`, category: "Skill" }) });
                          await reload();
                        }, `${s.skill} added to your plan.`)}>
                        {data.plan.some((p) => p.title === `Learn ${s.skill}`) ? "On your plan" : "Add to plan"}
                      </Button>
                      <Button size="sm" to={`/graduate/skill-lab?skill=${encodeURIComponent(s.skill)}`}>Prove it</Button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          </Panel>

          <Panel title="Learning plan">
            {data.plan.length === 0 && <p className="m-0 text-sm text-text-muted">Add a skill above to plan your learning.</p>}
            <ul className="m-0 list-none p-0">
              {data.plan.map((p) => (
                <li key={p.id} className="border-t border-border py-3 text-sm">
                  <div className="flex items-center justify-between gap-3">
                    <span className="font-medium text-text">{p.title}</span>
                    {p.status === "Completed" && <Tag tone="good">Completed</Tag>}
                  </div>
                  {p.status === "Completed" ? (
                    <p className="m-0 text-text-secondary">{p.notes}</p>
                  ) : (
                    <div className="mt-2 flex flex-wrap gap-2">
                      <input className={`${field} min-w-60 flex-1`} aria-label={`What you did to learn: ${p.title}`} placeholder="What did you study or build? (30 characters or more)"
                        value={notes[p.id] ?? p.notes ?? ""} onChange={(e) => setNotes({ ...notes, [p.id]: e.target.value })} />
                      <Button size="sm" disabled={busy !== null || (notes[p.id] ?? p.notes ?? "").trim().length < 30}
                        onClick={() => void run("complete", async () => {
                          await gradFetch(`grad/roadmap/${p.id}`, { method: "PUT", body: JSON.stringify({ status: "Completed", notes: notes[p.id] ?? p.notes }) });
                          await reload();
                        }, "Marked as completed.")}>
                        Mark completed
                      </Button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          </Panel>
        </div>
      )}
    </Page>
  );
}
