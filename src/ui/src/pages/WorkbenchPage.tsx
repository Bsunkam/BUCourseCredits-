import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api } from "../api";
import { Badge, ErrorAlert, isEvaluator, isReviewer, Loading, useLoad, useUser } from "../components";
import { candidateSource, courseStatus, EQUIVALENCY_DB, formatDateTime, outcomeLabel, units } from "../labels";
import type { BuCourse, Candidate, Course, CourseDetail, Evaluation, Evidence, OutcomeType } from "../types";

export default function WorkbenchPage() {
  const { id } = useParams();
  const user = useUser();
  const { data, error, reload, setError } = useLoad<CourseDetail>(`/api/courses/${id}`, (d) =>
    d?.course.status === "Matching" ? 1500 : null,
  );
  const [selected, setSelected] = useState<number | null>(null);
  // "auto" = pre-selected from the top suggestion; the evaluator has not chosen it yet.
  const [selectedBy, setSelectedBy] = useState<"auto" | "evaluator">("auto");

  useEffect(() => {
    // Pre-select only a plausible, unblocked match; never a weak "No equivalent found" candidate.
    if (data && selected === null) {
      const first = data.candidates.find((c) => !c.isBlocked && c.matchLevel !== "No equivalent found");
      if (first) setSelected(first.buCourse.id);
    }
  }, [data, selected]);
  const selectedCandidate = data?.candidates.find((c) => c.buCourse.id === selected);
  const selectionIsSuggestion = selectedBy === "auto" && !!selectedCandidate && selectedCandidate.source !== "ApprovedRule";

  if (!isReviewer(user)) return <div className="alert error">Only Credit Evaluation staff and evaluators can open the workbench.</div>;
  if (!data) return error ? <ErrorAlert error={error} /> : <Loading />;

  const { course } = data;
  const final = data.evaluations.find((e) => e.status === "Final");
  const draft = data.evaluations.find((e) => e.status === "Draft");
  const status = courseStatus[course.status];

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
          <div className="muted small">
            <Link to="/cases">Cases</Link> / <Link to={`/cases/${data.caseId}`}>{data.caseRef}</Link> / {course.code}
          </div>
          <h1>Evaluator workbench · {course.code} {course.title}</h1>
          <div className="row">
            <Badge tone={status.tone}>{status.label}</Badge>
            {course.matchLevel && <Badge tone="neutral">{course.matchLevel}</Badge>}
            <span className="muted">{data.program}</span>
          </div>
        </div>
        {course.status !== "Decided" && (
          <button disabled={course.status === "Matching"} onClick={() => act(() => api.post(`/api/courses/${course.id}/match`))}>
            {course.status === "Matching" ? "Matching…" : "Re-run matching"}
          </button>
        )}
      </div>

      <ErrorAlert error={error} />
      {course.status === "Blocked" && (
        <div className="alert error">
          The top candidate must be taken at BU under the program rules. It cannot be awarded as a transfer equivalent.
        </div>
      )}
      {course.status === "NeedsSyllabus" && (
        <div className="alert warn">No approved equivalency and no syllabus. The student has been asked for the syllabus (BR-08).</div>
      )}

      <div className="compare" style={{ marginBottom: 16 }}>
        <SourcePanel data={data} onSaved={reload} onError={setError} />
        <section className="card stack">
          <h2>BU candidates</h2>
          {course.appliedRule && (
            <div className="alert info">
              Approved equivalency #{course.appliedRule.id}: {outcomeLabel[course.appliedRule.outcomeType]}{" "}
              {course.appliedRule.buCourse?.code} · {course.appliedRule.decisionSource}
              {course.appliedRule.verifiedStatus === "Check" && <> · <Badge tone="warn">Unverified catalogue data</Badge></>}
            </div>
          )}
          {data.candidates.length === 0 && <p className="muted">No candidates. Search the catalogue in the decision form.</p>}
          {data.candidates.map((c) => (
            <CandidateCard key={c.id} candidate={c} source={course} selected={selected === c.buCourse.id}
              onSelect={() => { setSelected(c.buCourse.id); setSelectedBy("evaluator"); }} />
          ))}
        </section>
      </div>

      <div className="grid-2">
        {final ? (
          <FinalDecision evaluation={final} />
        ) : (
          <DecisionForm
            data={data}
            draft={draft}
            selectedBuCourseId={selected}
            selectionIsSuggestion={selectionIsSuggestion}
            canFinaliseAnything={isEvaluator(user)}
            onChanged={reload}
            onError={setError}
          />
        )}
        <AgentRuns data={data} />
      </div>
    </>
  );
}

