import { useEffect, useState } from "react";
import { gradFetch } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import { Alert, Button } from "./ui";
import { gradInput } from "./GradModal";

interface Drill {
  drillId: string;
  dimension: string;
  questions: string[];
  bonusXp: number;
}

interface DrillFeedback {
  feedback: string;
  score: number;
  improvements: string[];
  answered: number;
  completed: boolean;
  xpAwarded: number;
  baseline: number;
  drillAverage: number;
  improved: boolean;
}

/** Weakest rubric criterion with a 5-minute targeted drill. The server picks the same one (InterviewService.WeakestDimension). */
export function RecoveryDrill({ sessionId, weakest }: { sessionId: number; weakest: string }) {
  const [drill, setDrill] = useState<Drill | null>(null);
  const [index, setIndex] = useState(0);
  const [answer, setAnswer] = useState("");
  const [feedback, setFeedback] = useState<DrillFeedback | null>(null);
  const [secondsLeft, setSecondsLeft] = useState(300);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!drill) return;
    const timer = setInterval(() => setSecondsLeft((s) => Math.max(0, s - 1)), 1000);
    return () => clearInterval(timer);
  }, [drill]);

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

  const start = () =>
    run(async () => {
      const next = await gradFetch<Drill>("graduate/interview/recovery-drill", {
        method: "POST",
        body: JSON.stringify({ sessionId }),
      });
      setDrill(next);
      setIndex(0);
      setAnswer("");
      setFeedback(null);
      setSecondsLeft(300);
    });

  const coach = () =>
    run(async () => {
      setFeedback(
        await gradFetch<DrillFeedback>("graduate/interview/recovery-drill/answer", {
          method: "POST",
          body: JSON.stringify({ drillId: drill!.drillId, index, answerText: answer }),
        }),
      );
    });

  return (
    <div className="rounded-control border border-warning bg-warning-light/40 p-4">
      <div className="text-xs font-semibold uppercase tracking-wide text-warning">Weakest area in this session</div>
      <div className="mb-2 text-sm font-semibold text-text">{drill?.dimension ?? weakest}</div>
      {error && <Alert kind="error">{error}</Alert>}
      {!drill ? (
        <Button size="sm" disabled={busy} onClick={() => void start()}>
          <UiIcon name="sparkles" />
          {busy ? "Building drill…" : "Start 5-Min Recovery Drill"}
        </Button>
      ) : (
        <div className="flex flex-col gap-2">
          <div className="flex justify-between text-xs text-text-secondary">
            <span>Question {index + 1} of {drill.questions.length}</span>
            <span className="dash-num" role="timer">
              {secondsLeft === 0
                ? "Time is up, finish when ready"
                : `${Math.floor(secondsLeft / 60)}:${String(secondsLeft % 60).padStart(2, "0")}`}
            </span>
          </div>
          <p className="m-0 text-sm text-text">{drill.questions[index]}</p>
          <textarea
            rows={3}
            aria-label="Drill answer"
            className={gradInput}
            value={answer}
            onChange={(e) => setAnswer(e.target.value)}
          />
          {feedback && (
            <div className="text-xs text-text-secondary">
              <strong className="text-primary">{feedback.score}/10</strong> {feedback.feedback}
              {feedback.completed && (
                <div className={`mt-1 font-semibold ${feedback.improved ? "text-success" : "text-warning"}`}>
                  Drill complete. +{feedback.xpAwarded} bonus XP. {drill.dimension}: {feedback.baseline} →{" "}
                  {feedback.drillAverage}/10{feedback.improved ? ", improved." : ", not higher yet."}
                </div>
              )}
            </div>
          )}
          {!feedback ? (
            <Button size="sm" disabled={busy || answer.trim().length < 10} onClick={() => void coach()}>
              {busy ? "Coaching…" : "Get coaching"}
            </Button>
          ) : feedback.completed ? (
            !feedback.improved && (
              <Button size="sm" disabled={busy} onClick={() => void start()}>
                {busy ? "Building drill…" : "Drill again"}
              </Button>
            )
          ) : (
              <Button
                size="sm"
                onClick={() => {
                  setIndex(index + 1);
                  setAnswer("");
                  setFeedback(null);
                }}
              >
                Next micro-question
              </Button>
          )}
        </div>
      )}
    </div>
  );
}
