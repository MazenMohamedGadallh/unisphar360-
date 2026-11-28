import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { gradFetch } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import { Alert, Button, Card, CardHeader, ConfirmDialog, ErrorState, LoadingBlock } from "./ui";
import { GradModal } from "./GradModal";

interface Level {
  level: number;
  title: string;
  topic: string;
  status: "completed" | "skipped" | "active" | "locked";
}

interface Quest {
  targetRole: string;
  xp: number;
  quizAttempts: number;
  readinessPercent: number;
  passScore: number;
  levels: Level[];
}

interface Quiz {
  level: number;
  questions: { question: string; options: string[] }[];
}

interface QuizResult {
  passed: boolean;
  score: number;
  total: number;
  xpAwarded: number;
  readinessPercent: number;
  review: { correct: boolean; correctIndex: number; explanation: string }[];
}

interface PrepItem {
  concept: string;
  detail: string;
  reviewed: boolean;
}

/** AI text marks code with backticks. */
const withCode = (text: string) =>
  text.split("`").map((part, i) =>
    i % 2 ? <code key={i} className="rounded bg-primary-light px-1 text-primary">{part}</code> : part,
  );

function LevelPrep({ level }: { level: number }) {
  const [items, setItems] = useState<PrepItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const requested = useRef(false);

  const load = useCallback(() => {
    setError(null);
    gradFetch<{ items: PrepItem[] }>(`graduate/quest/levels/${level}/prep`, { method: "POST" })
      .then((p) => setItems(p.items))
      .catch((e: Error) => setError(e.message));
  }, [level]);

  useEffect(() => {
    // Generating costs an AI call, so StrictMode's second effect run must not repeat it.
    if (requested.current) return;
    requested.current = true;
    load();
  }, [load]);

  async function toggle(index: number) {
    try {
      const saved = await gradFetch<{ items: PrepItem[] }>(`graduate/quest/levels/${level}/prep/${index}`, {
        method: "PUT",
        body: JSON.stringify({ reviewed: !items![index].reviewed }),
      });
      setItems(saved.items);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="mt-3">
      <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-primary">
        Prepare for this level
        {items && ` · ${items.filter((i) => i.reviewed).length}/${items.length} reviewed`}
      </div>
      {error && (
        <div className="mb-2 flex flex-wrap items-center gap-2 text-xs text-danger" role="alert">
          {error}
          <Button size="sm" variant="secondary" onClick={load}>Try again</Button>
        </div>
      )}
      {!items ? (
        !error && <p className="m-0 text-xs text-text-secondary">Building your prep guide from your CV and target role…</p>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-2 p-0">
          {items.map((item, i) => (
            <li key={item.concept} className="flex flex-wrap items-start justify-between gap-2 rounded-control border border-border bg-bg/70 p-3">
              <div className="min-w-[200px] flex-1">
                <div className="text-sm font-semibold text-text">{withCode(item.concept)}</div>
                <div className="text-xs leading-relaxed text-text-secondary">{withCode(item.detail)}</div>
              </div>
              <Button size="sm" variant={item.reviewed ? "ghost" : "secondary"} onClick={() => void toggle(i)}>
                {item.reviewed ? "Reviewed ✓" : "Mark Concept Reviewed"}
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function CareerQuest() {
  const [quest, setQuest] = useState<Quest | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [quiz, setQuiz] = useState<Quiz | null>(null);
  const [answers, setAnswers] = useState<number[]>([]);
  const [result, setResult] = useState<QuizResult | null>(null);
  const [skipping, setSkipping] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const resultTop = useRef<HTMLDivElement>(null);

  const load = useCallback(() => {
    setLoadError(null);
    return gradFetch<Quest>("graduate/quest")
      .then(setQuest)
      .catch((e: Error) => setLoadError(e.message));
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    resultTop.current?.scrollIntoView({ block: "nearest" });
  }, [result]);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const start = (level: number) =>
    run(async () => {
      const next = await gradFetch<Quiz>(`graduate/quest/levels/${level}/quiz`, { method: "POST" });
      setQuiz(next);
      setAnswers(next.questions.map(() => -1));
      setResult(null);
    });

  const submit = () =>
    run(async () => {
      setResult(
        await gradFetch<QuizResult>(`graduate/quest/levels/${quiz!.level}/submit`, {
          method: "POST",
          body: JSON.stringify({ answers }),
        }),
      );
      await load();
    });

  const skip = (level: number) =>
    run(async () => {
      setSkipping(null);
      setQuest(await gradFetch<Quest>(`graduate/quest/levels/${level}/skip`, { method: "POST" }));
      setQuiz(null);
    });

  if (loadError) return <Card><ErrorState message={loadError} onRetry={() => void load()} /></Card>;
  if (!quest) return <LoadingBlock label="Loading your Career Quest…" />;

  const answered = answers.filter((a) => a >= 0).length;
  const nextLevel = quest.levels.find((l) => l.status === "active");

  const footer = quiz && (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <span className="dash-num text-xs font-semibold text-text-secondary" aria-live="polite">
        {result ? `${result.score}/${result.total} correct` : `Answered ${answered}/${quiz.questions.length}`}
      </span>
      <div className="flex flex-wrap gap-2">
        {!result ? (
          <>
            <Button size="sm" variant="secondary" onClick={() => setQuiz(null)}>Cancel</Button>
            {nextLevel?.level === quiz.level && (
              <Button size="sm" variant="secondary" disabled={busy} onClick={() => setSkipping(quiz.level)}>
                Skip Level
              </Button>
            )}
            <Button size="sm" disabled={busy || answered < quiz.questions.length} onClick={() => void submit()}>
              {busy ? "Grading…" : "Submit Answers"}
            </Button>
          </>
        ) : result.passed ? (
          <Button onClick={() => setQuiz(null)}>
            {nextLevel ? `Continue to Level ${nextLevel.level}` : "Back to quest map"}
          </Button>
        ) : (
          <>
            <Button size="sm" variant="secondary" onClick={() => setQuiz(null)}>Back to prep guide</Button>
            <Button size="sm" disabled={busy} onClick={() => void start(quiz.level)}>
              {busy ? "Preparing quiz…" : "Retry with New Questions"}
            </Button>
          </>
        )}
      </div>
    </div>
  );

  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={`Career Quest · ${quest.targetRole}`}
        action={
          <div className="flex flex-wrap gap-2 text-xs font-bold">
            <span className="dash-num rounded-md bg-primary-light px-2.5 py-1 text-primary">{quest.xp} XP</span>
            <span className="dash-num rounded-md bg-success-light px-2.5 py-1 text-success">
              Job Readiness {quest.readinessPercent}%
            </span>
          </div>
        }
      />
      <div className="p-5">
        {error && !quiz && <Alert kind="error">{error}</Alert>}
        <ol className="m-0 flex list-none flex-col p-0">
          {quest.levels.map((l, i) => (
            <li key={l.level} className="flex gap-4">
              <div className="flex flex-col items-center">
                <span
                  aria-hidden="true"
                  className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full border-2 text-sm font-bold ${
                    l.status === "completed"
                      ? "border-success bg-success-light text-success"
                      : l.status === "skipped"
                        ? "border-warning bg-warning-light text-warning"
                        : l.status === "active"
                          ? "border-primary bg-primary text-white"
                          : "border-border bg-bg text-text-muted"
                  }`}
                >
                  {l.status === "completed" ? <UiIcon name="check" /> : l.level}
                </span>
                {i < quest.levels.length - 1 && (
                  <span className={`w-0.5 flex-1 ${l.status === "completed" ? "bg-success" : "bg-border"}`} />
                )}
              </div>
              <div className="min-w-0 flex-1 pb-6">
                <div className={l.status === "active" ? "rounded-card border border-primary/50 bg-primary-light/20 p-4" : ""}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className={`text-sm font-semibold ${l.status === "locked" ? "text-text-muted" : "text-text"}`}>
                        {l.title}
                      </div>
                      <div className="text-xs text-text-secondary">{l.topic}</div>
                      <span className="text-[11px] font-semibold uppercase tracking-wide text-text-muted">{l.status}</span>
                    </div>
                    {l.status === "skipped" && (
                      <Button size="sm" variant="secondary" disabled={busy} onClick={() => void start(l.level)}>
                        Take it now for XP
                      </Button>
                    )}
                  </div>
                  {l.status === "active" && (
                    <>
                      <LevelPrep key={`${quest.targetRole}-${l.level}`} level={l.level} />
                      <div className="mt-3 flex flex-wrap gap-2">
                        <Button size="sm" disabled={busy} onClick={() => void start(l.level)}>
                          {busy && !quiz ? "Preparing quiz…" : "Start Level Quiz"}
                        </Button>
                        <Button size="sm" variant="ghost" disabled={busy} onClick={() => setSkipping(l.level)}>
                          Skip Level
                        </Button>
                      </div>
                    </>
                  )}
                </div>
              </div>
            </li>
          ))}
        </ol>
        <p className="m-0 text-xs text-text-muted">
          Score {quest.passScore}/5 to pass a level. Quiz attempts: {quest.quizAttempts}.
        </p>
      </div>

      {quiz && (
        <GradModal
          title={`Level ${quiz.level} · ${quest.levels[quiz.level - 1]?.title}`}
          onClose={() => setQuiz(null)}
          footer={footer}
        >
          <div className="flex flex-col gap-5">
            {result && (
              <div
                ref={resultTop}
                role="status"
                className={`rounded-card border-2 p-5 text-center ${
                  result.passed ? "border-success bg-success-light" : "border-warning bg-warning-light"
                }`}
              >
                <div className={`dash-num text-2xl font-bold ${result.passed ? "text-success" : "text-warning"}`}>
                  {result.passed
                    ? `${result.score}/${result.total} — Level Passed!`
                    : `${result.score}/${result.total} — Needs ${quest.passScore}/${result.total} to pass`}
                </div>
                <div className="mt-1 text-sm font-semibold text-text">
                  +{result.xpAwarded} XP · Job Readiness {result.readinessPercent}%
                </div>
              </div>
            )}
            {error && <Alert kind="error">{error}</Alert>}
            {quiz.questions.map((q, i) => (
              <fieldset key={q.question} className="m-0 min-w-0 border-0 p-0" disabled={!!result}>
                <legend className="mb-2 font-medium text-text">
                  {i + 1}. {withCode(q.question)}
                  {result && (
                    <span className={result.review[i].correct ? "ml-2 text-success" : "ml-2 text-danger"}>
                      {result.review[i].correct ? "✓ Correct" : "✗ Wrong"}
                    </span>
                  )}
                </legend>
                {q.options.map((option, j) => (
                  <label
                    key={j}
                    className={`mb-1 flex cursor-pointer gap-2 rounded-control border px-3 py-2 ${
                      result && result.review[i].correctIndex === j
                        ? "border-success bg-success-light"
                        : result && answers[i] === j
                          ? "border-danger bg-danger-light"
                          : "border-border"
                    }`}
                  >
                    <input
                      type="radio"
                      className="accent-primary"
                      name={`q${i}`}
                      checked={answers[i] === j}
                      onChange={() => setAnswers(answers.map((a, k) => (k === i ? j : a)))}
                    />
                    <span className="min-w-0 text-text">{withCode(option)}</span>
                  </label>
                ))}
                {result && <p className="m-0 mt-1 text-xs text-text-secondary">{withCode(result.review[i].explanation)}</p>}
              </fieldset>
            ))}
          </div>
        </GradModal>
      )}

      {/* ConfirmDialog is z-1000, above the quiz modal; the portal keeps it out of the blurred card. */}
      {skipping !== null &&
        createPortal(
          <ConfirmDialog
            open
            title={`Skip level ${skipping}?`}
            confirmLabel="Skip Level"
            onCancel={() => setSkipping(null)}
            onConfirm={() => void skip(skipping)}
          >
            The next level unlocks, but you earn no XP and this level does not count towards Job Readiness. You can take
            its quiz later.
          </ConfirmDialog>,
          document.body,
        )}
    </Card>
  );
}