function SourcePanel({ data, onSaved, onError }: { data: CourseDetail; onSaved: () => void; onError: (m: string) => void }) {
  const { course, policyFlags } = data;
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState(data.syllabusText ?? "");

  async function save() {
    try {
      await api.put(`/api/courses/${course.id}/syllabus`, { text });
      setEditing(false);
      onSaved();
    } catch (e) {
      onError((e as Error).message);
    }
  }

  return (
    <section className="card stack">
      <h2>Source course</h2>
      <dl className="kv">
        <dt>Institution</dt><dd>{course.institution}</dd>
        <dt>Course</dt><dd><strong>{course.code}</strong> {course.title}</dd>
        <dt>Credits</dt>
        <dd>
          {course.credits} {course.creditSystem.toLowerCase()} → <strong>{units(course.convertedCredits)}</strong> BU credits
          {course.creditSystem === "Quarter" && <span className="muted small"> (× 2/3, rules engine)</span>}
        </dd>
        <dt>Grade</dt>
        <dd>
          {course.grade ?? "—"}{course.term ? ` · ${course.term}` : ""}
          <div className="muted small">External grades do not count toward the BU GPA. Credit posts with grade T and 0 Hub units.</div>
        </dd>
        <dt>Description</dt><dd>{course.description ?? <span className="muted">—</span>}</dd>
        <dt>Learning outcomes</dt><dd>{course.learningOutcomes ?? <span className="muted">Not supplied. Check the syllabus.</span>}</dd>
      </dl>

      {policyFlags && (
        <div className="row">
          <Badge tone={policyFlags.gradeBelowMinimum ? "danger" : policyFlags.gradeBelowMinimum === null ? "warn" : "success"}>
            {policyFlags.gradeBelowMinimum ? `Grade below ${policyFlags.minimumGrade}`
              : policyFlags.gradeBelowMinimum === null ? "Grade needs review" : `Grade meets ${policyFlags.minimumGrade}`}
          </Badge>
          {policyFlags.quarterConversionApplied && <Badge tone="info">Quarter conversion applied</Badge>}
          <Badge tone="neutral">BU-only in program: {policyFlags.buOnlyCourses.join(", ") || "none"}</Badge>
          <span className="muted small">rules {policyFlags.rulesVersion}</span>
        </div>
      )}

      <div className="row between">
        <h3>Syllabus evidence</h3>
        {course.status !== "Decided" && !editing && <button onClick={() => setEditing(true)}>{data.syllabusText ? "Edit" : "Add syllabus text"}</button>}
      </div>
      {editing ? (
        <div className="stack">
          <textarea rows={8} value={text} onChange={(e) => setText(e.target.value)}
            placeholder="Paste the syllabus learning outcomes and topics. Treated as untrusted data." />
          <div className="row">
            <button className="primary" onClick={save}>Save and mark for re-matching</button>
            <button onClick={() => setEditing(false)}>Cancel</button>
          </div>
        </div>
      ) : data.syllabusText ? (
        <div className="syllabus small">{data.syllabusText}</div>
      ) : (
        <p className="muted">No syllabus on file.</p>
      )}
    </section>
  );
}

function CandidateCard({ candidate: c, source, selected, onSelect }: {
  candidate: Candidate; source: Course; selected: boolean; onSelect: () => void;
}) {
  const evidence = Array.isArray(c.evidence) && c.evidence.length > 0 && typeof c.evidence[0] === "object"
    ? (c.evidence as Evidence[]) : [];
  const creditGap = source.convertedCredits - c.buCourse.credits;
  return (
    <div className={`candidate ${selected ? "selected" : ""}`}>
      <div className="row between">
        <div>
          <strong>{c.buCourse.code}</strong> {c.buCourse.title}
          <div className="muted small">
            {c.buCourse.credits} BU credits vs {units(source.convertedCredits)} transferred
            {creditGap < 0 && <> · <span className="tone-warn badge">{units(-creditGap)} fewer</span></>}
            {" "}· {c.buCourse.requirementType}
          </div>
        </div>
        <button onClick={onSelect} disabled={c.isBlocked} aria-pressed={selected}>{selected ? "Selected" : "Select"}</button>
      </div>
      <div className="row">
        <Badge tone={c.isBlocked ? "danger" : c.source === "ApprovedRule" ? "info" : "accent"}>{c.matchLevel}</Badge>
        {c.confidence && c.source !== "ApprovedRule" && <Badge tone="neutral">confidence {c.confidence}</Badge>}
        {c.score != null && <span className="muted small">similarity {c.score} (not an official equivalency)</span>}
        <Badge tone={c.buCourse.verifiedStatus === "Yes" ? "success" : "warn"}>
          {c.buCourse.verifiedStatus === "Yes" ? "Catalogue verified" : "Catalogue: check"}
        </Badge>
      </div>
      <div className="small muted">{candidateSource[c.source]} · {c.matchVersion}</div>
      {c.isBlocked && c.buCourse.buOnlyRule && <div className="alert error small">Rule: {c.buCourse.buOnlyRule}</div>}
      {c.explanation && <p className="small" style={{ margin: 0 }}>{c.explanation}</p>}
      {evidence.map((e, i) => (
        <div key={i} className="evidence small">
          “{e.snippet}” <span className="muted">— {e.source}{e.matchesOutcome ? ` · matches: ${e.matchesOutcome}` : ""}</span>
        </div>
      ))}
      {c.buCourse.description && <div className="small"><span className="muted">BU description: </span>{c.buCourse.description}</div>}
      {c.buCourse.learningOutcomes && <div className="small"><span className="muted">BU learning outcomes: </span>{c.buCourse.learningOutcomes}</div>}
      {c.buCourse.sourceUrl && <a className="small" href={c.buCourse.sourceUrl} target="_blank" rel="noreferrer">Catalogue source</a>}
    </div>
  );
}

