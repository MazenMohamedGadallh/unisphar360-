import { useEffect, useState, type ChangeEvent } from "react";
import { API_BASE_URL, authHeaders } from "../lib/api";
import { gradFetch } from "../lib/grad";
import { Alert, Button, ConfirmDialog } from "../components/ui";
import { Bullets, Page, Panel, Tag, field, fmtDate, label, useAction, useGradData } from "../components/grad";

interface Doc { id: number; fileName: string; sizeBytes: number; parseStatus: string; parseError: string | null; uploadedAtUtc: string; textChars: number }
interface Analysis {
  id: number; cvDocumentId: number; overallAssessment: string | null; scorePercent: number | null; aiScore: number | null;
  strengths: string[]; weaknesses: string[]; suggestions: string[]; keywords: string[];
  rewrites: { original: string; rewritten: string; why: string }[];
  technicalSkills: string[] | null; softSkills: string[] | null; sections: string[] | null; parsingRisks: string[] | null;
  scoreBreakdown: { criterion: string; points: number; max: number; reason: string }[] | null;
  jobFit: { fitPercent: number; evidenced: string[]; notEvidenced: string[] } | null;
  isDeterministicFallback: boolean; providerName: string; model: string; createdAtUtc: string;
}

const load = async () => {
  const [documents, history, profile] = await Promise.all([
    gradFetch<Doc[]>("grad/profile/cv"),
    gradFetch<{ fileName: string; analysis: Analysis }[]>("grad/profile/cv-history"),
    gradFetch<{ targetRole: string | null }>("grad/profile"),
  ]);
  return { documents, history, profile };
};

const MAX_BYTES = 10 * 1024 * 1024;

/** XMLHttpRequest rather than fetch, because only it reports real upload progress. */
function upload(file: File, onProgress: (percent: number) => void) {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", `${API_BASE_URL}/api/grad/profile/cv`);
    Object.entries(authHeaders()).forEach(([k, v]) => xhr.setRequestHeader(k, v));
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(Math.round((e.loaded / e.total) * 100));
    xhr.onload = () => {
      if (xhr.status < 300) return resolve();
      let message = `Upload failed (HTTP ${xhr.status}).`;
      try { message = JSON.parse(xhr.responseText).error ?? message; } catch { /* not JSON */ }
      reject(new Error(message));
    };
    xhr.onerror = () => reject(new Error("The upload did not reach the server. Check your connection and try again."));
    const form = new FormData();
    form.append("file", file);
    xhr.send(form);
  });
}

