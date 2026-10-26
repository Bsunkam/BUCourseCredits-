import { useState } from "react";
import { ErrorAlert, Loading, useLoad, useUser } from "../components";
import type { CaseSummary } from "../types";
import { StatusView } from "./StatusPage";

export default function StudentPage() {
  const user = useUser();
  const { data, error } = useLoad<CaseSummary[]>("/api/me/cases");
  const [selected, setSelected] = useState<number | null>(null);

  if (!data) return error ? <ErrorAlert error={error} /> : <Loading />;
  const caseId = selected ?? data[0]?.id;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>My transfer credit</h1>
          <p className="muted">Signed in as {user?.displayName}. Pending courses count as zero units until the department decides.</p>
        </div>
        {data.length > 1 && (
          <select value={caseId} onChange={(e) => setSelected(Number(e.target.value))} aria-label="Choose case">
            {data.map((c) => <option key={c.id} value={c.id}>{c.caseRef} · {c.program}</option>)}
          </select>
        )}
      </div>
      {caseId ? <StatusView key={caseId} caseId={caseId} allowUpload /> : (
        <div className="card empty">No transfer credit case yet. Credit Evaluation will open one when your transcript arrives.</div>
      )}
    </>
  );
}
