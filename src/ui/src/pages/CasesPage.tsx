import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../api";
import { Badge, ErrorAlert, isReviewer, isStaff, Loading, useLoad, useUser } from "../components";
import { caseStatus, deadlineText, formatDate } from "../labels";
import type { CaseStatus, CaseSummary } from "../types";

export default function CasesPage() {
  const user = useUser();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  // The dashboard links here with ?status=… to show one status.
  const [status, setStatus] = useState<CaseStatus | "">(() => {
    const s = searchParams.get("status");
    return s && s in caseStatus ? (s as CaseStatus) : "";
  });
  const [q, setQ] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const params = new URLSearchParams();
  if (status) params.set("status", status);
  if (q) params.set("q", q);
  const { data, error, loading, reload, setError } = useLoad<CaseSummary[]>(`/api/cases?${params}`);

  async function createDemo(studentRef: string) {
    try {
      const r = await api.post<{ id: number }>("/api/demo/cases", { studentRef });
      navigate(`/cases/${r.id}`);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  const open = (c: CaseSummary) => navigate(isReviewer(user) ? `/cases/${c.id}` : `/cases/${c.id}/status`);

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Transfer credit cases</h1>
          <p className="muted">Sorted by transfer deadline. Pending credit counts as zero units until a final decision.</p>
        </div>
        {isStaff(user) && (
          <div className="row">
            <button onClick={() => createDemo("DS-0001")}>New six-course demo case (Student A)</button>
            <button onClick={() => createDemo("DS-0002")}>Same courses for Student B</button>
            <button className="primary" onClick={() => setShowCreate((s) => !s)}>New case</button>
          </div>
        )}
      </div>

      <ErrorAlert error={error} />
      {showCreate && <CreateCase onCreated={(id) => navigate(`/cases/${id}`)} onCancel={() => setShowCreate(false)} />}

      <div className="card stack">
        <div className="row">
          <input placeholder="Search case, student or reference" value={q} onChange={(e) => setQ(e.target.value)}
            aria-label="Search cases" style={{ minWidth: 260 }} />
          <select value={status} onChange={(e) => setStatus(e.target.value as CaseStatus | "")} aria-label="Status filter">
            <option value="">All statuses</option>
            {Object.entries(caseStatus).map(([k, v]) => <option key={k} value={k}>{v.label}</option>)}
          </select>
          <button onClick={() => void reload()}>Refresh</button>
        </div>
        {loading && !data ? <Loading /> : data && data.length === 0 ? (
          <div className="empty">No cases match.</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Case</th><th>Student (synthetic)</th><th>Program</th><th>Status</th>
                  <th className="num">Open courses</th><th>Deadline</th>
                </tr>
              </thead>
              <tbody>
                {data?.map((c) => (
                  <tr key={c.id} className="clickable" onClick={() => open(c)}>
                    <td><a href={`/cases/${c.id}`} onClick={(e) => { e.preventDefault(); open(c); }}>{c.caseRef}</a></td>
                    <td>{c.studentName}<div className="muted small">{c.studentRef}</div></td>
                    <td>{c.program}</td>
                    <td><Badge tone={caseStatus[c.status].tone}>{caseStatus[c.status].label}</Badge></td>
                    <td className="num">{c.openCourseCount} / {c.courseCount}</td>
                    <td>
                      {formatDate(c.deadlineDate)}
                      <div className={`small ${c.overdue ? "" : "muted"}`}>
                        {c.overdue ? <Badge tone="danger">{deadlineText(c.daysRemaining, true)}</Badge> : deadlineText(c.daysRemaining, false)}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}

function CreateCase({ onCreated, onCancel }: { onCreated: (id: number) => void; onCancel: () => void }) {
  const [form, setForm] = useState({ studentRef: "", studentName: "", studentEmail: "", program: "Computer Science", startDate: "" });
  const [error, setError] = useState<string | null>(null);
  const programs = useLoad<string[]>("/api/catalogue/programs");
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value });

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const c = await api.post<CaseSummary>("/api/cases", { ...form, studentEmail: form.studentEmail || null });
      onCreated(c.id);
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <form className="card stack" onSubmit={submit} style={{ marginBottom: 16 }}>
      <h2>New case</h2>
      <p className="muted small">Use synthetic names and references only (BR-13).</p>
      <ErrorAlert error={error} />
      <div className="form-grid">
        <label className="field">Student reference<input required value={form.studentRef} onChange={set("studentRef")} placeholder="DS-0003" /></label>
        <label className="field">Student name<input required value={form.studentName} onChange={set("studentName")} placeholder="Demo Student C" /></label>
        <label className="field">Contact email<input type="email" value={form.studentEmail} onChange={set("studentEmail")} /></label>
        <label className="field">Program
          <select value={form.program} onChange={set("program")}>
            {(programs.data ?? ["Computer Science"]).map((p) => <option key={p}>{p}</option>)}
          </select>
        </label>
        <label className="field">BU start date<input required type="date" value={form.startDate} onChange={set("startDate")} /></label>
      </div>
      <div className="row">
        <button className="primary" type="submit">Create case</button>
        <button type="button" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