function DecisionForm({
  data, draft, selectedBuCourseId, selectionIsSuggestion, canFinaliseAnything, onChanged, onError,
}: {
  data: CourseDetail; draft?: Evaluation; selectedBuCourseId: number | null; selectionIsSuggestion: boolean;
  canFinaliseAnything: boolean; onChanged: () => void; onError: (m: string) => void;
}) {
  const { course } = data;
  const rule = course.appliedRule;
  const [outcome, setOutcome] = useState<OutcomeType>(draft?.outcomeType ?? rule?.outcomeType ?? "BuEquivalent");
  const [buCourseId, setBuCourseId] = useState<number | null>(draft?.buCourse?.id ?? rule?.buCourse?.id ?? selectedBuCourseId);
  // True while the BU course in the form is the agent's pick that nobody has explicitly chosen (governance rule:
  // an AI suggestion must never look like, or become, a decision by default).
  const [fromSuggestion, setFromSuggestion] = useState(!draft && !rule && selectionIsSuggestion);
  const [confirmed, setConfirmed] = useState(false);
  const [awarded, setAwarded] = useState(String(draft?.awardedUnits ?? course.convertedCredits));
  const [rationale, setRationale] = useState(draft?.rationale ?? (rule ? `Applied approved equivalency #${rule.id}.` : ""));
  const [search, setSearch] = useState("");
  const [results, setResults] = useState<BuCourse[]>([]);

  useEffect(() => {
    if (!draft && selectedBuCourseId) {
      setBuCourseId(selectedBuCourseId);
      setFromSuggestion(!rule && selectionIsSuggestion);
    }
  }, [selectedBuCourseId, selectionIsSuggestion, draft, rule]);

  useEffect(() => {
    if (search.length < 2) return setResults([]);
    const t = setTimeout(() => {
      api.get<BuCourse[]>(`/api/catalogue/search?q=${encodeURIComponent(search)}&program=${encodeURIComponent(data.program)}`)
        .then(setResults).catch(() => setResults([]));
    }, 250);
    return () => clearTimeout(t);
  }, [search, data.program]);

  const matchesRule = !!rule && rule.outcomeType === outcome && (rule.buCourse?.id ?? null) === (outcome === "BuEquivalent" ? buCourseId : null)
    && Number(awarded) === (rule.outcomeType === "NoCredit" ? 0 : course.convertedCredits);
  const needsConfirmation = outcome === "BuEquivalent" && fromSuggestion && !confirmed;
  const canFinalise = (canFinaliseAnything || matchesRule) && !needsConfirmation;
  const chosen = data.candidates.find((c) => c.buCourse.id === buCourseId)?.buCourse ?? results.find((r) => r.id === buCourseId);

  async function saveDraft(): Promise<number | null> {
    try {
      const ev = await api.post<Evaluation>("/api/evaluations", {
        sourceCourseId: course.id,
        outcomeType: outcome,
        buCourseId: outcome === "BuEquivalent" ? buCourseId : null,
        awardedUnits: outcome === "NoCredit" ? 0 : Number(awarded),
        rationale,
      });
      return ev.id;
    } catch (e) {
      onError((e as Error).message);
      return null;
    }
  }

  async function finalise() {
    const evId = await saveDraft();
    if (evId == null) return;
    try {
      await api.post(`/api/evaluations/${evId}/finalize`);
      onChanged();
    } catch (e) {
      onError((e as Error).message);
    }
  }

  return (
    <section className="card stack">
      <h2>Decision</h2>
      <p className="muted small">
        Only an authorised department evaluator finalises (BR-01). Credit Evaluation staff may only confirm an existing approved equivalency.
      </p>
      <fieldset className="row" style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className="small muted" style={{ marginBottom: 6 }}>Outcome</legend>
        {(Object.keys(outcomeLabel) as OutcomeType[]).map((o) => (
          <label key={o} className="row">
            <input type="radio" name="outcome" checked={outcome === o} onChange={() => setOutcome(o)} /> {outcomeLabel[o]}
          </label>
        ))}
      </fieldset>

      {outcome === "BuEquivalent" && (
        <div className="stack">
          <div>
            BU course: {chosen ? <strong>{chosen.code} {chosen.title}</strong> : <span className="muted">select a candidate or search</span>}
            {chosen && fromSuggestion && <> <Badge tone="accent">AI suggestion · verify</Badge></>}
          </div>
          {chosen && fromSuggestion && (
            <label className="row small">
              <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} />
              I compared the evidence and choose {chosen.code} myself. The agent's suggestion is not a decision.
            </label>
          )}
          <input placeholder="Search BU catalogue" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search BU catalogue" />
          {results.length > 0 && (
            <div className="stack small">
              {results.slice(0, 6).map((r) => (
                <button key={r.id} style={{ textAlign: "left" }} disabled={!!r.buOnlyRule}
                  title={r.buOnlyRule ?? undefined} onClick={() => { setBuCourseId(r.id); setFromSuggestion(false); }}>
                  {r.code} {r.title}{r.buOnlyRule ? " · must be taken at BU" : ""}
                </button>
              ))}
            </div>
          )}
        </div>
      )}

      {outcome !== "NoCredit" && (
        <label className="field">Awarded units (default: converted credits {units(course.convertedCredits)}; must be more than 0)
          <input type="number" min="0.01" max="20" step="0.01" value={awarded} onChange={(e) => setAwarded(e.target.value)} />
        </label>
      )}
      <label className="field">Rationale (required, recorded in the audit trail)
        <textarea rows={3} value={rationale} onChange={(e) => setRationale(e.target.value)} />
      </label>
      {draft && <p className="small muted">Draft saved by {draft.evaluatorId} · {formatDateTime(draft.createdAt)}</p>}
      <div className="row">
        <button onClick={async () => { if (await saveDraft()) onChanged(); }}>Save draft</button>
        <button className="primary" disabled={!canFinalise} onClick={finalise}
          title={canFinalise ? "Records the final decision"
            : needsConfirmation ? "Confirm that you chose the BU course yourself"
            : "Only an evaluator can finalise this outcome"}>
          Finalise decision
        </button>
      </div>
    </section>
  );
}

