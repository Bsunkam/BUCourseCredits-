import { Link, useParams } from "react-router-dom";
import { Badge, DocumentList, ErrorAlert, isReviewer, Loading, Stat, UploadPanel, useLoad, useUser } from "../components";
import { caseStatus, deadlineText, formatDate, formatDateTime, standingTone, units } from "../labels";
import type { DegreeProgress, StudentCourse, StudentStatus } from "../types";

/** One bar split into secured, pending and at-risk units, so the student sees at a glance how much is safe. */
function ProgressBar({ progress }: { progress: DegreeProgress }) {
  const total = progress.securedUnits + progress.pendingUnits + progress.atRiskUnits;
  if (total === 0) return null;
  const pct = (n: number) => `${(n / total) * 100}%`;
  return (
    <div className="progress-bar" role="img"
      aria-label={`${units(progress.securedUnits)} units secured, ${units(progress.pendingUnits)} pending review, ${units(progress.atRiskUnits)} at risk`}>
      <span className="seg tone-success" style={{ width: pct(progress.securedUnits) }} />
      <span className="seg tone-neutral" style={{ width: pct(progress.pendingUnits) }} />
      <span className="seg tone-warn" style={{ width: pct(progress.atRiskUnits) }} />
    </div>
  );
}

/** MyBU Student-style record of what was posted: transfer courses with grade T (simulated, step 7). */
function PostedRecord({ courses, postedAt }: { courses: StudentCourse[]; postedAt?: string | null }) {
  const posted = courses.filter((c) => c.postedGrade);
  return (
    <div className="card">
      <h2>Posted transfer record <Badge tone="info">Demo simulation · not written to MyBU Student</Badge></h2>
      {postedAt && <p className="muted small">Posted {formatDateTime(postedAt)}</p>}
      <div className="table-wrap">
        <table>
          <thead>
            <tr><th>Transfer course</th><th>BU credit</th><th className="num">Units</th><th>Grade</th><th className="num">Hub units</th></tr>
          </thead>
          <tbody>
            {posted.map((c) => (
              <tr key={c.id}>
                <td><strong>{c.code}</strong> {c.title}<div className="muted small">{c.institution}</div></td>
                <td>{c.outcome}</td>
                <td className="num">{units(c.awardedUnits)}</td>
                <td><strong>{c.postedGrade}</strong></td>
                <td className="num">{c.hubUnits}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="muted small">Grade T marks transfer credit and does not count toward your BU GPA. Courses with no credit are not posted.</p>
    </div>
  );
}

/** Student-safe case status: no candidates, reviewer notes or rationale (least privilege). */
export function StatusView({ caseId, allowUpload }: { caseId: number; allowUpload: boolean }) {
  const { data, error, reload, setError } = useLoad<StudentStatus>(`/api/cases/${caseId}/status`, (d) =>
    d?.documents.some((x) => x.scanStatus === "Pending") ? 2000 : null,
  );
  if (!data) return error ? <ErrorAlert error={error} /> : <Loading />;

  const { progress } = data;
  const needSyllabus = data.courses.filter((c) => c.syllabusNeeded);
  return (
    <div className="stack">
      <ErrorAlert error={error} />
      <div className="card stack">
        <div className="row between">
          <div>
            <h2 style={{ marginBottom: 4 }}>{data.caseRef} · {data.program}</h2>
            <Badge tone={caseStatus[data.status].tone}>{caseStatus[data.status].label}</Badge>
          </div>
          <div className="small muted">Started {formatDate(data.startDate)}</div>
        </div>
        <div className={`alert ${data.nextAction.startsWith("Send") || data.nextAction.startsWith("Upload") ? "warn" : "info"}`}>
          <strong>Next step:</strong> {data.nextAction}
        </div>
        <div className="grid-4">
          <Stat value={units(progress.securedUnits)} label="Units secured (final decisions)" />
          <Stat value={units(progress.pendingUnits)} label="Units under review (not awarded yet)" />
          <Stat value={units(progress.atRiskUnits)} label="Units at risk" />
          <Stat value={deadlineText(data.daysRemaining, data.overdue)} label={`Transfer deadline ${formatDate(data.deadlineDate)}`} />
        </div>
        <ProgressBar progress={progress} />
        <div className="row small muted">
          <span><Badge tone="success">Secured</Badge> decided and final</span>
          <span><Badge tone="neutral">Pending review</Badge> with a department evaluator</span>
          <span><Badge tone="warn">At risk</Badge> needs your action, or may not transfer</span>
        </div>
      </div>

      {needSyllabus.length > 0 && (
        <div className="card stack">
          <h2>Syllabus requests ({needSyllabus.length})</h2>
          {needSyllabus.map((c) => (
            <div key={c.id} className="alert warn">
              <strong>{c.code} {c.title}</strong> ({units(c.potentialUnits)} potential units)
              <div className="small">{c.syllabusReason}</div>
            </div>
          ))}
          {allowUpload && <p className="small muted">Upload it below and choose the course it is for, so it reaches the right evaluator.</p>}
        </div>
      )}

      <div className="card">
        <h2>Your courses</h2>
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Course</th><th>Standing</th><th>Outcome</th><th>Counts toward</th><th className="num">Units awarded</th><th className="num">Potential units</th></tr>
            </thead>
            <tbody>
              {data.courses.map((c) => (
                <tr key={c.id}>
                  <td><strong>{c.code}</strong> {c.title}<div className="muted small">{c.institution}</div></td>
                  <td>
                    <Badge tone={standingTone[c.standing]}>{c.standing}</Badge>
                    {c.standingReason && <div className="muted small">{c.standingReason}</div>}
                  </td>
                  <td>{c.outcome ?? <span className="muted">{c.state}</span>}</td>
                  <td className="small">{c.requirement ?? <span className="muted">Not decided yet</span>}</td>
                  <td className="num">{c.state === "Decided" ? units(c.awardedUnits) : "0 (pending)"}</td>
                  <td className="num muted">{c.state === "Decided" ? "" : units(c.potentialUnits)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="grid-2">
        <div className="card">
          <h2>Secured units by requirement</h2>
          {progress.securedByRequirement.length === 0
            ? <p className="muted">No units are secured yet.</p>
            : (
              <table>
                <tbody>
                  {progress.securedByRequirement.map((r) => (
                    <tr key={r.requirement}>
                      <td>{r.requirement}</td>
                      <td className="num">{units(r.units)} units</td>
                      <td className="num muted small">{r.courses} course{r.courses === 1 ? "" : "s"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
        </div>
        <div className="card">
          <h2>Good to know</h2>
          <ul className="small">
            {data.policyNotes.map((n) => <li key={n}>{n}</li>)}
          </ul>
        </div>
      </div>

      {data.status === "Posted" && <PostedRecord courses={data.courses} postedAt={data.postedAt} />}

      <div className="grid-2">
        <div className="card stack">
          <h2>Documents</h2>
          {allowUpload && (
            <UploadPanel caseId={caseId} onUploaded={() => void reload()}
              courses={data.courses.filter((c) => c.state !== "Decided").map((c) => ({ id: c.id, code: c.code }))}
              defaultType={needSyllabus.length > 0 ? "Syllabus" : undefined} />
          )}
          <p className="small muted">You can also email documents to the transfer intake mailbox with {data.caseRef} in the subject.</p>
          <DocumentList documents={data.documents} onError={setError} />
        </div>
        <div className="card">
          <h2>Timeline</h2>
          <ul className="timeline">
            {data.timeline.map((t, i) => (
              <li key={i}><time>{formatDateTime(t.timestamp)}</time><span>{t.message}</span></li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}

export default function StatusPage() {
  const { id } = useParams();
  const user = useUser();
  return (
    <>
      <div className="page-head">
        <div>
          <div className="muted small">
            <Link to="/cases">Cases</Link>{isReviewer(user) && <> / <Link to={`/cases/${id}`}>Case detail</Link></>} / Student view
          </div>
          <h1>Case status (student view)</h1>
          <p className="muted">What the student sees: status, next action and deadline, with no internal notes.</p>
        </div>
      </div>
      <StatusView caseId={Number(id)} allowUpload={false} />
    </>
  );
}
