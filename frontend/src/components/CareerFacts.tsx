/* Skill and experience editors shared by onboarding and Career Profile. Every change saves immediately. */
import { useState, type FormEvent } from "react";
import { gradFetch } from "../lib/grad";
import { Alert, Button, ConfirmDialog } from "./ui";
import { field, useAction } from "./grad";

export interface Skill { id: number; name: string; level: string | null; source: string }
export interface Entry { id: number; kind: string; title: string; organization: string | null; description: string | null; period: string | null }

export function SkillsEditor({ skills, onChanged }: { skills: Skill[]; onChanged: () => Promise<void> }) {
  const [name, setName] = useState("");
  const [level, setLevel] = useState("");
  const { busy, message, run } = useAction();

  async function add(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    const ok = await run("add", async () => {
      await gradFetch("grad/profile/skills", { method: "POST", body: JSON.stringify({ name, level: level || null }) });
      await onChanged();
    });
    if (ok) setName("");
  }

  return (
    <div>
      {message && <Alert kind={message.kind}>{message.text}</Alert>}
      <form onSubmit={(e) => void add(e)} className="flex flex-wrap gap-2">
        <input className={`${field} min-w-48 flex-1`} aria-label="Skill, tool or technology" placeholder="Skill, tool or technology" maxLength={60} value={name} onChange={(e) => setName(e.target.value)} />
        <select className={`${field} w-auto`} aria-label="Your level" value={level} onChange={(e) => setLevel(e.target.value)}>
          <option value="">Level (optional)</option>
          {["Beginner", "Intermediate", "Advanced"].map((l) => <option key={l}>{l}</option>)}
        </select>
        <Button type="submit" disabled={busy !== null}>Add skill</Button>
      </form>
      <ul className="mt-3 mb-0 flex list-none flex-wrap gap-2 p-0">
        {skills.map((s) => (
          <li key={s.id} className="flex items-center gap-2 rounded-md border border-border bg-bg px-2.5 py-1 text-sm text-text">
            {s.name}{s.level ? <span className="text-text-muted">{s.level}</span> : null}
            <button type="button" aria-label={`Remove ${s.name}`} className="cursor-pointer border-0 bg-transparent p-0 text-text-muted hover:text-danger"
              onClick={() => void run("remove", async () => { await gradFetch(`grad/profile/skills/${s.id}`, { method: "DELETE" }); await onChanged(); })}>
              &times;
            </button>
          </li>
        ))}
      </ul>
      {skills.length === 0 && <p className="mt-2 mb-0 text-sm text-text-muted">No skills added yet.</p>}
    </div>
  );
}

const EMPTY = { kind: "Experience", title: "", organization: "", period: "", description: "" };

export function EntriesEditor({ entries, onChanged }: { entries: Entry[]; onChanged: () => Promise<void> }) {
  const [form, setForm] = useState(EMPTY);
  const [removing, setRemoving] = useState<Entry | null>(null);
  const { busy, message, run } = useAction();

  async function add(e: FormEvent) {
    e.preventDefault();
    const ok = await run("add", async () => {
      await gradFetch("grad/profile/entries", { method: "POST", body: JSON.stringify(form) });
      await onChanged();
    });
    if (ok) setForm(EMPTY);
  }

  const input = (key: keyof typeof EMPTY, placeholder: string, required = false) => (
    <input className={field} aria-label={placeholder} placeholder={placeholder} required={required} value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} />
  );

  return (
    <div>
      {message && <Alert kind={message.kind}>{message.text}</Alert>}
      <ul className="m-0 mb-3 list-none space-y-2 p-0">
        {entries.map((x) => (
          <li key={x.id} className="flex items-start justify-between gap-3 border-b border-border pb-2 text-sm">
            <span>
              <span className="font-medium text-text">{x.title}</span>
              <span className="block text-text-muted">{[x.kind, x.organization, x.period].filter(Boolean).join(", ")}</span>
              {x.description && <span className="block text-text-secondary">{x.description}</span>}
            </span>
            <Button size="sm" variant="ghost" onClick={() => setRemoving(x)}>Remove</Button>
          </li>
        ))}
      </ul>
      <form onSubmit={(e) => void add(e)} className="grid gap-2 sm:grid-cols-2">
        <select className={field} aria-label="Type" value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })}>
          {["Experience", "Project", "Certification", "Education"].map((k) => <option key={k}>{k}</option>)}
        </select>
        {input("title", "Title (internship, freelance, volunteer role...)", true)}
        {input("organization", "Organization")}
        {input("period", "Period, e.g. 2025 - 2026")}
        <textarea className={`${field} sm:col-span-2`} rows={2} aria-label="What you did" placeholder="What you did" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        <div><Button type="submit" variant="secondary" disabled={busy !== null}>Add entry</Button></div>
      </form>
      <ConfirmDialog open={removing !== null} title="Remove this entry?" confirmLabel="Remove" danger
        onCancel={() => setRemoving(null)}
        onConfirm={() => {
          const id = removing!.id;
          setRemoving(null);
          void run("remove", async () => { await gradFetch(`grad/profile/entries/${id}`, { method: "DELETE" }); await onChanged(); });
        }}>
        {removing?.title} will be removed from your profile.
      </ConfirmDialog>
    </div>
  );
}
