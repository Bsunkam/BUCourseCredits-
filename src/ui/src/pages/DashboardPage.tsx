import { useState, type MouseEvent } from "react";
import { useNavigate } from "react-router-dom";
import { ErrorAlert, isReviewer, Loading, Stat, useLoad, useUser } from "../components";
import { caseStatus, courseStatus, deadlineText, formatDate } from "../labels";
import type { CaseStatus, CaseSummary, CourseStatus, Metrics } from "../types";

// Workflow order, so the bars read top to bottom as a case or course moves through review.
const CASE_ORDER: CaseStatus[] = ["Open", "AwaitingDocuments", "InReview", "Decided", "Posted"];
const COURSE_ORDER: CourseStatus[] = ["Pending", "Matching", "NeedsSyllabus", "NeedsReview", "ApprovedMatchFound", "Blocked", "Decided"];
const CLOSED: CaseStatus[] = ["Decided", "Posted"];

const pct = (n: number, d: number) => (d === 0 ? 0 : Math.round((n / d) * 100));

export default function DashboardPage() {
  const user = useUser();
  const navigate = useNavigate();
  const metrics = useLoad<Metrics>("/api/metrics");
  const cases = useLoad<CaseSummary[]>("/api/cases");
  const m = metrics.data;
  const list = cases.data;

  const reload = () => { void metrics.reload(); void cases.reload(); };
  const openCase = (c: CaseSummary) => navigate(isReviewer(user) ? `/cases/${c.id}` : `/cases/${c.id}/status`);

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Dashboard</h1>
          <p className="muted">Where every transfer-credit case and course stands right now. Click a case status to see those cases.</p>
        </div>
        <button onClick={reload}>Refresh</button>
      </div>
      <ErrorAlert error={metrics.error ?? cases.error} />
      {!m || !list ? <Loading /> : <Dashboard m={m} list={list} onStatus={(s) => navigate(`/cases?status=${s}`)} onCase={openCase} />}
    </>
  );
}