function FinalDecision({ evaluation: e }: { evaluation: Evaluation }) {
  const applied = e.evaluatorId === EQUIVALENCY_DB;
  return (
    <section className="card stack">
      <h2>Final decision</h2>
      <div className="row">
        <Badge tone="success">{outcomeLabel[e.outcomeType]}</Badge>
        {e.buCourse && <strong>{e.buCourse.code} {e.buCourse.title}</strong>}
        <span>· {units(e.awardedUnits)} units</span>
      </div>
      <dl className="kv">
        <dt>Reviewer</dt><dd>{applied ? "Equivalency database (an existing department decision)" : e.evaluatorId}</dd>
        <dt>Decided</dt><dd>{e.decisionAt ? formatDateTime(e.decisionAt) : "—"}</dd>
        <dt>Rationale</dt><dd>{e.rationale}</dd>
      </dl>
      <p className="muted small">
        {applied
          ? "Applied from an existing approved equivalency, as for any course BU has evaluated before. No new review was needed."
          : "Saved as an approved equivalency so the next identical course is resolved without an AI call, unless the decision depended on this student's grade."}
      </p>
    </section>
  );
}

function AgentRuns({ data }: { data: CourseDetail }) {
  return (
    <section className="card stack">
      <h2>Agent runs (AI usage evidence)</h2>
      <div className="mono muted" title="Reuse key: institution, course, credits, syllabus, program, rules and prompt versions">
        key {data.cacheKey.slice(0, 30)}…
      </div>
      {data.agentRuns.length === 0 ? <p className="muted">No runs yet.</p> : (
        <div className="table-wrap">
          <table>
            <thead><tr><th>#</th><th>Path</th><th>Status</th><th>Model / note</th><th className="num">ms</th><th>When</th></tr></thead>
            <tbody>
              {data.agentRuns.map((r) => (
                <tr key={r.id}>
                  <td>{r.id}</td>
                  <td>{r.source === "NewRun" ? "Agent run" : r.source === "Cache" ? "Cache hit" : "Approved rule"}</td>
                  <td>{r.status}</td>
                  <td className="small">
                    {r.model ?? ""}{r.promptVersion ? ` · ${r.promptVersion}` : ""}{r.graphVersion ? ` · graph ${r.graphVersion}` : ""}
                    {r.note && <div className="muted">{r.note}</div>}
                  </td>
                  <td className="num">{r.latencyMs}</td>
                  <td className="small">{formatDateTime(r.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
