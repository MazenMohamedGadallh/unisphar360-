import { useEffect, useState } from "react";
import { gradFetch } from "../lib/grad";
import { Panel, Tag } from "./grad";

interface Analytics {
  total: number;
  reachedInterview: number;
  interviewRatePercent: number | null;
  topRoles: { name: string; count: number }[];
  unevidencedSkills: { name: string; count: number }[];
  insights: string[];
}

/** `version` changes whenever the list does, so the numbers are fetched again. */
export function ApplicationInsights({ version }: { version: string }) {
  const [data, setData] = useState<Analytics | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    gradFetch<Analytics>("grad/applications/analytics")
      .then((a) => { setData(a); setFailed(false); })
      .catch(() => setFailed(true));
  }, [version]);

  if (failed) return <p className="text-sm text-text-muted">Application analytics could not be loaded.</p>;
  if (!data || data.total === 0) return null;
  return (
    <Panel title="What your applications show" className="mb-6">
      <p className="m-0 text-sm text-text">
        {data.reachedInterview} of {data.total} reached an interview or assessment ({data.interviewRatePercent}%).
      </p>
      <p className="text-sm text-text-secondary">Roles you target most: {data.topRoles.map((r) => `${r.name} (${r.count})`).join(", ")}.</p>
      {data.unevidencedSkills.length > 0 && (
        <div className="flex flex-wrap items-center gap-1.5 text-sm text-text-secondary">
          <span>Asked for by those jobs, not evidenced by you:</span>
          {data.unevidencedSkills.map((s) => <Tag key={s.name} tone="warn">{s.name} ({s.count})</Tag>)}
        </div>
      )}
      {data.insights.map((i) => <p key={i} className="mb-0 text-sm text-text-secondary">{i}</p>)}
    </Panel>
  );
}
