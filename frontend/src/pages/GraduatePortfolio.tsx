import { useState, type FormEvent } from "react";
import { gradFetch } from "../lib/grad";
import { Alert, Button, ConfirmDialog } from "../components/ui";
import { Bullets, Page, Panel, Tag, field, label, useAction, useGradData } from "../components/grad";

interface Project {
  id: number; title: string; description: string; problem: string | null; technologies: string | null; role: string | null;
  outcomes: string | null; skills: string | null; gitHubUrl: string | null; demoUrl: string | null; complete: boolean;
  checks: { name: string; ok: boolean; tip: string }[];
}

const load = () => gradFetch<Project[]>("graduate/portfolio");

const FIELDS = [
  ["title", "Title", 1], ["description", "Description", 4], ["problem", "Problem solved", 2], ["role", "Your role and contribution", 2],
  ["technologies", "Technologies (comma separated)", 1], ["skills", "Skills demonstrated (comma separated)", 1],
  ["outcomes", "Outcomes", 2], ["gitHubUrl", "GitHub address", 1], ["demoUrl", "Demo address", 1],
] as const;

type Form = Record<string, string>;

export function GraduatePortfolio() {
  const { data, error, reload } = useGradData(load);
  const [editing, setEditing] = useState<number | "new" | null>(null);
  const [form, setForm] = useState<Form>({});
  const [preview, setPreview] = useState(false);
  const [removing, setRemoving] = useState<Project | null>(null);
  const [advice, setAdvice] = useState<{ id: number; suggestedDescription: string; missing: string[] } | null>(null);
  const { busy, message, run } = useAction();

  function edit(project: Project | null) {
    setForm(project ? Object.fromEntries(FIELDS.map(([k]) => [k, project[k] ?? ""])) : {});
    setEditing(project?.id ?? "new");
    setPreview(false);
  }

  async function save(e: FormEvent) {
    e.preventDefault();
    const ok = await run("save", async () => {
      await gradFetch(editing === "new" ? "graduate/portfolio" : `graduate/portfolio/${editing}`, {
        method: editing === "new" ? "POST" : "PUT",
        body: JSON.stringify(form),
      });
      await reload();
    }, "Project saved.");
    if (ok) setEditing(null);
  }

  const link = (url: string | null, text: string) =>
    url ? <a href={url} target="_blank" rel="noreferrer noopener" className="text-primary">{text}</a> : null;

  return (
    <Page title="Portfolio Builder" description="Document your projects and your own part in them. Your portfolio is private: only you can see it."
      loading={!data} error={error} onRetry={() => void reload()}
      actions={
        <>
          <Button variant="secondary" disabled={!data?.length} onClick={() => { setPreview(!preview); setEditing(null); }}>{preview ? "Back to editing" : "Preview"}</Button>
          <Button onClick={() => edit(null)}>Add project</Button>
        </>
      }>
      {data && (
        <div className="grid gap-5">
          {message && <Alert kind={message.kind}>{message.text}</Alert>}

          {preview ? (
            <Panel>
              {data.map((p) => (
                <article key={p.id} className="border-b border-border py-6 first:pt-0 last:border-0">
                  <h2 className="m-0 text-2xl font-bold text-text">{p.title}</h2>
                  <p className="max-w-[70ch] whitespace-pre-wrap text-text">{p.description}</p>
                  {([["Problem", p.problem], ["My contribution", p.role], ["Outcomes", p.outcomes], ["Built with", p.technologies], ["Skills", p.skills]] as const)
                    .filter(([, v]) => v).map(([name, v]) => (
                      <p key={name} className="max-w-[70ch] text-sm text-text-secondary"><strong className="text-text">{name}. </strong>{v}</p>
                    ))}
                  <p className="m-0 flex gap-4 text-sm">{link(p.gitHubUrl, "Source code")}{link(p.demoUrl, "Live demo")}</p>
                </article>
              ))}
            </Panel>
          ) : editing !== null ? (
            <Panel title={editing === "new" ? "New project" : "Edit project"}>
              <form onSubmit={(e) => void save(e)} className="grid gap-3">
                {FIELDS.map(([key, title, rows]) => (
                  <label key={key} className={label}>{title}
                    {rows === 1 ? (
                      <input className={`${field} mt-1`} required={key === "title"} value={form[key] ?? ""} onChange={(e) => setForm({ ...form, [key]: e.target.value })} />
                    ) : (
                      <textarea className={`${field} mt-1`} rows={rows} required={key === "description"} value={form[key] ?? ""} onChange={(e) => setForm({ ...form, [key]: e.target.value })} />
                    )}
                  </label>
                ))}
                <div className="flex gap-2">
                  <Button type="submit" disabled={busy !== null}>Save project</Button>
                  <Button variant="secondary" onClick={() => setEditing(null)}>Cancel</Button>
                </div>
              </form>
            </Panel>
          ) : data.length === 0 ? (
            <Panel><p className="m-0 text-sm text-text-secondary">No projects yet. Add one you built at university, in a job, or on your own.</p></Panel>
          ) : (
            data.map((p) => (
              <Panel key={p.id} title={p.title} aside={<Tag tone={p.complete ? "good" : "warn"}>{p.complete ? "Complete" : "Needs work"}</Tag>}>
                <p className="mt-0 max-w-[75ch] text-sm text-text-secondary">{p.description}</p>
                <ul className="m-0 flex list-none flex-wrap gap-1.5 p-0">
                  {p.checks.map((c) => <li key={c.name}><Tag tone={c.ok ? "good" : "plain"}>{c.ok ? "Done" : "Missing"}: {c.name}</Tag></li>)}
                </ul>
                {p.checks.some((c) => !c.ok) && <div className="mt-3"><Bullets items={p.checks.filter((c) => !c.ok).map((c) => c.tip)} /></div>}
                <div className="mt-4 flex flex-wrap gap-2">
                  <Button size="sm" variant="secondary" onClick={() => edit(p)}>Edit</Button>
                  <Button size="sm" variant="secondary" disabled={busy !== null} onClick={() => void run(`improve-${p.id}`, async () => {
                    setAdvice({ id: p.id, ...(await gradFetch<{ suggestedDescription: string; missing: string[] }>(`graduate/portfolio/${p.id}/improve`, { method: "POST" })) });
                  })}>
                    {busy === `improve-${p.id}` ? "Reviewing..." : "Suggest better wording"}
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => setRemoving(p)}>Delete</Button>
                </div>
                {advice?.id === p.id && (
                  <div className="mt-4 rounded-control bg-primary-light p-4 text-sm">
                    <p className="mt-0 text-text">{advice.suggestedDescription}</p>
                    <p className="mb-1 font-medium text-text">Only you can add these:</p>
                    <Bullets items={advice.missing} />
                    <p className="text-text-muted">Check that every statement is true before using it.</p>
                    <Button size="sm" onClick={() => { edit(p); setForm((f) => ({ ...f, description: advice.suggestedDescription })); setAdvice(null); }}>
                      Edit with this wording
                    </Button>
                  </div>
                )}
              </Panel>
            ))
          )}
        </div>
      )}
      <ConfirmDialog open={removing !== null} title="Delete this project?" confirmLabel="Delete project" danger
        onCancel={() => setRemoving(null)}
        onConfirm={() => {
          const id = removing!.id;
          setRemoving(null);
          void run("delete", async () => { await gradFetch(`graduate/portfolio/${id}`, { method: "DELETE" }); await reload(); }, "Project deleted.");
        }}>
        {removing?.title} will be permanently removed from your portfolio.
      </ConfirmDialog>
    </Page>
  );
}
