import { useState, type ReactNode } from "react";
import { BarChart, ChartCard, DonutChart, LineChart, pct, SERIES, type Segment } from "../charts";
import { Badge, DetailPanel, ErrorAlert, Loading, Stat, useLoad } from "../components";
import { caseStatus, courseStatus, formatBytes, formatDate, outcomeLabel, scanStatus } from "../labels";
import type { CaseStatus, CourseStatus, OutcomeType, Overview, ScanStatus } from "../types";

// ---------- tile drill-downs (rows come from /api/overview/details/{topic}) ----------

type Row = Record<string, unknown>;
type Column = { key: string; label: string; num?: boolean; total?: boolean; show?: (v: unknown) => ReactNode };
type Topic = "cases" | "courses" | "decisions" | "units" | "equivalencies" | "catalogue" | "institutions" | "documents";

const text = (v: unknown) => (v === null || v === undefined || v === "" ? "—" : String(v));
const verified = (v: unknown) => (v === "Yes" ? <Badge tone="success">Verified</Badge> : <Badge tone="warn">Needs check</Badge>);
const outcome = (v: unknown) => outcomeLabel[v as OutcomeType] ?? text(v);
const date = (v: unknown) => (v ? formatDate(String(v)) : "—");
const units = (v: unknown) => Number(v).toLocaleString(undefined, { maximumFractionDigits: 2 });

const TOPICS: Record<Topic, { title: string; note: string; columns: Column[] }> = {
  cases: {
    title: "Transfer cases", note: "Cases grouped by status and program. Overdue and due-soon counts include open cases only.",
    columns: [
      { key: "status", label: "Status", show: (v) => { const s = caseStatus[v as CaseStatus]; return <Badge tone={s.tone}>{s.label}</Badge>; } },
      { key: "program", label: "Program" },
      { key: "cases", label: "Cases", num: true, total: true },
      { key: "overdue", label: "Overdue", num: true, total: true },
      { key: "dueIn30Days", label: "Due in 30 days", num: true, total: true },
    ],
  },
  courses: {
    title: "Courses submitted", note: "Every submitted course, by source institution and review status.",
    columns: [
      { key: "institution", label: "Source institution" },
      { key: "status", label: "Status", show: (v) => { const s = courseStatus[v as CourseStatus]; return <Badge tone={s.tone}>{s.label}</Badge>; } },
      { key: "courses", label: "Courses", num: true, total: true },
      { key: "credits", label: "BU credits (converted)", num: true, total: true, show: units },
    ],
  },
  decisions: {
    title: "Final decisions", note: "Most recent 200 decisions made by the academic department.",
    columns: [
      { key: "decisionAt", label: "Decided", show: date },
      { key: "institution", label: "Source institution" },
      { key: "sourceCourse", label: "Source course" },
      { key: "buCourse", label: "BU course" },
      { key: "outcomeType", label: "Outcome", show: outcome },
      { key: "awardedUnits", label: "Units", num: true, total: true, show: units },
    ],
  },
  units: {
    title: "Units awarded", note: "Awarded units by BU course and outcome, from final decisions.",
    columns: [
      { key: "buCourse", label: "BU course", show: (v) => text(v ?? "No BU course (elective or no credit)") },
      { key: "outcomeType", label: "Outcome", show: outcome },
      { key: "decisions", label: "Decisions", num: true, total: true },
      { key: "awardedUnits", label: "Units awarded", num: true, total: true, show: units },
    ],
  },
  equivalencies: {
    title: "Approved equivalencies", note: "Approved mappings reused automatically, with no AI call.",
    columns: [
      { key: "institution", label: "Source institution" },
      { key: "sourceCourse", label: "Source course" },
      { key: "buCourse", label: "BU course", show: (v) => text(v ?? "Elective / no BU course") },
      { key: "outcomeType", label: "Outcome", show: outcome },
      { key: "program", label: "Program" },
      { key: "verifiedStatus", label: "Catalogue", show: verified },
      { key: "effectiveFrom", label: "Effective from", show: date },
    ],
  },
  catalogue: {
    title: "BU catalogue courses", note: "Courses that transfer credit can map to.",
    columns: [
      { key: "code", label: "Code" },
      { key: "title", label: "Title" },
      { key: "program", label: "Program" },
      { key: "requirementType", label: "Requirement" },
      { key: "credits", label: "Credits", num: true, show: units },
      { key: "verifiedStatus", label: "Catalogue", show: verified },
      { key: "mustBeTakenAtBu", label: "Program rule", show: (v) => (v ? <Badge tone="danger">Must be taken at BU</Badge> : "—") },
    ],
  },
  institutions: {
    title: "Source institutions", note: "Institutions students transfer from.",
    columns: [
      { key: "name", label: "Institution" },
      { key: "creditSystem", label: "Credit system" },
      { key: "courses", label: "Courses submitted", num: true, total: true },
      { key: "equivalencies", label: "Approved equivalencies", num: true, total: true },
      { key: "catalogueSource", label: "Catalogue source" },
    ],
  },
  documents: {
    title: "Documents received", note: "Documents by type, channel and virus-scan result. File contents are never shown here.",
    columns: [
      { key: "documentType", label: "Type" },
      { key: "source", label: "Channel", show: (v) => (v === "StaffUpload" ? "Staff upload" : text(v)) },
      { key: "scanStatus", label: "Scan", show: (v) => { const s = scanStatus[v as ScanStatus]; return <Badge tone={s.tone}>{s.label}</Badge>; } },
      { key: "documents", label: "Documents", num: true, total: true },
      { key: "sizeBytes", label: "Total size", num: true, total: true, show: (v) => formatBytes(Number(v)) },
    ],
  },
};

