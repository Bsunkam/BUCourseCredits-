import { useState } from "react";
import { api } from "../api";
import { Badge, DocumentList, ErrorAlert, Loading, useLoad } from "../components";
import { formatDateTime } from "../labels";
import type { CaseSummary, IntakeMessage } from "../types";

interface ImportResult { intakeMessageId: number; matchStatus: string; duplicate: boolean; accepted: number; rejected: string[] }

export default function IntakePage() {
  const { data, error, reload, setError } = useLoad<IntakeMessage[]>("/api/intake");
  const cases = useLoad<CaseSummary[]>("/api/cases");
  const [result, setResult] = useState<string | null>(null);

  async function importEml(files: FileList | null) {
    if (!files?.[0]) return;
    const form = new FormData();
    form.append("file", files[0]);
    try {
      const r = await api.post<ImportResult>("/api/intake/import-eml", form);
      setResult(r.duplicate
        ? "This email was already processed; nothing was imported again."
        : `Email #${r.intakeMessageId}: ${r.matchStatus}. ${r.accepted} file(s) accepted${r.rejected.length ? `, rejected: ${r.rejected.join("; ")}` : ""}.`);
      await reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function assign(messageId: number, caseId: string) {
    if (!caseId) return;
    try {
      await api.post(`/api/intake/${messageId}/assign`, { caseId: Number(caseId) });
      setResult(`Email #${messageId} assigned.`);
      await reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Email triage</h1>
          <p className="muted">
            Emails without exactly one case reference or a known sender land here. Doubtful matches are never auto-assigned.
          </p>
        </div>
      </div>
      <ErrorAlert error={error} />
      {result && <div className="alert info">{result}</div>}

      <div className="card stack" style={{ marginBottom: 16 }}>
        <h2>Email simulator</h2>
        <p className="muted small">
          Import an .eml file through the same intake pipeline as the live mailbox (validate, de-duplicate, quarantine, scan,
          match). The email body is never stored or sent to AI.
        </p>
        <input type="file" accept=".eml" onChange={(e) => void importEml(e.target.files)} aria-label="Import .eml file" />
      </div>

      <div className="card stack">
        <h2>Unmatched and ambiguous emails</h2>
        {!data ? <Loading /> : data.length === 0 ? <div className="empty">Triage queue is empty.</div> : data.map((m) => (
          <div key={m.id} className="candidate">
            <div className="row between">
              <div>
                <strong>{m.subject || "(no subject)"}</strong>
                <div className="muted small">#{m.id} · from {m.senderAddress} · {formatDateTime(m.receivedAt)} · {m.channel}</div>
              </div>
              <Badge tone={m.matchStatus === "Ambiguous" ? "warn" : "neutral"}>{m.matchStatus}</Badge>
            </div>
            <DocumentList documents={m.documents} onError={setError} />
            <div className="row">
              <label className="row small">Assign to case
                <select defaultValue="" onChange={(e) => void assign(m.id, e.target.value)}>
                  <option value="">Choose…</option>
                  {cases.data?.map((c) => <option key={c.id} value={c.id}>{c.caseRef} · {c.studentName}</option>)}
                </select>
              </label>
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
