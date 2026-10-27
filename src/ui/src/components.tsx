import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { api } from "./api";
import { formatBytes, formatDateTime, scanStatus, type Tone } from "./labels";
import type { DemoUser, DocumentInfo, DocumentType } from "./types";

// ---------- current demo user ----------
export const UserContext = createContext<DemoUser | null>(null);
export const useUser = () => useContext(UserContext);
export const isReviewer = (u: DemoUser | null) => !!u && ["Staff", "Evaluator", "Admin"].includes(u.role);
export const isStaff = (u: DemoUser | null) => !!u && ["Staff", "Admin"].includes(u.role);
export const isEvaluator = (u: DemoUser | null) => !!u && ["Evaluator", "Admin"].includes(u.role);

// ---------- data loading ----------
export function useLoad<T>(path: string | null, pollMs?: (data: T | null) => number | null) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const pollRef = useRef(pollMs);
  pollRef.current = pollMs;

  const reload = useCallback(async () => {
    if (!path) return;
    try {
      const result = await api.get<T>(path);
      setData(result);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [path]);

  useEffect(() => {
    setLoading(true);
    void reload();
  }, [reload]);

  // Poll while the caller says so (e.g. courses still matching, documents still scanning).
  useEffect(() => {
    const interval = pollRef.current?.(data) ?? null;
    if (!interval) return;
    const t = setTimeout(() => void reload(), interval);
    return () => clearTimeout(t);
  }, [data, reload]);

  return { data, error, loading, reload, setError };
}

// ---------- small pieces ----------
export function Badge({ tone, children, title }: { tone: Tone; children: ReactNode; title?: string }) {
  return <span className={`badge tone-${tone}`} title={title}>{children}</span>;
}

export function ErrorAlert({ error }: { error: string | null }) {
  return error ? <div className="alert error" role="alert">{error}</div> : null;
}

export function Loading({ what = "Loading" }: { what?: string }) {
  return <div className="empty">{what}…</div>;
}

export function Stat({ value, label, hint, onClick, active }: {
  value: ReactNode; label: string; hint?: string; onClick?: () => void; active?: boolean;
}) {
  if (!onClick) {
    return (
      <div className="card stat" title={hint}>
        <span className="value">{value}</span>
        <span className="label">{label}</span>
      </div>
    );
  }
  return (
    <div className={`card stat clickable${active ? " active" : ""}`} title={hint} role="button" tabIndex={0} aria-pressed={!!active}
      onClick={onClick} onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onClick(); } }}>
      <span className="value">{value}</span>
      <span className="label">{label}</span>
      <span className="stat-more">{active ? "Hide details" : "View details →"}</span>
    </div>
  );
}

/** The table a clicked tile opens, shown right under the tiles. */
export function DetailPanel({ title, note, onClose, children }: { title: string; note?: string; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => { ref.current?.scrollIntoView({ block: "nearest", behavior: "smooth" }); }, [title]);
  return (
    <div ref={ref} className="card detail-panel" role="region" aria-label={title}>
      <div className="row between">
        <div>
          <h2>{title}</h2>
          {note && <p className="muted small" style={{ margin: "-8px 0 0" }}>{note}</p>}
        </div>
        <button onClick={onClose} aria-label="Close details">Close</button>
      </div>
      {children}
    </div>
  );
}

