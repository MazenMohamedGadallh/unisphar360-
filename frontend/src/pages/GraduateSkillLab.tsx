import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { gradFetch } from "../lib/grad";
import { Alert, Button } from "../components/ui";
import { Bullets, Page, Panel, Tag, field, fmtDate, label, useAction, useGradData } from "../components/grad";

interface Attempt {
  id: number; skill: string; difficulty: string; createdAtUtc: string; submittedAtUtc: string | null; score: number | null; passed: boolean;
  feedback: string | null; scenario: string; questions: { question: string; options: string[] }[]; task: string; criteria: string[];
  review: { correct: boolean; correctIndex: number; explanation: string }[] | null; improvements: string[] | null; written: string | null;
}
interface Lab { skills: string[]; passScore: number; attempts: Attempt[] }

const load = () => gradFetch<Lab>("graduate/lab");

export function GraduateSkillLab() {
  const [params] = useSearchParams();
  const { data, error, reload } = useGradData(load);
  const [skill, setSkill] = useState(params.get("skill") ?? "");
  const [difficulty, setDifficulty] = useState("Medium");
  const [openId, setOpenId] = useState<number | null>(null);
  const [answers, setAnswers] = useState<Record<number, number>>({});
  const [written, setWritten] = useState("");
  const { busy, message, run } = useAction();

  const attempt = data?.attempts.find((a) => a.id === openId);
  const chosen = data?.skills.find((s) => s.toLowerCase() === skill.toLowerCase()) ?? data?.skills[0] ?? "";

  function open(a: Attempt) {
    setOpenId(a.id);
    setAnswers({});
    setWritten("");
  }

  async function start() {
    await run("start", async () => {
      const next = await gradFetch<Attempt>("graduate/lab", { method: "POST", body: JSON.stringify({ skill: chosen, difficulty }) });
      await reload();
      open(next);
    });
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    await run("submit", async () => {
      if (attempt!.questions.some((_, i) => answers[i] === undefined)) throw new Error("Answer every question first.");
      await gradFetch(`graduate/lab/${attempt!.id}/submit`, {
        method: "POST",
        body: JSON.stringify({ answers: attempt!.questions.map((_, i) => answers[i]), written }),
      });
      await reload();
    });
  }

  return (
    <Page title="Skill Proof Lab" description="Show what you can do. Each challenge has scenario questions and a written task. Your answers are read, never executed."
      loading={!data} error={error} onRetry={() => void reload()}>
      {data && (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,300px)_minmax(0,1fr)] lg:items-start">
          <div className="grid gap-5">
            <Panel title="New challenge">
              <label className={label}>Skill
                <select className={`${field} mt-1`} value={chosen} onChange={(e) => setSkill(e.target.value)}>
                  {data.skills.map((s) => <option key={s}>{s}</option>)}
                </select>
              </label>
              <label className={`${label} mt-3`}>Difficulty
                <select className={`${field} mt-1`} value={difficulty} onChange={(e) => setDifficulty(e.target.value)}>
                  {["Easy", "Medium", "Hard"].map((d) => <option key={d}>{d}</option>)}
                </select>
              </label>
              <div className="mt-4"><Button disabled={busy !== null} onClick={() => void start()}>{busy === "start" ? "Preparing..." : "Get challenge"}</Button></div>
              <p className="mb-0 text-xs text-text-muted">Pass mark: {data.passScore}/100. Questions are 60 points and the written task 40, graded by AI.</p>
            </Panel>
            <Panel title="Your attempts">
              {data.attempts.length === 0 && <p className="m-0 text-sm text-text-muted">No attempts yet.</p>}
              <ul className="m-0 list-none space-y-1 p-0">
                {data.attempts.map((a) => (
                  <li key={a.id}>
                    <button type="button" aria-pressed={a.id === openId} onClick={() => open(a)}
                      className={`flex w-full cursor-pointer justify-between gap-2 rounded-control border-0 px-3 py-2 text-start text-sm ${a.id === openId ? "bg-primary-light" : "bg-transparent hover:bg-bg"}`}>
                      <span><span className="block text-text">{a.skill}, {a.difficulty}</span><span className="text-text-muted">{fmtDate(a.createdAtUtc)}</span></span>
                      <Tag tone={!a.submittedAtUtc ? "plain" : a.passed ? "good" : "bad"}>{!a.submittedAtUtc ? "Open" : `${a.score}/100`}</Tag>
                    </button>
                  </li>
                ))}
              </ul>
            </Panel>
          </div>

          <div>
            {message && <Alert kind={message.kind}>{message.text}</Alert>}
            {!attempt ? (
              <Panel><p className="m-0 text-sm text-text-secondary">Choose a skill and get a challenge, or open one of your attempts.</p></Panel>
            ) : (
              <form onSubmit={(e) => void submit(e)} className="grid gap-5">
                {attempt.submittedAtUtc && (
                  <Panel title={`${attempt.passed ? "Passed" : "Not passed yet"}: ${attempt.score}/100`}>
                    <p className="mt-0 text-text">{attempt.feedback}</p>
                    <Bullets items={attempt.improvements ?? []} empty="No further improvements suggested." />
                  </Panel>
                )}
                <Panel title={`${attempt.skill}, ${attempt.difficulty}`}>
                  <p className="m-0 whitespace-pre-wrap text-text">{attempt.scenario}</p>
                </Panel>
                {attempt.questions.map((q, i) => (
                  <Panel key={i}>
                    <fieldset className="m-0 border-0 p-0" disabled={!!attempt.submittedAtUtc}>
                      <legend className="mb-2 p-0 font-medium whitespace-pre-wrap text-text">{i + 1}. {q.question}</legend>
                      {q.options.map((o, j) => (
                        <label key={j} className="flex cursor-pointer items-start gap-2.5 py-1 text-sm text-text-secondary">
                          <input type="radio" className="accent-primary" name={`q${i}`} checked={answers[i] === j} onChange={() => setAnswers({ ...answers, [i]: j })} />
                          <span>{o}{attempt.review?.[i].correctIndex === j && <strong className="ml-2 text-success">Correct answer</strong>}</span>
                        </label>
                      ))}
                    </fieldset>
                    {attempt.review && (
                      <p className="mb-0 text-sm text-text-muted">{attempt.review[i].correct ? "You answered correctly. " : "Your answer was not correct. "}{attempt.review[i].explanation}</p>
                    )}
                  </Panel>
                ))}
                <Panel title="Written task">
                  <p className="mt-0 whitespace-pre-wrap text-text">{attempt.task}</p>
                  <p className="text-sm text-text-muted">Graded on: {attempt.criteria.join(", ")}.</p>
                  {attempt.submittedAtUtc ? (
                    <p className="m-0 whitespace-pre-wrap text-sm text-text-secondary">{attempt.written}</p>
                  ) : (
                    <>
                      <textarea className={field} rows={8} minLength={40} maxLength={4000} required aria-label="Your answer" value={written} onChange={(e) => setWritten(e.target.value)} />
                      <div className="mt-3"><Button type="submit" disabled={busy !== null}>{busy === "submit" ? "Grading..." : "Submit for grading"}</Button></div>
                    </>
                  )}
                </Panel>
              </form>
            )}
          </div>
        </div>
      )}
    </Page>
  );
}
