import { Link } from "react-router-dom";
import { gradFetch } from "../lib/grad";
import { Alert, Button } from "../components/ui";
import { Bullets, Meter, Page, Panel, Tag, useAction, useGradData } from "../components/grad";

interface Snapshot {
  createdAtUtc: string;
  careerReady: boolean;
  strengths: string[];
  dimensions: { name: string; percent: number | null; explanation: string; route: string }[];
  criteria: { name: string; met: boolean; detail: string; route: string }[];
}

const load = () => gradFetch<{ current: Snapshot; history: Snapshot[] }>("graduate/report");

export function GraduateReports() {
  const { data, error, reload } = useGradData(load);
  const { busy, message, run } = useAction();
  const now = data?.current;

  return (
    <Page title="Career Readiness" description="Six separate measures from your own evidence. There is deliberately no single overall score."
      loading={!data} error={error} onRetry={() => void reload()}
      actions={
        <>
          <Button variant="secondary" onClick={() => window.print()}>Print</Button>
          <Button disabled={busy !== null} onClick={() => void run("save", async () => {
            await gradFetch("graduate/report", { method: "POST" });
            await reload();
          }, "Snapshot saved.")}>Save a snapshot</Button>
        </>
      }>
      {now && data && (
        <div className="grid gap-5">
          {message && <Alert kind={message.kind}>{message.text}</Alert>}
          <Panel title="Where you stand">
            <div className="grid gap-5 sm:grid-cols-2">
              {now.dimensions.map((d) => (
                <div key={d.name}>
                  <Meter name={d.name} value={d.percent} />
                  <p className="m-0 mt-1 text-xs text-text-muted">{d.explanation} <Link to={d.route} className="text-primary">Improve</Link></p>
                </div>
              ))}
            </div>
          </Panel>

          <Panel title="Career ready criteria" aside={<Tag tone={now.careerReady ? "good" : "warn"}>{now.careerReady ? "All met" : `${now.criteria.filter((c) => c.met).length} of ${now.criteria.length} met`}</Tag>}>
            <ul className="m-0 list-none p-0">
              {now.criteria.map((c) => (
                <li key={c.name} className="flex flex-wrap items-center justify-between gap-3 border-t border-border py-3 text-sm">
                  <span><span className="block font-medium text-text">{c.name}</span><span className="text-text-muted">{c.detail}</span></span>
                  {c.met ? <Tag tone="good">Met</Tag> : <Button size="sm" variant="secondary" to={c.route}>Work on this</Button>}
                </li>
              ))}
            </ul>
          </Panel>

          <Panel title="Strengths so far"><Bullets items={now.strengths} empty="Strengths appear as you add evidence." /></Panel>

          <Panel title="Saved snapshots">
            {data.history.length === 0 ? (
              <p className="m-0 text-sm text-text-muted">Save a snapshot to track how you improve.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-text-muted">
                      <th className="py-1 pr-3 text-start font-medium">Saved</th>
                      {now.dimensions.map((d) => <th key={d.name} className="pr-3 text-start font-medium">{d.name}</th>)}
                    </tr>
                  </thead>
                  <tbody>
                    {data.history.map((h) => (
                      <tr key={h.createdAtUtc} className="border-t border-border tabular-nums">
                        <td className="py-1.5 pr-3 text-text">{new Date(h.createdAtUtc).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })}</td>
                        {h.dimensions.map((d) => <td key={d.name} className="pr-3 text-text-secondary">{d.percent === null ? "-" : `${d.percent}%`}</td>)}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Panel>
        </div>
      )}
    </Page>
  );
}
