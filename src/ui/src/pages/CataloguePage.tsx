import { useState } from "react";
import { api } from "../api";
import { Badge, ErrorAlert, Loading, useLoad, useUser } from "../components";
import { formatDate, outcomeLabel } from "../labels";
import type { BuCourse, OutcomeType } from "../types";

interface EquivalencyRow {
  id: number; institution: string; sourceCode: string; sourceTitle: string; program: string; outcomeType: OutcomeType;
  buCourse?: BuCourse | null; verifiedStatus: "Yes" | "Check"; effectiveFrom: string; decisionSource: string; fromEvaluation: boolean;
}
interface ProgramRuleRow { id: number; program: string; buCourse: string; title: string; ruleType: string; source: string }
interface Setting { key: string; value: string; description?: string | null }

export default function CataloguePage() {
  const user = useUser();
  const [program, setProgram] = useState("Computer Science");
  const [q, setQ] = useState("");
  const programs = useLoad<string[]>("/api/catalogue/programs");
  const courses = useLoad<BuCourse[]>(`/api/catalogue/search?program=${encodeURIComponent(program)}&q=${encodeURIComponent(q)}`);
  const equivalencies = useLoad<EquivalencyRow[]>(`/api/catalogue/equivalencies?program=${encodeURIComponent(program)}`);
  const rules = useLoad<ProgramRuleRow[]>("/api/catalogue/program-rules");

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Catalogue & rules</h1>
          <p className="muted">Reference data seeded from the course-match workbook. Records marked “check” need confirmation before use.</p>
        </div>
        <select value={program} onChange={(e) => setProgram(e.target.value)} aria-label="Program">
          {(programs.data ?? [program]).map((p) => <option key={p}>{p}</option>)}
        </select>
      </div>

      <div className="stack">
        <section className="card stack">
          <div className="row between">
            <h2>BU courses · {program}</h2>
            <input placeholder="Search" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search catalogue" />
          </div>
          <ErrorAlert error={courses.error} />
          {!courses.data ? <Loading /> : (
            <div className="table-wrap">
              <table>
                <thead><tr><th>Code</th><th>Title</th><th className="num">Credits</th><th>Requirement</th><th>Verified</th><th>Rule</th></tr></thead>
                <tbody>
                  {courses.data.map((c) => (
                    <tr key={c.id}>
                      <td>{c.sourceUrl ? <a href={c.sourceUrl} target="_blank" rel="noreferrer">{c.code}</a> : c.code}</td>
                      <td>{c.title}</td>
                      <td className="num">{c.credits}</td>
                      <td>{c.requirementType}</td>
                      <td><Badge tone={c.verifiedStatus === "Yes" ? "success" : "warn"}>{c.verifiedStatus === "Yes" ? "Yes" : "Check"}</Badge></td>
                      <td>{c.buOnlyRule ? <Badge tone="danger" title={c.buOnlyRule}>Must be taken at BU</Badge> : ""}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="card stack">
          <h2>Approved equivalencies · {program}</h2>
          <p className="muted small">Includes evaluator decisions saved for reuse; these resolve future identical courses without an AI call.</p>
          {!equivalencies.data ? <Loading /> : (
            <div className="table-wrap">
              <table>
                <thead><tr><th>Institution</th><th>Source course</th><th>Outcome</th><th>Verified</th><th>Since</th><th>Source</th></tr></thead>
                <tbody>
                  {equivalencies.data.map((r) => (
                    <tr key={r.id}>
                      <td>{r.institution}</td>
                      <td><strong>{r.sourceCode}</strong> {r.sourceTitle}</td>
                      <td>{outcomeLabel[r.outcomeType]} {r.buCourse?.code}</td>
                      <td><Badge tone={r.verifiedStatus === "Yes" ? "success" : "warn"}>{r.verifiedStatus}</Badge></td>
                      <td>{formatDate(r.effectiveFrom)}</td>
                      <td className="small">{r.fromEvaluation ? <Badge tone="accent">Evaluator decision</Badge> : r.decisionSource}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="card stack">
          <h2>Program rules</h2>
          {!rules.data ? <Loading /> : (
            <ul>
              {rules.data.map((r) => <li key={r.id}><strong>{r.buCourse}</strong> {r.title} · {r.program}: {r.source}</li>)}
            </ul>
          )}
        </section>

        {user?.role === "Admin" && <SettingsPanel />}
      </div>
    </>
  );
}

function SettingsPanel() {
  const { data, error, reload, setError } = useLoad<Setting[]>("/api/admin/settings");
  const [edits, setEdits] = useState<Record<string, string>>({});

  async function save(key: string) {
    try {
      await api.put(`/api/admin/settings/${encodeURIComponent(key)}`, { value: edits[key] });
      setEdits((e) => { const n = { ...e }; delete n[key]; return n; });
      await reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <section className="card stack">
      <h2>Rule settings (admin)</h2>
      <p className="muted small">Bump RulesVersion whenever a rule value changes, so cached results are re-evaluated.</p>
      <ErrorAlert error={error} />
      {!data ? <Loading /> : (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Key</th><th>Value</th><th>Description</th><th /></tr></thead>
            <tbody>
              {data.map((s) => (
                <tr key={s.key}>
                  <td className="mono">{s.key}</td>
                  <td><input value={edits[s.key] ?? s.value} onChange={(e) => setEdits({ ...edits, [s.key]: e.target.value })} aria-label={s.key} /></td>
                  <td className="small muted">{s.description}</td>
                  <td>{edits[s.key] !== undefined && <button onClick={() => save(s.key)}>Save</button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
