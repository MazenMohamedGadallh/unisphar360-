import { useEffect, useRef, useState } from "react";
import { gradFetch } from "../lib/grad";
import { Alert, Button, LoadingBlock } from "./ui";
import { GradModal, gradInput } from "./GradModal";

export interface JobRef {
  id: number;
  title: string;
  company: string;
}

interface PrepQuestion {
  id: number;
  questionText: string;
  feedback: string | null;
  feedbackScore: number | null;
  improvements: string[];
}

interface JobPrep {
  missingKeywords: string[];
  bulletImprovements: { keyword: string; original: string | null; improved: string }[];
  interview: { id: number; questions: PrepQuestion[] };
}

function PrepQuestionCard({ sessionId, number, initial }: { sessionId: number; number: number; initial: PrepQuestion }) {
  const [question, setQuestion] = useState(initial);
  const [answer, setAnswer] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function coach() {
    setBusy(true);
    setError(null);
    try {
      setQuestion(
        await gradFetch<PrepQuestion>(`grad/interview/sessions/${sessionId}/questions/${question.id}/answer`, {
          method: "POST",
          body: JSON.stringify({ answerText: answer }),
        }),
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="rounded-control border border-border bg-bg/70 p-4">
      <p className="m-0 mb-2 font-medium text-text">
        {number}. {question.questionText}
      </p>
      <textarea
        rows={3}
        aria-label={`Answer to question ${number}`}
        className={gradInput}
        value={answer}
        onChange={(e) => setAnswer(e.target.value)}
        placeholder="Practise your answer…"
      />
      <div className="mt-2">
        <Button size="sm" disabled={busy || answer.trim().length < 10} onClick={() => void coach()}>
          {busy ? "Coaching…" : "Coach my answer"}
        </Button>
      </div>
      {error && <p className="m-0 mt-2 text-xs text-danger">{error}</p>}
      {question.feedback && (
        <div className="mt-3 text-xs text-text-secondary">
          <strong className="text-primary">{question.feedbackScore}/10</strong> {question.feedback}
          {question.improvements.length > 0 && (
            <ul className="m-0 mt-1 list-disc pl-4">
              {question.improvements.map((i) => <li key={i}>{i}</li>)}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

export function PrepareJobDrawer({ job, onClose }: { job: JobRef; onClose: () => void }) {
  const [prep, setPrep] = useState<JobPrep | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<Record<string, boolean>>({});
  const requested = useRef(false);

  function generate() {
    setError(null);
    gradFetch<JobPrep>(`graduate/jobs/${job.id}/prepare`, { method: "POST" })
      .then(setPrep)
      .catch((e: Error) => setError(e.message));
  }

  useEffect(() => {
    // The request creates a practice session, so StrictMode's second effect run must not repeat it.
    if (requested.current) return;
    requested.current = true;
    generate();
  }, []);

  return (
    <GradModal drawer title={`Prepare for ${job.title} · ${job.company}`} onClose={onClose}>
      {error ? (
        <>
          <Alert kind="error">{error}</Alert>
          <div className="flex gap-2">
            <Button size="sm" onClick={generate}>Try again</Button>
            <Button size="sm" variant="secondary" to="/graduate/cv">Open CV Studio</Button>
          </div>
        </>
      ) : !prep ? (
        <LoadingBlock label="Comparing your CV with this job…" />
      ) : (
        <div className="flex flex-col gap-5">
          <section>
            <h3 className="m-0 mb-2 text-sm font-semibold text-text">ATS keyword gaps</h3>
            {prep.missingKeywords.length === 0 ? (
              <p className="m-0 text-text-secondary">No missing keywords found for this job.</p>
            ) : (
              <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
                {prep.missingKeywords.map((k) => (
                  <li key={k}>
                    <label className="flex cursor-pointer items-center gap-2">
                      <input type="checkbox" className="accent-primary" checked={!!done[k]} onChange={(e) => setDone({ ...done, [k]: e.target.checked })} />
                      <span className={done[k] ? "text-text-secondary line-through" : "text-text"}>{k}</span>
                    </label>
                  </li>
                ))}
              </ul>
            )}
            <p className="m-0 mt-2 text-xs text-text-muted">Only add a keyword to your CV if you can back it up.</p>
          </section>

          {prep.bulletImprovements.length > 0 && (
            <section>
              <h3 className="m-0 mb-2 text-sm font-semibold text-text">CV bullet improvements</h3>
              <div className="flex flex-col gap-2">
                {prep.bulletImprovements.map((b) => (
                  <div key={b.improved} className="rounded-control border border-border bg-bg/70 p-3 text-xs">
                    {b.keyword && <div className="mb-1 font-semibold text-primary">{b.keyword}</div>}
                    {b.original && <div className="text-text-secondary line-through">{b.original}</div>}
                    <div className="text-text">{b.improved}</div>
                  </div>
                ))}
              </div>
            </section>
          )}

          <section className="flex flex-col gap-3">
            <h3 className="m-0 text-sm font-semibold text-text">3-question mini interview</h3>
            {prep.interview.questions.map((q, i) => (
              <PrepQuestionCard key={q.id} sessionId={prep.interview.id} number={i + 1} initial={q} />
            ))}
          </section>
        </div>
      )}
    </GradModal>
  );
}

export function OutreachModal({ job, onClose }: { job: JobRef; onClose: () => void }) {
  const [tone, setTone] = useState<"Formal" | "Direct">("Formal");
  const [draft, setDraft] = useState<{ coverLetter: string; pitch: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);

  async function generate() {
    setBusy(true);
    setError(null);
    setCopied(null);
    try {
      setDraft(await gradFetch(`graduate/jobs/${job.id}/outreach`, { method: "POST", body: JSON.stringify({ tone }) }));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  function copy(label: string, text: string) {
    navigator.clipboard.writeText(text).then(
      () => setCopied(label),
      () => setError("Clipboard is unavailable. Select the text and copy it manually."),
    );
  }

  return (
    <GradModal title={`Pitch & cover letter · ${job.company}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <div className="flex items-end gap-2">
          <label className="flex-1 text-xs font-medium text-text-secondary">
            Tone
            <select className={`${gradInput} mt-1`} value={tone} onChange={(e) => setTone(e.target.value as "Formal" | "Direct")}>
              <option value="Formal">Formal</option>
              <option value="Direct">Direct</option>
            </select>
          </label>
          <Button disabled={busy} onClick={() => void generate()}>
            {busy ? "Drafting…" : draft ? "Regenerate" : "Generate drafts"}
          </Button>
        </div>
        {error && <Alert kind="error">{error}</Alert>}
        {draft &&
          ([
            ["Cover letter", draft.coverLetter, 10],
            ["LinkedIn / recruiter pitch", draft.pitch, 4],
          ] as const).map(([label, text, rows]) => (
            <div key={label}>
              <div className="mb-1 flex items-center justify-between">
                <span className="text-xs font-semibold text-text">{label}</span>
                <Button size="sm" variant="secondary" onClick={() => copy(label, text)}>
                  {copied === label ? "Copied ✓" : "Copy"}
                </Button>
              </div>
              <textarea readOnly rows={rows} aria-label={label} className={gradInput} value={text} />
            </div>
          ))}
      </div>
    </GradModal>
  );
}