function Dashboard({ m, list, onStatus, onCase }: {
  m: Metrics; list: CaseSummary[]; onStatus: (s: CaseStatus) => void; onCase: (c: CaseSummary) => void;
}) {
  const active = list.filter((c) => !CLOSED.includes(c.status));
  const overdue = active.filter((c) => c.overdue);
  const openCourses = list.reduce((n, c) => n + c.openCourseCount, 0);
  const decidedCourses = m.coursesByStatus.Decided ?? 0;

  const caseRows: BarRow[] = CASE_ORDER.map((s) => ({
    key: s, label: caseStatus[s].label, value: m.casesByStatus[s] ?? 0, onClick: () => onStatus(s),
  }));
  const courseRows: BarRow[] = COURSE_ORDER.map((s) => ({
    key: s, label: courseStatus[s].label, value: m.coursesByStatus[s] ?? 0,
  }));

  const buckets: [string, (d: number) => boolean][] = [
    ["Due in 30 days or less", (d) => d >= 0 && d <= 30],
    ["31 to 90 days", (d) => d > 30 && d <= 90],
    ["91 to 180 days", (d) => d > 90 && d <= 180],
    ["More than 180 days", (d) => d > 180],
  ];
  const deadlineRows: BarRow[] = [
    { key: "overdue", label: "⚠ Overdue", value: overdue.length, critical: true },
    ...buckets.map(([label, test]) => ({ key: label, label, value: active.filter((c) => !c.overdue && test(c.daysRemaining)).length })),
  ];

  const resolution: Segment[] = [
    { key: "rule", label: "Approved equivalency", value: m.approvedRuleHits, color: "var(--viz-1)" },
    { key: "cache", label: "Reused earlier suggestion", value: m.cacheHits, color: "var(--viz-2)" },
    { key: "agent", label: "New agent run", value: m.aiCalls, color: "var(--viz-3)" },
  ];

  const attention = [...active].sort((a, b) => a.daysRemaining - b.daysRemaining).slice(0, 5);

  return (
    <div className="stack dash">
      <div className="grid-4">
        <Stat value={m.cases} label={`Cases (${active.length} still open)`} />
        <Stat value={overdue.length} label="Open cases past their deadline" />
        <Stat value={openCourses} label="Courses waiting for a decision" />
        <Stat value={`${pct(decidedCourses, m.courses)}%`} label={`Courses decided (${decidedCourses} of ${m.courses})`} />
      </div>

      <div className="viz-grid">
        <ChartCard title="Cases by status" note="Click a bar to open the matching cases.">
          <BarChart rows={caseRows} unit="case" />
        </ChartCard>
        <ChartCard title="Courses by status" note="Every course on every case.">
          <BarChart rows={courseRows} unit="course" />
        </ChartCard>
        <ChartCard title="How course matches were resolved"
          note={`${Math.round(m.reuseRate * 100)}% were resolved without calling the agent.` +
            (m.failedAgentRuns ? ` ${m.failedAgentRuns} agent run(s) failed and went to manual review.` : "")}>
          <StackedBar segments={resolution} unit="match" />
        </ChartCard>
        <ChartCard title="Open cases by time to deadline" note="Cases that are decided or posted are not counted.">
          <BarChart rows={deadlineRows} unit="case" />
        </ChartCard>
      </div>

      <div className="card">
        <h2>Closest deadlines</h2>
        {attention.length === 0 ? <div className="empty">No open cases.</div> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Case</th><th>Student (synthetic)</th><th>Status</th><th className="num">Open courses</th><th>Deadline</th></tr></thead>
              <tbody>
                {attention.map((c) => (
                  <tr key={c.id} className="clickable" onClick={() => onCase(c)}>
                    <td><a href={`/cases/${c.id}`} onClick={(e) => { e.preventDefault(); onCase(c); }}>{c.caseRef}</a></td>
                    <td>{c.studentName}</td>
                    <td>{caseStatus[c.status].label}</td>
                    <td className="num">{c.openCourseCount} / {c.courseCount}</td>
                    <td>{formatDate(c.deadlineDate)} <span className="muted small">· {deadlineText(c.daysRemaining, c.overdue)}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

// ---------- chart pieces (plain HTML, no chart library) ----------

type Tip = { x: number; y: number; text: string } | null;

function useTip() {
  const [tip, setTip] = useState<Tip>(null);
  const show = (text: string) => (e: MouseEvent) => setTip({ x: e.clientX, y: e.clientY, text });
  const hide = () => setTip(null);
  const node = tip && <div className="viz-tip" style={{ left: tip.x + 12, top: tip.y + 12 }} role="tooltip">{tip.text}</div>;
  return { show, hide, node };
}

const plural = (n: number, unit: string) => `${n} ${n === 1 ? unit : unit.endsWith("ch") ? `${unit}es` : `${unit}s`}`;

function ChartCard({ title, note, children }: { title: string; note?: string; children: React.ReactNode }) {
  return (
    <div className="card viz-card">
      <h2>{title}</h2>
      {children}
      {note && <p className="muted small viz-note">{note}</p>}
    </div>
  );
}

type BarRow = { key: string; label: string; value: number; critical?: boolean; onClick?: () => void };

function BarChart({ rows, unit }: { rows: BarRow[]; unit: string }) {
  const { show, hide, node } = useTip();
  const max = Math.max(1, ...rows.map((r) => r.value));
  const total = rows.reduce((n, r) => n + r.value, 0);
  return (
    <div className="viz-bars" onMouseLeave={hide}>
      {rows.map((r) => {
        const text = `${r.label.replace("⚠ ", "")}: ${plural(r.value, unit)} (${pct(r.value, total)}% of ${total})`;
        return (
          <div key={r.key} className={`viz-row${r.onClick ? " clickable" : ""}`} onMouseMove={show(text)}
            onClick={r.onClick} role={r.onClick ? "button" : undefined} tabIndex={r.onClick ? 0 : undefined}
            onKeyDown={r.onClick ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); r.onClick?.(); } } : undefined}
            aria-label={text}>
            <span className="viz-label">{r.label}</span>
            <span className="viz-track">
              {r.value > 0 && <span className={`viz-bar${r.critical ? " critical" : ""}`} style={{ width: `${(r.value / max) * 100}%` }} />}
              <span className="viz-value">{r.value}</span>
            </span>
          </div>
        );
      })}
      {node}
    </div>
  );
}

type Segment = { key: string; label: string; value: number; color: string };

function StackedBar({ segments, unit }: { segments: Segment[]; unit: string }) {
  const { show, hide, node } = useTip();
  const total = segments.reduce((n, s) => n + s.value, 0);
  if (total === 0) return <div className="empty">No matches run yet.</div>;
  return (
    <div className="stack" style={{ gap: 12 }} onMouseLeave={hide}>
      <div className="viz-stack" role="img" aria-label={segments.map((s) => `${s.label} ${s.value}`).join(", ")}>
        {segments.filter((s) => s.value > 0).map((s) => (
          <span key={s.key} style={{ flexGrow: s.value, background: s.color }}
            onMouseMove={show(`${s.label}: ${plural(s.value, unit)} (${pct(s.value, total)}%)`)} />
        ))}
      </div>
      <ul className="viz-legend">
        {segments.map((s) => (
          <li key={s.key}>
            <span className="viz-swatch" style={{ background: s.color }} />
            {s.label}
            <span className="viz-legend-value">{s.value} · {pct(s.value, total)}%</span>
          </li>
        ))}
      </ul>
      {node}
    </div>
  );
}
