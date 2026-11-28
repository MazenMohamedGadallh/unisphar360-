import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { gradFetch, onboardingSkippedKey } from "../lib/grad";
import { Alert, Button } from "../components/ui";
import { Page, Panel, field, label, useAction, useGradData } from "../components/grad";
import { EntriesEditor, SkillsEditor, type Entry, type Skill } from "../components/CareerFacts";

interface Profile {
  userId: number; fullName: string; universityName: string | null; major: string; department: string;
  graduationYear: number | null; graduatedFrom: string | null;
  headline: string | null; languages: string | null; preferredIndustries: string | null; preferredLocations: string | null;
  employmentType: string; careerGoal: string | null; careerChallenges: string | null; gitHubUrl: string | null; portfolioUrl: string | null;
  skills: Skill[]; entries: Entry[];
}
interface Options {
  targetRole: string | null; experienceLevel: string | null; primaryStruggle: string | null; preferredWorkMode: string | null; preferredCity: string | null;
  roles: string[]; experienceLevels: string[]; struggles: string[]; workModes: string[];
}

const load = async () => ({
  profile: await gradFetch<Profile>("grad/profile"),
  options: await gradFetch<Options>("graduate/profile/onboarding"),
});

const STEPS = ["About you", "Skills", "Experience", "Preferences", "Goals and challenges"];
const STEP_KEY = "unisphere.onboarding.step";

