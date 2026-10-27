import { useState } from "react";
import { ErrorAlert, Loading, Pager, Stat, useLoad } from "../components";
import { formatDateTime } from "../labels";
import type { AgentRun, Metrics, Paged } from "../types";

const pct = (n: number, d: number) => (d === 0 ? "—" : `${Math.round((n / d) * 100)}%`);
const duration = (h?: number | null) => (h == null ? "—" : h < 48 ? `${h} h` : `${Math.round(h / 24)} days`);

export default function MetricsPage() {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const metrics = useLoad<Metrics>("/api/metrics");
  const runs = useLoad<Paged<AgentRun>>(`/api/agent-runs?page=${page}&pageSize=${pageSize}`);
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
          <div className="card stack">
            <h2>Primary outcome: time a course sits at zero units</h2>
            <p className="muted small" style={{ margin: 0 }}>
              From case opened to the course's final decision. The baseline today (discovery question 1) is not yet confirmed by the sponsor.
            </p>
            <div className="grid-3">
              <Stat value={duration(m.medianHoursAtZeroUnits)} label="Median, all decided courses" />
              <Stat value={duration(m.medianHoursAtZeroUnitsReviewed)} label="Median, courses an evaluator reviewed" />
              <Stat value={m.coursesStillAtZeroUnits} label="Courses still at zero units" />
            </div>
          </div>
          <div className="grid-3">
            <Stat value={m.cases} label="Cases" />
            <Stat value={m.courses} label="Courses" />
            <Stat value={`${Math.round(m.reuseRate * 100)}%`} label="Reuse rate (resolved without an agent call)"
              hint="Approved-rule and cache hits divided by all match resolutions" />
            <Stat value={m.aiCalls} label="Agent calls" hint="Full agent runs (stub runs locally)" />
            <Stat value={m.approvedRuleHits} label="Approved-equivalency hits" />
            <Stat value={m.cacheHits} label="Cached-suggestion hits" />
            <Stat value={pct(m.finalDecisionsWithReviewer, m.finalDecisions)} label={`Human-control compliance (${m.finalDecisions} human decisions)`} />
            <Stat value={m.appliedFromEquivalencyDb} label="Existing equivalencies applied (no new review)" />
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
            {!runs.data ? <Loading /> : runs.data.total === 0 ? <div className="empty">No agent runs yet.</div> : (
              <>
              <div className="table-wrap">
                <table>
                  <thead><tr><th>#</th><th>Path</th><th>Status</th><th>Model</th><th>Prompt</th><th className="num">ms</th><th>When</th></tr></thead>
                  <tbody>
                    {runs.data.items.map((r) => (
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
              {/* The server clamps an out-of-range page, so follow the page it actually returned. */}
              <Pager page={runs.data.page} pageSize={runs.data.pageSize} total={runs.data.total} unit="runs"
                onPage={setPage} onPageSize={(s) => { setPageSize(s); setPage(1); }} />
              </>
            )}
          </div>
        </div>
      )}
    </>
  );
}
