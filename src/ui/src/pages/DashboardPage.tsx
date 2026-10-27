import { useState, type ReactNode } from "react";
import { BarChart, ChartCard, pct, StackedBar, type BarRow, type Segment } from "../charts";
import { useNavigate } from "react-router-dom";
import { Badge, DetailPanel, ErrorAlert, isReviewer, Loading, Stat, useLoad, useUser } from "../components";
import { caseStatus, courseStatus, deadlineText, formatDate } from "../labels";
import type { CaseStatus, CaseSummary, CourseStatus, Metrics } from "../types";

// Workflow order, so the bars read top to bottom as a case or course moves through review.
const CASE_ORDER: CaseStatus[] = ["Open", "AwaitingDocuments", "InReview", "Decided", "Posted"];
const COURSE_ORDER: CourseStatus[] = ["Pending", "Matching", "NeedsSyllabus", "NeedsReview", "ApprovedMatchFound", "Blocked", "Decided"];
const CLOSED: CaseStatus[] = ["Decided", "Posted"];


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

  const byDeadline = (cs: CaseSummary[]) => [...cs].sort((a, b) => a.daysRemaining - b.daysRemaining);
  const attention = byDeadline(active).slice(0, 5);

  // Each tile opens the cases behind its number, right under the tiles.
  const [focus, setFocus] = useState<Focus | null>(null);
  const details: Record<Focus, { title: string; note: string; cases: CaseSummary[]; empty: string }> = {
    all: { title: "All cases", note: `${active.length} open, ${list.length - active.length} decided or posted.`, cases: byDeadline(list), empty: "No cases yet." },
    overdue: { title: "Open cases past their deadline", note: "Oldest deadline first.", cases: byDeadline(overdue), empty: "No open case is past its deadline." },
    waiting: {
      title: "Cases with courses waiting for a decision", note: `${openCourses} courses across these cases still need a final decision.`,
      cases: byDeadline(list.filter((c) => c.openCourseCount > 0)), empty: "Every course has a final decision.",
    },
    decided: {
      title: "Cases with decided courses", note: `${decidedCourses} of ${m.courses} courses have a final decision.`,
      cases: byDeadline(list.filter((c) => c.openCourseCount < c.courseCount)), empty: "No course has a final decision yet.",
    },
  };
  const tile = (key: Focus, value: ReactNode, label: string) => (
    <Stat value={value} label={label} active={focus === key} onClick={() => setFocus(focus === key ? null : key)} />
  );

  return (
    <div className="stack dash">
      <div className="grid-4">
        {tile("all", m.cases, `Cases (${active.length} still open)`)}
        {tile("overdue", overdue.length, "Open cases past their deadline")}
        {tile("waiting", openCourses, "Courses waiting for a decision")}
        {tile("decided", `${pct(decidedCourses, m.courses)}%`, `Courses decided (${decidedCourses} of ${m.courses})`)}
      </div>
      {focus && (
        <DetailPanel title={details[focus].title} note={details[focus].note} onClose={() => setFocus(null)}>
          <CaseTable cases={details[focus].cases} onCase={onCase} empty={details[focus].empty} />
        </DetailPanel>
      )}

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
        <CaseTable cases={attention} onCase={onCase} empty="No open cases." />
      </div>
    </div>
  );
}

type Focus = "all" | "overdue" | "waiting" | "decided";

function CaseTable({ cases, onCase, empty }: { cases: CaseSummary[]; onCase: (c: CaseSummary) => void; empty: string }) {
  if (cases.length === 0) return <div className="empty">{empty}</div>;
  return (
    <div className="table-wrap">
      <table>
        <thead><tr><th>Case</th><th>Student (synthetic)</th><th>Program</th><th>Status</th><th className="num">Open courses</th><th>Deadline</th></tr></thead>
        <tbody>
          {cases.map((c) => (
            <tr key={c.id} className="clickable" onClick={() => onCase(c)}>
              <td><a href={`/cases/${c.id}`} onClick={(e) => { e.preventDefault(); onCase(c); }}>{c.caseRef}</a></td>
              <td>{c.studentName}</td>
              <td>{c.program}</td>
              <td><Badge tone={caseStatus[c.status].tone}>{caseStatus[c.status].label}</Badge></td>
              <td className="num">{c.openCourseCount} / {c.courseCount}</td>
              <td>{formatDate(c.deadlineDate)} <span className="muted small">· {deadlineText(c.daysRemaining, c.overdue)}</span></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