export function GraduateOnboarding() {
  const navigate = useNavigate();
  const { data, error, reload } = useGradData(load);
  const [step, setStep] = useState(() => Math.min(Number(localStorage.getItem(STEP_KEY)) || 0, STEPS.length - 1));
  const [form, setForm] = useState<Record<string, string>>({});
  const { busy, message, run } = useAction();

  useEffect(() => localStorage.setItem(STEP_KEY, String(step)), [step]);

  const profile = data?.profile;
  const options = data?.options;
  const value = (key: string, saved: string | null | undefined) => form[key] ?? saved ?? "";
  const set = (key: string, next: string) => setForm((f) => ({ ...f, [key]: next }));

  const text = (title: string, key: keyof Profile, placeholder = "") => (
    <label className={label}>
      {title}
      <input className={`${field} mt-1`} placeholder={placeholder} value={value(key, profile?.[key] as string)} onChange={(e) => set(key, e.target.value)} />
    </label>
  );
  const select = (title: string, key: keyof Options, choices: string[]) => (
    <label className={label}>
      {title}
      <select className={`${field} mt-1`} value={value(key, options?.[key] as string) || choices[0]} onChange={(e) => set(key, e.target.value)}>
        {choices.map((c) => <option key={c}>{c}</option>)}
      </select>
    </label>
  );

  /** Only edited fields are sent; the API keeps everything else. */
  async function saveFields() {
    const profileKeys = ["headline", "languages", "preferredIndustries", "preferredLocations", "employmentType", "careerGoal", "careerChallenges", "gitHubUrl", "portfolioUrl"];
    const body = Object.fromEntries(Object.entries(form).filter(([k]) => profileKeys.includes(k)));
    if (Object.keys(body).length > 0) await gradFetch("grad/profile", { method: "PUT", body: JSON.stringify(body) });
  }

  async function next() {
    const ok = await run("save", async () => {
      await saveFields();
      if (step < STEPS.length - 1) return;
      await gradFetch("graduate/profile/onboarding", {
        method: "POST",
        body: JSON.stringify({
          targetRole: value("targetRole", options!.targetRole) || options!.roles[0],
          experienceLevel: value("experienceLevel", options!.experienceLevel) || options!.experienceLevels[0],
          primaryStruggle: value("primaryStruggle", options!.primaryStruggle) || options!.struggles[0],
          preferredWorkMode: value("preferredWorkMode", options!.preferredWorkMode) || options!.workModes[0],
          preferredCity: value("preferredCity", options!.preferredCity) || null,
        }),
      });
      localStorage.removeItem(STEP_KEY);
      navigate("/graduate/profile");
    });
    if (ok && step < STEPS.length - 1) setStep(step + 1);
  }

  async function skip() {
    await run("skip", saveFields);
    localStorage.setItem(onboardingSkippedKey(profile!.userId), "1");
    navigate("/graduate");
  }

  return (
    <Page title="Set up your career profile" description="Five short steps. Your answers are saved as you go, so you can stop and continue later."
      loading={!data} error={error} onRetry={() => void reload()}
      actions={profile && <Button variant="ghost" onClick={() => void skip()}>Skip for now</Button>}>
      {profile && options && (
        <div className="mx-auto max-w-3xl">
          <ol className="m-0 mb-5 flex list-none gap-2 p-0" aria-label="Progress">
            {STEPS.map((name, i) => (
              <li key={name} className="flex-1" aria-current={i === step ? "step" : undefined}>
                <button type="button" onClick={() => setStep(i)} className="w-full cursor-pointer border-0 bg-transparent p-0 text-start">
                  <span className={`block h-1.5 rounded-full ${i <= step ? "bg-primary" : "bg-border"}`} />
                  <span className={`mt-1.5 hidden text-xs sm:block ${i === step ? "font-semibold text-text" : "text-text-muted"}`}>{name}</span>
                </button>
              </li>
            ))}
          </ol>
          {message && <Alert kind={message.kind}>{message.text}</Alert>}
          <Panel title={STEPS[step]}>
            <div className="grid gap-4">
              {step === 0 && (
                <>
                  <p className="m-0 text-sm text-text-secondary">
                    From your university account: {[profile.fullName, profile.universityName ?? profile.graduatedFrom, profile.major || profile.department, profile.graduationYear].filter(Boolean).join(", ")}.
                  </p>
                  {text("Professional headline", "headline", "e.g. Junior backend developer")}
                  {text("Languages and proficiency", "languages", "Arabic (native), English (fluent)")}
                  {text("GitHub address", "gitHubUrl", "https://github.com/...")}
                  {text("Portfolio address", "portfolioUrl", "https://...")}
                </>
              )}
              {step === 1 && <SkillsEditor skills={profile.skills} onChanged={reload} />}
              {step === 2 && <EntriesEditor entries={profile.entries} onChanged={reload} />}
              {step === 3 && (
                <>
                  {text("Preferred industries", "preferredIndustries", "Fintech, Health")}
                  {text("Preferred locations", "preferredLocations", "Cairo, Remote")}
                  {text("Employment type", "employmentType", "Full-time, Internship")}
                  {text("Short-term career goal", "careerGoal", "What you want within a year")}
                </>
              )}
              {step === 4 && (
                <>
                  <div className="grid gap-4 sm:grid-cols-2">
                    {select("Target role", "targetRole", options.targetRole && !options.roles.includes(options.targetRole) ? [options.targetRole, ...options.roles] : options.roles)}
                    {select("Experience level", "experienceLevel", options.experienceLevels)}
                    {select("Biggest struggle", "primaryStruggle", options.struggles)}
                    {select("Work arrangement", "preferredWorkMode", options.workModes)}
                  </div>
                  <label className={label}>
                    Preferred city (optional)
                    <input className={`${field} mt-1`} maxLength={60} value={value("preferredCity", options.preferredCity)} onChange={(e) => set("preferredCity", e.target.value)} />
                  </label>
                  <label className={label}>
                    What do you think stands between you and a job? (optional)
                    <textarea className={`${field} mt-1`} rows={4} maxLength={1500}
                      placeholder="For example: technical interviews, writing my CV, no portfolio yet"
                      value={value("careerChallenges", profile.careerChallenges)} onChange={(e) => set("careerChallenges", e.target.value)} />
                  </label>
                </>
              )}
            </div>
            <div className="mt-6 flex justify-between gap-2">
              <Button variant="secondary" disabled={step === 0} onClick={() => setStep(step - 1)}>Back</Button>
              <Button disabled={busy !== null} onClick={() => void next()}>
                {busy ? "Saving..." : step === STEPS.length - 1 ? "Finish setup" : "Save and continue"}
              </Button>
            </div>
          </Panel>
        </div>
      )}
    </Page>
  );
}