export function GraduateCvAnalysis() {
  const { data, error, reload } = useGradData(load);
  const { busy, message, run } = useAction();
  const [progress, setProgress] = useState<number | null>(null);
  const [seconds, setSeconds] = useState(0);
  const [docId, setDocId] = useState<number | null>(null);
  const [shownId, setShownId] = useState<number | null>(null);
  const [role, setRole] = useState<string | null>(null);
  const [jobDescription, setJobDescription] = useState("");
  const [manualText, setManualText] = useState("");
  const [confirmDelete, setConfirmDelete] = useState(false);

  const doc = data?.documents.find((d) => d.id === docId) ?? data?.documents[0];
  const history = data?.history ?? [];
  const shown = history.find((h) => h.analysis.id === shownId) ?? history.find((h) => h.analysis.cvDocumentId === doc?.id);
  const result = shown?.analysis;

  useEffect(() => {
    if (busy !== "analyze") return;
    setSeconds(0);
    const timer = setInterval(() => setSeconds((s) => s + 1), 1000);
    return () => clearInterval(timer);
  }, [busy]);

  async function choose(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;
    await run("upload", async () => {
      if (!/\.(pdf|docx|txt)$/i.test(file.name)) throw new Error("Choose a PDF, DOCX or TXT file.");
      if (file.size === 0 || file.size > MAX_BYTES) throw new Error("The file must be between 1 byte and 10 MB.");
      setProgress(0);
      try {
        await upload(file, setProgress);
        setDocId(null);
        setShownId(null);
        await reload();
      } finally {
        setProgress(null);
      }
    }, "CV uploaded. Select Analyze CV when you are ready.");
  }

  async function analyze() {
    await run("analyze", async () => {
      const analysis = await gradFetch<Analysis>(`grad/profile/cv/${doc!.id}/analyze`, {
        method: "POST",
        body: JSON.stringify({ targetRole: (role ?? data!.profile.targetRole) || null, jobDescription: jobDescription.trim() || null }),
      });
      await reload();
      setShownId(analysis.id);
    });
  }

  return (
    <Page title="CV Analysis" description="Upload your CV, then run the analysis. Nothing is analyzed until you ask."
      loading={!data} error={error} onRetry={() => void reload()}>
      {data && (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,360px)_minmax(0,1fr)] lg:items-start">
          <div className="grid gap-5">
            <Panel title="Your CV">
              {message && <Alert kind={message.kind}>{message.text}</Alert>}
              {doc ? (
                <div className="text-sm">
                  {data.documents.length > 1 ? (
                    <select className={field} aria-label="Uploaded files" value={doc.id} onChange={(e) => { setDocId(Number(e.target.value)); setShownId(null); }}>
                      {data.documents.map((d) => <option key={d.id} value={d.id}>{d.fileName} ({fmtDate(d.uploadedAtUtc)})</option>)}
                    </select>
                  ) : (
                    <p className="m-0 font-semibold break-all text-text">{doc.fileName}</p>
                  )}
                  <p className="my-2 text-text-muted">
                    Uploaded {fmtDate(doc.uploadedAtUtc)}, {Math.max(1, Math.round(doc.sizeBytes / 1024))} KB, {doc.textChars} characters read.
                  </p>
                  <Tag tone={doc.parseStatus === "Failed" ? "bad" : "good"}>{doc.parseStatus === "Failed" ? "Text not readable" : "Ready to analyze"}</Tag>
                </div>
              ) : (
                <p className="m-0 text-sm text-text-secondary">No CV uploaded yet. PDF or DOCX, up to 10 MB.</p>
              )}
              {progress !== null && (
                <div className="mt-3">
                  <progress className="w-full accent-primary" max={100} value={progress} aria-label="Upload progress" />
                  <p className="m-0 text-xs text-text-muted">Uploading {progress}%</p>
                </div>
              )}
              <div className="mt-4 flex flex-wrap gap-2">
                <label className="inline-flex cursor-pointer items-center rounded-control border border-border bg-bg px-4 py-2 text-sm font-medium text-text focus-within:outline-2 focus-within:outline-primary">
                  {doc ? "Upload another file" : "Choose file"}
                  <input type="file" accept=".pdf,.docx,.txt" className="sr-only" disabled={busy !== null} onChange={(e) => void choose(e)} />
                </label>
                {doc && <Button variant="ghost" disabled={busy !== null} onClick={() => setConfirmDelete(true)}>Delete</Button>}
              </div>
            </Panel>

            {doc && (
              <Panel title="Run the analysis">
                <label className={label}>Target role
                  <input className={`${field} mt-1`} value={role ?? data.profile.targetRole ?? ""} onChange={(e) => setRole(e.target.value)} />
                </label>
                <label className={`${label} mt-3`}>Job description (optional, adds a job-fit check)
                  <textarea className={`${field} mt-1`} rows={4} maxLength={8000} value={jobDescription} onChange={(e) => setJobDescription(e.target.value)} />
                </label>
                {doc.parseStatus === "Failed" && (
                  <div className="mt-3">
                    <Alert kind="warning">{doc.parseError ?? "The text could not be read."}</Alert>
                    <textarea className={field} rows={6} aria-label="Paste your CV text" placeholder="Paste your CV text here" value={manualText} onChange={(e) => setManualText(e.target.value)} />
                    <div className="mt-2">
                      <Button variant="secondary" size="sm" disabled={!manualText.trim() || busy !== null} onClick={() => void run("text", async () => {
                        await gradFetch(`grad/profile/cv/${doc.id}/manual-text`, { method: "PUT", body: JSON.stringify({ text: manualText }) });
                        await reload();
                      }, "Text saved.")}>Save text</Button>
                    </div>
                  </div>
                )}
                <div className="mt-4">
                  <Button disabled={busy !== null} onClick={() => void analyze()}>{busy === "analyze" ? "Analyzing..." : "Analyze CV"}</Button>
                </div>
                {busy === "analyze" && <p className="mb-0 text-sm text-text-secondary" role="status">Reading your CV and waiting for the AI provider ({seconds}s). This can take a minute or two.</p>}
              </Panel>
            )}

            <Panel title="History">
              {history.length === 0 ? (
                <p className="m-0 text-sm text-text-muted">Your past analyses will appear here.</p>
              ) : (
                <ul className="m-0 list-none space-y-1 p-0">
                  {history.map((h, i) => {
                    const previous = history.slice(i + 1).find((x) => !x.analysis.isDeterministicFallback);
                    const delta = previous && !h.analysis.isDeterministicFallback ? (h.analysis.scorePercent ?? 0) - (previous.analysis.scorePercent ?? 0) : null;
                    return (
                      <li key={h.analysis.id}>
                        <button type="button" aria-pressed={h.analysis.id === result?.id} onClick={() => setShownId(h.analysis.id)}
                          className={`flex w-full cursor-pointer justify-between gap-3 rounded-control border-0 px-3 py-2 text-start text-sm ${h.analysis.id === result?.id ? "bg-primary-light" : "bg-transparent hover:bg-bg"}`}>
                          <span className="min-w-0">
                            <span className="block truncate text-text">{h.fileName}</span>
                            <span className="text-text-muted">{fmtDate(h.analysis.createdAtUtc)}{h.analysis.isDeterministicFallback ? ", rules only" : ""}</span>
                          </span>
                          <span className="text-end font-semibold text-text tabular-nums">
                            {h.analysis.scorePercent}
                            {delta !== null && delta !== 0 && <span className={`block text-xs ${delta > 0 ? "text-success" : "text-danger"}`}>{delta > 0 ? "+" : ""}{delta}</span>}
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </Panel>
          </div>

          {result ? (
            <div className="grid gap-5">
              {result.isDeterministicFallback && (
                <Alert kind="warning">
                  The AI provider was unavailable, so this is a rule-based check only. It does not count towards your career journey. Select Analyze CV to try again.
                </Alert>
              )}
              <Panel title={`ATS result for ${shown!.fileName}`} aside={<span className="text-sm text-text-muted">{fmtDate(result.createdAtUtc)}, {result.providerName}</span>}>
                <div className="flex flex-wrap items-start gap-6">
                  <p className="m-0 text-5xl font-bold text-primary tabular-nums">{result.scorePercent}<span className="text-xl text-text-muted">/100</span></p>
                  <p className="m-0 max-w-[60ch] flex-1 text-text">{result.overallAssessment}</p>
                </div>
                <p className="text-sm text-text-secondary">
                  {result.aiScore !== null
                    ? `The score is the average of the AI's judgement (${result.aiScore}) and the rule checks below (${result.scoreBreakdown?.reduce((n, c) => n + c.points, 0)}).`
                    : "The score comes from the rule checks below."} It is an estimate, not an employer's system.
                </p>
                <table className="w-full text-sm">
                  <thead><tr className="text-start text-text-muted"><th className="py-1 text-start font-medium">Check</th><th className="text-start font-medium">Points</th><th className="text-start font-medium">Why</th></tr></thead>
                  <tbody>
                    {result.scoreBreakdown?.map((c) => (
                      <tr key={c.criterion} className="border-t border-border">
                        <td className="py-1.5 pr-3 text-text">{c.criterion}</td>
                        <td className="pr-3 tabular-nums">{c.points}/{c.max}</td>
                        <td className="text-text-secondary">{c.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </Panel>
              {result.jobFit && (
                <Panel title={`Fit with the job description: ${result.jobFit.fitPercent}%`}>
                  <p className="m-0 mb-2 text-sm text-text-secondary">Skills named in the description, checked against your CV text. This is separate from the ATS score.</p>
                  <div className="flex flex-wrap gap-1.5">
                    {result.jobFit.evidenced.map((s) => <Tag key={s} tone="good">{s}: in your CV</Tag>)}
                    {result.jobFit.notEvidenced.map((s) => <Tag key={s} tone="warn">{s}: not evidenced</Tag>)}
                  </div>
                </Panel>
              )}
              <div className="grid gap-5 md:grid-cols-2">
                <Panel title="Strengths"><Bullets items={result.strengths} /></Panel>
                <Panel title="Weaknesses"><Bullets items={result.weaknesses} /></Panel>
                <Panel title="Recommended changes"><Bullets items={result.suggestions} /></Panel>
                <Panel title="Parsing and formatting risks"><Bullets items={result.parsingRisks ?? []} empty="No risks detected." /></Panel>
              </div>
              <Panel title="What was found">
                <div className="grid gap-3 text-sm">
                  {([["Sections", result.sections], ["Technical skills", result.technicalSkills], ["Soft skills", result.softSkills], ["Keywords to consider", result.keywords]] as const).map(([name, items]) => (
                    <div key={name}>
                      <p className="m-0 mb-1 font-medium text-text">{name}</p>
                      <div className="flex flex-wrap gap-1.5">{items?.length ? items.map((s) => <Tag key={s}>{s}</Tag>) : <span className="text-text-muted">None detected</span>}</div>
                    </div>
                  ))}
                </div>
              </Panel>
              {result.rewrites.length > 0 && (
                <Panel title="Suggested rewrites">
                  <p className="m-0 mb-3 text-sm text-text-secondary">Check every rewrite is true before you use it.</p>
                  <ul className="m-0 list-none space-y-3 p-0 text-sm">
                    {result.rewrites.map((r) => (
                      <li key={r.original} className="border-t border-border pt-3">
                        <p className="m-0 text-text-muted line-through">{r.original}</p>
                        <p className="m-0 text-text">{r.rewritten}</p>
                        <p className="m-0 text-text-secondary">{r.why}</p>
                      </li>
                    ))}
                  </ul>
                </Panel>
              )}
            </div>
          ) : (
            <Panel><p className="m-0 text-sm text-text-secondary">{doc ? "Select Analyze CV to see your score, evidence and recommendations." : "Upload a CV to begin."}</p></Panel>
          )}
        </div>
      )}
      <ConfirmDialog open={confirmDelete} title="Delete this CV?" confirmLabel="Delete CV" danger
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => {
          setConfirmDelete(false);
          void run("delete", async () => {
            await gradFetch(`grad/profile/cv/${doc!.id}`, { method: "DELETE" });
            setDocId(null);
            setShownId(null);
            await reload();
          }, "CV deleted.");
        }}>
        {doc?.fileName}, its extracted text and all of its analyses will be permanently removed.
      </ConfirmDialog>
    </Page>
  );
}
