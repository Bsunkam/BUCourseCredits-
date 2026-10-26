import { ErrorAlert, Loading, Stat, useLoad } from "../components";
import { formatDateTime } from "../labels";
import type { AgentRun, Metrics } from "../types";

const pct = (n: number, d: number) => (d === 0 ? "—" : `${Math.round((n / d) * 100)}%`);

export default function MetricsPage() {
  const metrics = useLoad<Metrics>("/api/metrics");
  const runs = useLoad<AgentRun[]>("/api/agent-runs?take=50");
  const m = metrics.data;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Metrics & AI usage evidence</h1>
          <p className="muted">Measured from stored agent runs, evaluations and cases. Baselines still need sponsor confirmation.</p>
        </div>
        <button onClick={() => { void metrics.reload(); void runs.reload(); }}>Refresh</button>
      </div>
      <ErrorAlert error={metrics.error ?? runs.error} />
      {!m ? <Loading /> : (
        <div className="stack">
          <div className="grid-3">
            <Stat value={m.cases} label="Cases" />
            <Stat value={m.courses} label="Courses" />
            <Stat value={`${Math.round(m.reuseRate * 100)}%`} label="Reuse rate (resolved without an agent call)"
              hint="Approved-rule and cache hits divided by all match resolutions" />
            <Stat value={m.aiCalls} label="Agent calls" hint="Full agent runs (stub runs locally)" />
            <Stat value={m.approvedRuleHits} label="Approved-equivalency hits" />
            <Stat value={m.cacheHits} label="Cached-suggestion hits" />
            <Stat value={pct(m.finalDecisionsWithReviewer, m.finalDecisions)} label={`Human-control compliance (${m.finalDecisions} final decisions)`} />
            <Stat value={pct(m.agentRunsWithModelRecord, m.aiCalls)} label="Agent runs stored with model + prompt version" />
            <Stat value={m.failedAgentRuns} label="Failed agent runs (fell back to manual review)" />
          </div>
          <div className="card">
            <h2>Cases by status</h2>
            <div className="row">
              {Object.entries(m.casesByStatus).map(([k, v]) => <span key={k} className="badge tone-neutral">{k}: {v}</span>)}
            </div>
          </div>
          <div className="card">
            <h2>Recent agent runs</h2>
            {!runs.data ? <Loading /> : (
              <div className="table-wrap">
                <table>
                  <thead><tr><th>#</th><th>Path</th><th>Status</th><th>Model</th><th>Prompt</th><th className="num">ms</th><th>When</th></tr></thead>
                  <tbody>
                    {runs.data.map((r) => (
                      <tr key={r.id}>
                        <td>{r.id}</td>
                        <td>{r.source}</td>
                        <td>{r.status}</td>
                        <td className="small">{r.model ?? <span className="muted">{r.note}</span>}</td>
                        <td className="small">{r.promptVersion}</td>
                        <td className="num">{r.latencyMs}</td>
                        <td className="small">{formatDateTime(r.createdAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}
    </>
  );
}
