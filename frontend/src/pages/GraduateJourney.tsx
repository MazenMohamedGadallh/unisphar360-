import { useState } from "react";
import { gradFetch } from "../lib/grad";
import { Button } from "../components/ui";
import { Page, Panel, Tag, Trail, fmtDate, useGradData, type Journey } from "../components/grad";

const load = () => gradFetch<Journey>("graduate/journey");

export function GraduateJourney() {
  const { data, error, reload } = useGradData(load);
  const [picked, setPicked] = useState<number | null>(null);
  const selected = picked ?? data?.careerLevel ?? 1;
  const level = data?.levels[selected - 1];
  const left = level?.missions.filter((m) => m.status !== "completed").length ?? 0;

  return (
    <Page title="Career Journey" description="Eight levels. XP is paid only when the evidence for a mission exists, and only once."
      loading={!data} error={error} onRetry={() => void reload()}
      actions={<Button variant="secondary" to="/graduate/roadmap">Knowledge quizzes</Button>}>
      {data && level && (
        <div className="grid gap-5">
          <Panel>
            <p className="m-0 mb-5 text-sm text-text-secondary">
              <span className="text-2xl font-bold text-text tabular-nums">{data.xp} XP</span>
              <span className="ml-3">Level {data.careerLevel} of 8, {data.completedMissions} of {data.totalMissions} missions done.</span>
            </p>
            <Trail journey={data} selected={selected} onSelect={setPicked} />
          </Panel>

          <Panel title={`Level ${level.level}: ${level.title}`} aside={
            <Tag tone={level.status === "completed" ? "good" : level.status === "active" ? "info" : "plain"}>
              {level.status === "completed" ? `Badge earned: ${level.badge}` : level.status === "locked" ? "Locked: finish earlier levels first" : `${left} mission(s) to earn ${level.badge}`}
            </Tag>
          }>
            <ul className="m-0 list-none p-0">
              {level.missions.map((m) => (
                <li key={m.key} className="flex flex-wrap items-center justify-between gap-3 border-t border-border py-3.5">
                  <div className="min-w-0 max-w-[65ch]">
                    <p className="m-0 font-medium text-text">{m.title}</p>
                    <p className="m-0 text-sm text-text-secondary">{m.description}</p>
                    <p className="m-0 text-sm text-text-muted">Done when: {m.criteria}</p>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="text-sm font-semibold text-primary tabular-nums">+{m.xp} XP</span>
                    {m.status === "completed" ? <Tag tone="good">Completed {fmtDate(m.completedAtUtc!)}</Tag>
                      : m.status === "locked" ? <Tag>Locked</Tag>
                      : <Button size="sm" to={m.route}>Open</Button>}
                  </div>
                </li>
              ))}
            </ul>
          </Panel>

          <Panel title="Development history">
            {data.history.length === 0 ? (
              <p className="m-0 text-sm text-text-muted">Rewards you earn are listed here.</p>
            ) : (
              <ul className="m-0 list-none space-y-1.5 p-0 text-sm">
                {data.history.map((h) => (
                  <li key={h.reason} className="flex justify-between gap-3">
                    <span className="text-text">{h.reason}</span>
                    <span className="text-text-muted tabular-nums">+{h.amount} XP</span>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>
      )}
    </Page>
  );
}
