import { Link, useParams } from "react-router-dom";
import { Badge, DocumentList, ErrorAlert, isReviewer, Loading, Stat, UploadPanel, useLoad, useUser } from "../components";
import { caseStatus, deadlineText, formatDate, formatDateTime } from "../labels";
import type { StudentStatus } from "../types";

/** Student-safe case status: no candidates, reviewer notes or rationale (least privilege). */
export function StatusView({ caseId, allowUpload }: { caseId: number; allowUpload: boolean }) {
  const { data, error, reload, setError } = useLoad<StudentStatus>(`/api/cases/${caseId}/status`, (d) =>
    d?.documents.some((x) => x.scanStatus === "Pending") ? 2000 : null,
  );
  if (!data) return error ? <ErrorAlert error={error} /> : <Loading />;

  const pending = data.courses.filter((c) => c.state !== "Decided").length;
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
        <div className="grid-3">
          <Stat value={data.awardedUnits} label="Units awarded (final decisions only)" />
          <Stat value={pending} label="Courses still pending (count as 0 units)" />
          <Stat value={deadlineText(data.daysRemaining, data.overdue)} label={`Transfer deadline ${formatDate(data.deadlineDate)}`} />
        </div>
      </div>

      <div className="card">
        <h2>Your courses</h2>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Course</th><th>Status</th><th>Outcome</th><th className="num">Units</th></tr></thead>
            <tbody>
              {data.courses.map((c) => (
                <tr key={c.id}>
                  <td><strong>{c.code}</strong> {c.title}<div className="muted small">{c.institution}</div></td>
                  <td>
                    <Badge tone={c.state === "Decided" ? "success" : c.syllabusNeeded ? "warn" : "neutral"}>{c.state}</Badge>
                  </td>
                  <td>{c.outcome ?? <span className="muted">Pending</span>}</td>
                  <td className="num">{c.state === "Decided" ? c.awardedUnits : "0 (pending)"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="grid-2">
        <div className="card stack">
          <h2>Documents</h2>
          {allowUpload && <UploadPanel caseId={caseId} onUploaded={() => void reload()} />}
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
