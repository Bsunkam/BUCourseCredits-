import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api";
import { Badge, DocumentList, ErrorAlert, isStaff, Loading, UploadPanel, useLoad, useUser } from "../components";
import {
  candidateSource, caseStatus, courseStatus, deadlineText, EQUIVALENCY_DB, formatDate, formatDateTime, outcomeLabel, units,
} from "../labels";
import type { CaseDetail, Course, Institution } from "../types";

export default function CaseDetailPage() {
  const { id } = useParams();
  const user = useUser();
  const navigate = useNavigate();
  const [showImport, setShowImport] = useState(false);
  const { data, error, reload, setError } = useLoad<CaseDetail>(`/api/cases/${id}`, (d) =>
    d && (d.courses.some((c) => c.status === "Matching") || d.documents.some((x) => x.scanStatus === "Pending")) ? 1500 : null,
  );

  if (!data) return error ? <ErrorAlert error={error} /> : <Loading />;
  const { summary } = data;
  const allDecided = data.courses.length > 0 && data.courses.every((c) => c.status === "Decided");
  const matchable = data.courses.filter((c) => c.status !== "Decided" && c.status !== "Matching").length;

  async function act(fn: () => Promise<unknown>) {
    try {
      await fn();
      await reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <div className="muted small"><Link to="/cases">Cases</Link> / {summary.caseRef}</div>
          <h1>{summary.caseRef} · {summary.studentName}</h1>
          <div className="row">
            <Badge tone={caseStatus[summary.status].tone}>{caseStatus[summary.status].label}</Badge>
            <span className="muted">{summary.program}</span>
            <span className="muted">· starts {formatDate(summary.startDate)}</span>
            <span>· deadline {formatDate(summary.deadlineDate)}</span>
            <Badge tone={summary.overdue ? "danger" : summary.daysRemaining < 60 ? "warn" : "neutral"}>
              {deadlineText(summary.daysRemaining, summary.overdue)}
            </Badge>
          </div>
        </div>
        <div className="row">
          <button onClick={() => navigate(`/cases/${id}/status`)}>Student view</button>
          {matchable > 0 && (
            <button className="primary" onClick={() => act(() => api.post(`/api/cases/${id}/match`))}>
              Run matching ({matchable})
            </button>
          )}
          {isStaff(user) && (
            <button disabled={!allDecided || summary.status === "Posted"}
              title={allDecided ? "Simulated: nothing is written to MyBU Student" : "Every course needs a final decision first"}
              onClick={() => act(() => api.post(`/api/cases/${id}/post`))}>
              Post results (simulated)
            </button>
          )}
        </div>
      </div>

      <ErrorAlert error={error} />

      <section className="card stack" style={{ marginBottom: 16 }}>
        <div className="row between">
          <h2>Courses</h2>
          {isStaff(user) && <button onClick={() => setShowImport((s) => !s)}>{showImport ? "Close" : "Add course"}</button>}
        </div>
        {showImport && <ImportCourse caseId={summary.id} onDone={() => { setShowImport(false); void reload(); }} />}
        <CourseTable courses={data.courses} onOpen={(c) => navigate(`/courses/${c.id}`)} />
      </section>

      <div className="grid-2">
        <section className="card stack">
          <h2>Documents</h2>
          <UploadPanel caseId={summary.id} courses={data.courses} onUploaded={() => void reload()} />
          <DocumentList documents={data.documents} onError={setError} />
        </section>
        <section className="card">
          <h2>Audit trail</h2>
          <ul className="timeline">
            {data.timeline.map((a) => (
              <li key={a.id}>
                <time>{formatDateTime(a.timestamp)}</time>
                <div>
                  <strong>{a.action}</strong> <span className="muted small">by {a.actorId}</span>
                  {a.detail && <div className="small">{a.detail}</div>}
                </div>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </>
  );
}

function CourseTable({ courses, onOpen }: { courses: Course[]; onOpen: (c: Course) => void }) {
  if (courses.length === 0) return <div className="empty">No courses yet. Add them from the transcript.</div>;
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Source course</th><th>Grade</th><th className="num">Credits</th><th>Match level</th>
            <th>Suggestion / outcome</th><th>Status</th><th>Posted</th><th />
          </tr>
        </thead>
        <tbody>
          {courses.map((c) => {
            const s = courseStatus[c.status];
            const final = c.evaluation?.status === "Final" ? c.evaluation : null;
            const applied = final?.evaluatorId === EQUIVALENCY_DB;
            return (
              <tr key={c.id} className="clickable" onClick={() => onOpen(c)}>
                <td>
                  <strong>{c.code}</strong> {c.title}
                  <div className="muted small">{c.institution}{c.term ? ` · ${c.term}` : ""}{c.hasSyllabus ? " · syllabus on file" : ""}</div>
                </td>
                <td>{c.grade ?? "—"}</td>
                <td className="num">
                  {units(c.convertedCredits)}
                  {c.creditSystem === "Quarter" && <div className="muted small">{c.credits} quarter</div>}
                </td>
                <td>{c.matchLevel ?? <span className="muted">—</span>}</td>
                <td>
                  {final ? (
                    <span>
                      {outcomeLabel[final.outcomeType]} {final.buCourse?.code} · {units(final.awardedUnits)} units
                      <div className="muted small">{applied ? "Applied from equivalency database · no new review" : `Decided by ${final.evaluatorId}`}</div>
                    </span>
                  ) : c.topCandidate ? (
                    <span>
                      {c.topCandidate.buCourse.code}
                      <div className="muted small">{candidateSource[c.topCandidate.source]}</div>
                    </span>
                  ) : <span className="muted">—</span>}
                </td>
                <td><Badge tone={s.tone}>{s.label}</Badge></td>
                <td title="Transfer credit is posted with grade T, 0 Hub units, not counted in BU GPA">
                  {c.postedGrade ? <strong>Grade {c.postedGrade}</strong> : <span className="muted">—</span>}
                </td>
                <td><button onClick={(e) => { e.stopPropagation(); onOpen(c); }}>Review</button></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function ImportCourse({ caseId, onDone }: { caseId: number; onDone: () => void }) {
  const institutions = useLoad<Institution[]>("/api/institutions");
  const [error, setError] = useState<string | null>(null);
  const [f, setF] = useState({
    institutionId: "", code: "", title: "", credits: "4", grade: "", term: "", description: "", learningOutcomes: "", syllabusText: "",
  });
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value });

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await api.post(`/api/cases/${caseId}/courses/import`, {
        courses: [{
          institutionId: Number(f.institutionId), code: f.code, title: f.title, credits: Number(f.credits),
          grade: f.grade || null, term: f.term || null, description: f.description || null,
          learningOutcomes: f.learningOutcomes || null, syllabusText: f.syllabusText || null,
        }],
      });
      onDone();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  const institution = institutions.data?.find((i) => String(i.id) === f.institutionId);
  return (
    <form className="stack" onSubmit={submit}>
      <ErrorAlert error={error} />
      <div className="form-grid">
        <label className="field">Institution
          <select required value={f.institutionId} onChange={set("institutionId")}>
            <option value="">Choose…</option>
            {institutions.data?.map((i) => <option key={i.id} value={i.id}>{i.name} ({i.creditSystem})</option>)}
          </select>
        </label>
        <label className="field">Code<input required value={f.code} onChange={set("code")} placeholder="CS 2510" /></label>
        <label className="field">Title<input required value={f.title} onChange={set("title")} /></label>
        <label className="field">Credits{institution?.creditSystem === "Quarter" ? " (quarter)" : ""}
          <input required type="number" min="0.5" max="20" step="0.5" value={f.credits} onChange={set("credits")} /></label>
        <label className="field">Grade<input value={f.grade} onChange={set("grade")} placeholder="B+" /></label>
        <label className="field">Term<input value={f.term} onChange={set("term")} placeholder="Fall 2025" /></label>
      </div>
      <p className="muted small">Leave description and outcomes blank to fill them from the institution's public catalogue, when it lists the course.</p>
      <label className="field">Catalogue description<textarea rows={2} value={f.description} onChange={set("description")} /></label>
      <label className="field">Learning outcomes<textarea rows={2} value={f.learningOutcomes} onChange={set("learningOutcomes")} /></label>
      <label className="field">Syllabus text (optional; treated as untrusted evidence)
        <textarea rows={3} value={f.syllabusText} onChange={set("syllabusText")} /></label>
      <div><button className="primary" type="submit">Add course</button></div>
    </form>
  );
}