// ---------- documents ----------
export function DocumentList({ documents, onError }: { documents: DocumentInfo[]; onError?: (m: string) => void }) {
  if (documents.length === 0) return <p className="muted">No documents yet.</p>;
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr><th>File</th><th>Type</th><th>Channel</th><th>Scan</th><th>Received</th><th /></tr>
        </thead>
        <tbody>
          {documents.map((d) => (
            <tr key={d.id}>
              <td>
                {d.fileName}
                <div className="muted small">{formatBytes(d.sizeBytes)} · sha256 {d.sha256.slice(0, 10)}…</div>
              </td>
              <td>{d.documentType}</td>
              <td>{d.source === "StaffUpload" ? "Staff upload" : d.source}</td>
              <td><Badge tone={scanStatus[d.scanStatus].tone}>{scanStatus[d.scanStatus].label}</Badge></td>
              <td className="small">{formatDateTime(d.uploadedAt)}</td>
              <td>
                {d.scanStatus === "Clean" && (
                  <button onClick={() => api.openDocument(d.id).catch((e) => onError?.(e.message))}>Open</button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function UploadPanel({
  caseId, courses, onUploaded, defaultType = "Transcript",
}: {
  caseId: number;
  courses?: { id: number; code: string }[];
  onUploaded: () => void;
  defaultType?: DocumentType;
}) {
  const [type, setType] = useState<DocumentType>(defaultType);
  const [courseId, setCourseId] = useState("");
  const [over, setOver] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: "error" | "info"; text: string } | null>(null);
  const input = useRef<HTMLInputElement>(null);

  async function upload(files: FileList | null) {
    if (!files || files.length === 0) return;
    setBusy(true);
    setMessage(null);
    const notes: string[] = [];
    for (const file of Array.from(files)) {
      const form = new FormData();
      form.append("file", file);
      form.append("documentType", type);
      if (type === "Syllabus" && courseId) form.append("sourceCourseId", courseId);
      try {
        const r = await api.post<{ duplicate: boolean }>(`/api/cases/${caseId}/documents`, form);
        notes.push(r.duplicate ? `${file.name}: already received, not stored again.` : `${file.name}: received, scanning.`);
      } catch (e) {
        setMessage({ kind: "error", text: (e as Error).message });
      }
    }
    if (notes.length) setMessage((m) => m ?? { kind: "info", text: notes.join(" ") });
    setBusy(false);
    if (input.current) input.current.value = "";
    onUploaded();
  }

  return (
    <div className="stack">
      <div className="form-grid">
        <label className="field">
          Document type
          <select value={type} onChange={(e) => setType(e.target.value as DocumentType)}>
            <option>Transcript</option>
            <option>Syllabus</option>
            <option>Other</option>
          </select>
        </label>
        {type === "Syllabus" && courses && courses.length > 0 && (
          <label className="field">
            For course
            <select value={courseId} onChange={(e) => setCourseId(e.target.value)}>
              <option value="">Not specified</option>
              {courses.map((c) => <option key={c.id} value={c.id}>{c.code}</option>)}
            </select>
          </label>
        )}
      </div>
      <div
        className={`dropzone ${over ? "over" : ""}`}
        onDragOver={(e) => { e.preventDefault(); setOver(true); }}
        onDragLeave={() => setOver(false)}
        onDrop={(e) => { e.preventDefault(); setOver(false); void upload(e.dataTransfer.files); }}
      >
        <p>Drag files here, or</p>
        <input ref={input} type="file" multiple accept=".pdf,.docx,.png,.jpg,.jpeg" disabled={busy}
          onChange={(e) => void upload(e.target.files)} aria-label="Choose files" />
        <p className="small">PDF, Word, PNG or JPG · up to 20 MB · every file is scanned before use</p>
      </div>
      {message && <div className={`alert ${message.kind}`}>{message.text}</div>}
    </div>
  );
}

// ---------- paging ----------
/** Page numbers to show: always the first and last, plus a window around the current page. */
function pageList(page: number, pages: number): (number | "…")[] {
  const keep = new Set([1, pages, page - 1, page, page + 1].filter((p) => p >= 1 && p <= pages));
  const sorted = [...keep].sort((a, b) => a - b);
  // A gap of one page shows that page; only longer gaps collapse to "…".
  return sorted.flatMap((p, i) => {
    const gap = i > 0 ? p - sorted[i - 1] : 1;
    return gap === 2 ? [p - 1, p] : gap > 2 ? ["…" as const, p] : [p];
  });
}

export function Pager({ page, pageSize, total, unit, onPage, onPageSize, sizes = [10, 25, 50] }: {
  page: number; pageSize: number; total: number; unit: string;
  onPage: (page: number) => void; onPageSize?: (size: number) => void; sizes?: number[];
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const first = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(total, page * pageSize);
  return (
    <nav className="pager" aria-label={`${unit} pages`}>
      <span className="muted small">Showing {first}–{last} of {total} {unit}</span>
      <div className="row">
        {onPageSize && (
          <label className="row small muted">
            Rows per page
            <select value={pageSize} onChange={(e) => onPageSize(Number(e.target.value))}>
              {sizes.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </label>
        )}
        <button onClick={() => onPage(page - 1)} disabled={page <= 1} aria-label="Previous page">‹ Prev</button>
        {pageList(page, pages).map((p, i) => p === "…"
          ? <span key={`gap-${i}`} className="muted pager-gap">…</span>
          : <button key={p} className={p === page ? "pager-current" : undefined} aria-current={p === page ? "page" : undefined}
              onClick={() => onPage(p)}>{p}</button>)}
        <button onClick={() => onPage(page + 1)} disabled={page >= pages} aria-label="Next page">Next ›</button>
      </div>
    </nav>
  );
}