function TopicDetails({ topic, onClose }: { topic: Topic; onClose: () => void }) {
  const t = TOPICS[topic];
  const { data, error } = useLoad<Row[]>(`/api/overview/details/${topic}`);
  const totals = t.columns.some((c) => c.total) && data && data.length > 1;
  return (
    <DetailPanel title={t.title} note={t.note} onClose={onClose}>
      <ErrorAlert error={error} />
      {!data ? <Loading /> : data.length === 0 ? <div className="empty">Nothing recorded yet.</div> : (
        <div className="table-wrap">
          <table>
            <thead><tr>{t.columns.map((c) => <th key={c.key} className={c.num ? "num" : undefined}>{c.label}</th>)}</tr></thead>
            <tbody>
              {data.map((row, i) => (
                <tr key={i}>
                  {t.columns.map((c) => (
                    <td key={c.key} className={c.num ? "num" : undefined}>{(c.show ?? text)(row[c.key])}</td>
                  ))}
                </tr>
              ))}
            </tbody>
            {totals && (
              <tfoot>
                <tr>
                  {t.columns.map((c, i) => (
                    <td key={c.key} className={c.num ? "num" : undefined}>
                      {c.total ? (c.show ?? text)(data.reduce((n, r) => n + Number(r[c.key] ?? 0), 0)) : i === 0 ? "Total" : ""}
                    </td>
                  ))}
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}
    </DetailPanel>
  );
}

// Workflow order, so slices and bars read in the order a case or course moves through review.
const CASE_ORDER: CaseStatus[] = ["Open", "AwaitingDocuments", "InReview", "Decided", "Posted"];
const COURSE_ORDER: CourseStatus[] = ["Pending", "Matching", "NeedsSyllabus", "NeedsReview", "ApprovedMatchFound", "Blocked", "Decided"];
const OUTCOMES: OutcomeType[] = ["BuEquivalent", "ElectiveCredit", "NoCredit"];
const MATCH_LEVELS: [string, string][] = [
  ["LikelyEquivalent", "Likely equivalent"],
  ["PartialSyllabusReview", "Partial · syllabus review"],
  ["NoEquivalentFound", "No equivalent found"],
  ["MustBeTakenAtBu", "Must be taken at BU"],
];
const DOCUMENT_TYPES = ["Transcript", "Syllabus", "Other"];

/** Slices in a fixed order, coloured by slot so a category keeps its colour wherever it appears. */
const slices = (entries: [string, string][], counts: Record<string, number>): Segment[] =>
  entries.map(([key, label], i) => ({ key, label, value: counts[key] ?? 0, color: SERIES[i] }));

const shortDate = (iso: string) => new Date(`${iso}T00:00:00`).toLocaleDateString(undefined, { month: "short", day: "numeric" });

/** Whole-application statistics, shown before anyone picks a demo user. Aggregate counts only. */
export default function OverviewPage({ signedIn = false }: { signedIn?: boolean }) {
  const { data, error, reload } = useLoad<Overview>("/api/overview");

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Transfer credit at a glance</h1>
          <p className="muted">Every case, course and decision in the system, as aggregate counts. All data is synthetic.</p>
        </div>
        <button onClick={() => void reload()}>Refresh</button>
      </div>
      {!signedIn && (
        <div className="alert info">Pick a <strong>demo user</strong> in the sidebar to open cases, review courses or see a student’s view.</div>
      )}
      <ErrorAlert error={error} />
      {!data ? <Loading /> : <Charts o={data} />}
    </>
  );
}

function Charts({ o }: { o: Overview }) {
  const t = o.totals;
  const resolved = o.resolution.approvedRule + o.resolution.cache + o.resolution.newRun;
  const [topic, setTopic] = useState<Topic | null>(null);
  const tile = (key: Topic, value: ReactNode, label: string) => (
    <Stat value={value} label={label} active={topic === key} onClick={() => setTopic(topic === key ? null : key)} />
  );

  return (
    <div className="stack dash">
      <div className="grid-4">
        {tile("cases", t.cases, "Transfer cases")}
        {tile("courses", t.courses, "Courses submitted")}
        {tile("decisions", t.finalDecisions, `Final decisions (${pct(t.finalDecisions, t.courses)}% of courses)`)}
        {tile("units", units(t.awardedUnits), "Units awarded")}
        {tile("equivalencies", t.approvedEquivalencies, "Approved equivalencies")}
        {tile("catalogue", t.catalogueCourses, "BU catalogue courses")}
        {tile("institutions", t.institutions, "Source institutions")}
        {tile("documents", t.documents, "Documents received")}
      </div>
      {topic && <TopicDetails key={topic} topic={topic} onClose={() => setTopic(null)} />}

      <div className="viz-grid">
        <ChartCard title="Cases by status">
          <DonutChart unit="case" segments={slices(CASE_ORDER.map((s) => [s, caseStatus[s].label]), o.casesByStatus)} />
        </ChartCard>
        <ChartCard title="Courses by status" note="Every course on every case.">
          <BarChart unit="course" rows={COURSE_ORDER.map((s) => ({ key: s, label: courseStatus[s].label, value: o.coursesByStatus[s] ?? 0 }))} />
        </ChartCard>

        <ChartCard wide title="Activity in the last 14 days" note="New cases, documents received and final decisions per day (UTC).">
          <LineChart
            labels={o.activity.map((d) => shortDate(d.date))}
            series={[
              { key: "cases", label: "New cases", color: SERIES[0], values: o.activity.map((d) => d.cases) },
              { key: "documents", label: "Documents received", color: SERIES[1], values: o.activity.map((d) => d.documents) },
              { key: "decisions", label: "Final decisions", color: SERIES[2], values: o.activity.map((d) => d.decisions) },
            ]}
          />
        </ChartCard>

        <ChartCard title="Course match results" note="Best match level found for each submitted course.">
          <DonutChart unit="course" segments={[
            ...slices(MATCH_LEVELS, o.matchLevels),
            { key: "NotMatched", label: "Not matched yet", value: o.matchLevels.NotMatched ?? 0, color: "var(--viz-neutral)" },
          ]} />
        </ChartCard>
        <ChartCard title="How matches were resolved"
          note={`${pct(o.resolution.approvedRule + o.resolution.cache, resolved)}% resolved without calling the agent.` +
            (o.resolution.failed ? ` ${o.resolution.failed} agent run(s) failed and went to manual review.` : "")}>
          <DonutChart unit="match" empty="No matches run yet." segments={slices([
            ["approvedRule", "Approved equivalency"], ["cache", "Reused earlier suggestion"], ["newRun", "New agent run"],
          ], o.resolution)} />
        </ChartCard>

        <ChartCard title="Final decisions by outcome" note="Decisions made by the academic department.">
          <DonutChart unit="decision" empty="No final decisions yet."
            segments={slices(OUTCOMES.map((k) => [k, outcomeLabel[k]]), o.decisionOutcomes)} />
        </ChartCard>
        <ChartCard title="Documents by type">
          <DonutChart unit="document" empty="No documents received yet."
            segments={slices(DOCUMENT_TYPES.map((k) => [k, k]), o.documentsByType)} />
        </ChartCard>

        <ChartCard title="Courses by source institution" note="Top 8 institutions by courses submitted.">
          <BarChart unit="course" rows={o.coursesByInstitution.map((r) => ({ key: r.name, label: r.name, value: r.count }))} />
        </ChartCard>
        <ChartCard title="Cases by program">
          <BarChart unit="case" rows={o.casesByProgram.map((r) => ({ key: r.name, label: r.name, value: r.count }))} />
        </ChartCard>
      </div>
    </div>
  );
}
