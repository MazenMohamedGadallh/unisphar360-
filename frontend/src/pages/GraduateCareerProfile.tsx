import { useState, type FormEvent } from "react";
import { gradFetch } from "../lib/grad";
import { Alert, Button } from "../components/ui";
import { Bullets, Meter, Page, Panel, Tag, field, fmtDate, label, useAction, useGradData } from "../components/grad";
import { EntriesEditor, SkillsEditor, type Entry, type Skill } from "../components/CareerFacts";

interface Finding { Point: string; Evidence: string }
interface Dna {
  report: null | {
    GeneratedAtUtc: string; ProviderName: string; Model: string;
    Data: { Summary: string; ExperienceLevel: string; ExperienceReason: string; Strengths: Finding[]; Gaps: Finding[]; Roles: Finding[]; Priorities: string[]; InferredSkills: Finding[] };
  };
  stale: boolean;
  reportedSkills: { name: string; level: string | null }[];
  cvSkills: string[];
  verifiedSkills: string[];
  readiness: { name: string; percent: number | null; explanation: string }[];
}

/** The API serialises records in camelCase; the stored report keeps the same shape. */
function pascal(value: unknown): any {
  if (Array.isArray(value)) return value.map(pascal);
  if (value && typeof value === "object")
    return Object.fromEntries(Object.entries(value).map(([k, v]) => [k[0].toUpperCase() + k.slice(1), pascal(v)]));
  return value;
}

const load = async () => {
  const [profile, dna] = await Promise.all([
    gradFetch<Record<string, any>>("grad/profile"),
    gradFetch<Dna>("graduate/dna"),
  ]);
  return { profile, dna: { ...dna, report: dna.report ? (pascal(dna.report) as Dna["report"]) : null } };
};

const FIELDS: [string, string][] = [
  ["headline", "Headline"], ["languages", "Languages"], ["preferredJobTitles", "Target job titles"], ["preferredIndustries", "Industries"],
  ["preferredLocations", "Locations"], ["careerGoal", "Career goal"], ["linkedInUrl", "LinkedIn address"], ["gitHubUrl", "GitHub address"], ["portfolioUrl", "Portfolio address"],
];

