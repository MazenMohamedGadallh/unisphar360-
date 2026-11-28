import { useEffect, useState } from "react";
import { gradFetch } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import { Alert, Button } from "./ui";
import { GradModal, gradInput } from "./GradModal";

interface Onboarding {
  hasCompletedOnboarding: boolean;
  targetRole: string | null;
  experienceLevel: string | null;
  primaryStruggle: string | null;
  preferredWorkMode: string | null;
  preferredCity: string | null;
  roles: string[];
  experienceLevels: string[];
  struggles: string[];
  workModes: string[];
}

/** "Recalibrate" button plus the 2-step wizard; with `autoOpen` it opens by itself until completed once. */
export function CareerCalibration({ onSaved, autoOpen = false }: { onSaved: () => void; autoOpen?: boolean }) {
  const [options, setOptions] = useState<Onboarding | null>(null);
  const [open, setOpen] = useState(false);
  const [step, setStep] = useState(1);
  const [form, setForm] = useState({ targetRole: "", experienceLevel: "", primaryStruggle: "", preferredWorkMode: "", preferredCity: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function start(from: Onboarding) {
    setForm({
      targetRole: from.targetRole ?? from.roles[0],
      experienceLevel: from.experienceLevels.includes(from.experienceLevel ?? "") ? from.experienceLevel! : from.experienceLevels[0],
      primaryStruggle: from.primaryStruggle ?? from.struggles[0],
      preferredWorkMode: from.preferredWorkMode ?? from.workModes[0],
      preferredCity: from.preferredCity ?? "",
    });
    setStep(1);
    setError(null);
    setOpen(true);
  }

  useEffect(() => {
    gradFetch<Onboarding>("graduate/profile/onboarding")
      .then((o) => {
        setOptions(o);
        if (autoOpen && !o.hasCompletedOnboarding) start(o);
      })
      .catch(() => {
        // The page's own load reports API failures; the wizard just stays unavailable.
      });
  }, []);

  async function save() {
    setSaving(true);
    setError(null);
    try {
      setOptions(await gradFetch<Onboarding>("graduate/profile/onboarding", { method: "POST", body: JSON.stringify(form) }));
      setOpen(false);
      onSaved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  const select = (label: string, key: keyof typeof form, values: string[]) => (
    <label className="block text-xs font-medium text-text-secondary">
      {label}
      <select className={`${gradInput} mt-1`} value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })}>
        {values.map((v) => (
          <option key={v} value={v}>{v}</option>
        ))}
      </select>
    </label>
  );

  if (!options) return null;
  return (
    <>
      <Button size="sm" variant="secondary" onClick={() => start(options)}>
        <UiIcon name="target" />
        {options.hasCompletedOnboarding ? "Recalibrate" : "Calibrate career"}
      </Button>
      {open && (
        <GradModal title={`Career calibration · Step ${step} of 2`} onClose={() => setOpen(false)}>
          <div className="flex flex-col gap-4">
            {error && <Alert kind="error">{error}</Alert>}
            {step === 1 ? (
              <>
                <p className="m-0 text-text-secondary">What are you aiming for? Your answers tune job matching, your quest and interview practice.</p>
                {select("Target role", "targetRole", Array.from(new Set([...options.roles, form.targetRole])))}
                {select("Experience level", "experienceLevel", options.experienceLevels)}
                {select("Where do you struggle most?", "primaryStruggle", options.struggles)}
              </>
            ) : (
              <>
                <p className="m-0 text-text-secondary">Where do you want to work?</p>
                {select("Preferred work mode", "preferredWorkMode", options.workModes)}
                <label className="block text-xs font-medium text-text-secondary">
                  Preferred city (optional)
                  <input
                    className={`${gradInput} mt-1`}
                    maxLength={60}
                    value={form.preferredCity}
                    onChange={(e) => setForm({ ...form, preferredCity: e.target.value })}
                  />
                </label>
              </>
            )}
            <div className="flex justify-between gap-2 border-t border-border pt-4">
              <Button variant="secondary" onClick={() => (step === 1 ? setOpen(false) : setStep(1))}>
                {step === 1 ? "Skip for now" : "Back"}
              </Button>
              {step === 1 ? (
                <Button onClick={() => setStep(2)}>Next</Button>
              ) : (
                <Button disabled={saving} onClick={() => void save()}>{saving ? "Saving…" : "Finish calibration"}</Button>
              )}
            </div>
          </div>
        </GradModal>
      )}
    </>
  );
}