export function GraduateCareerProfile() {
  const { data, error, reload } = useGradData(load);
  const [form, setForm] = useState<Record<string, string>>({});
  const saving = useAction();
  const assessing = useAction();
  const profile = data?.profile;
  const dna = data?.dna;
  const report = dna?.report?.Data;
  const value = (key: string) => form[key] ?? profile?.[key] ?? "";

  async function save(e: FormEvent) {
    e.preventDefault();
    const ok = await saving.run("save", async () => {
      await gradFetch("grad/profile", { method: "PUT", body: JSON.stringify(form) });
      await reload();
    }, "Profile saved.");
    if (ok) setForm({});
  }

  const findings = (items: Finding[]) =>
    items.length === 0 ? <p className="m-0 text-sm text-text-muted">Not enough evidence to say.</p> : (
      <ul className="m-0 list-none space-y-2 p-0 text-sm">
        {items.map((f) => (
          <li key={f.Point}><span className="font-medium text-text">{f.Point}</span><span className="block text-text-muted">{f.Evidence}</span></li>
        ))}
      </ul>
    );

  const skillRow = (name: string, items: string[]) => (
    <div>
      <p className="m-0 mb-1.5 text-sm font-medium text-text">{name}</p>
      <div className="flex flex-wrap gap-1.5">
        {items.length === 0 ? <span className="text-sm text-text-muted">None yet</span> : items.map((s) => <Tag key={s}>{s}</Tag>)}
      </div>
    </div>
  );

  return (
    <Page title="Career Profile" description="Your facts, and the Career DNA assessment built from them."
      loading={!data} error={error} onRetry={() => void reload()}
      actions={<Button variant="secondary" to="/graduate/onboarding">Review setup steps</Button>}>
      {profile && dna && (
        <div className="grid gap-5">
          <Panel title="Career DNA" aside={
            <Button disabled={assessing.busy !== null} onClick={() => void assessing.run("dna", async () => {
              await gradFetch("graduate/dna", { method: "POST" });
              await reload();
            })}>
              {assessing.busy ? "Assessing..." : dna.report ? "Refresh assessment" : "Generate assessment"}
            </Button>
          }>
            {assessing.message && <Alert kind={assessing.message.kind}>{assessing.message.text}</Alert>}
            {dna.stale && <Alert kind="info">Your profile or evidence changed since this assessment. Refresh to include it.</Alert>}
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {skillRow("You reported", dna.reportedSkills.map((s) => (s.level ? `${s.name} (${s.level})` : s.name)))}
              {skillRow("Found in your CV", dna.cvSkills)}
              {skillRow("Demonstrated in challenges", dna.verifiedSkills)}
              {skillRow("Inferred by AI, unconfirmed", report?.InferredSkills.map((f) => f.Point) ?? [])}
            </div>
            <div className="mt-5 grid gap-4 sm:grid-cols-2">
              {dna.readiness.map((d) => (
                <div key={d.name}><Meter name={d.name} value={d.percent} /><p className="m-0 mt-1 text-xs text-text-muted">{d.explanation}</p></div>
              ))}
            </div>
            {report ? (
              <div className="mt-6 border-t border-border pt-5">
                <p className="m-0 max-w-[75ch] text-text">{report.Summary}</p>
                <p className="text-sm text-text-secondary">Experience level: {report.ExperienceLevel}. {report.ExperienceReason}</p>
                <div className="grid gap-5 md:grid-cols-3">
                  <div><h3 className="text-sm font-semibold">Strengths</h3>{findings(report.Strengths)}</div>
                  <div><h3 className="text-sm font-semibold">Gaps</h3>{findings(report.Gaps)}</div>
                  <div><h3 className="text-sm font-semibold">Roles to consider</h3>{findings(report.Roles)}</div>
                </div>
                <h3 className="text-sm font-semibold">Development priorities</h3>
                <Bullets items={report.Priorities} />
                <p className="mb-0 text-xs text-text-muted">AI estimate from {dna.report!.ProviderName} ({dna.report!.Model}), {fmtDate(dna.report!.GeneratedAtUtc)}.</p>
              </div>
            ) : (
              <p className="mb-0 text-sm text-text-secondary">No assessment yet. Add your skills and CV first, then generate it.</p>
            )}
          </Panel>

          <Panel title="Details">
            {saving.message && <Alert kind={saving.message.kind}>{saving.message.text}</Alert>}
            <form onSubmit={(e) => void save(e)} className="grid gap-3 sm:grid-cols-2">
              {FIELDS.map(([key, title]) => (
                <label key={key} className={label}>{title}
                  <input className={`${field} mt-1`} value={value(key)} onChange={(e) => setForm({ ...form, [key]: e.target.value })} />
                </label>
              ))}
              {[["careerSummary", "Summary"], ["careerChallenges", "Current challenges"]].map(([key, title]) => (
                <label key={key} className={`${label} sm:col-span-2`}>{title}
                  <textarea className={`${field} mt-1`} rows={3} value={value(key)} onChange={(e) => setForm({ ...form, [key]: e.target.value })} />
                </label>
              ))}
              <div><Button type="submit" disabled={saving.busy !== null || Object.keys(form).length === 0}>Save profile</Button></div>
            </form>
          </Panel>

          <div className="grid gap-5 lg:grid-cols-2">
            <Panel title="Skills and tools"><SkillsEditor skills={profile.skills as Skill[]} onChanged={reload} /></Panel>
            <Panel title="Experience, certifications and education"><EntriesEditor entries={profile.entries as Entry[]} onChanged={reload} /></Panel>
          </div>
        </div>
      )}
    </Page>
  );
}
