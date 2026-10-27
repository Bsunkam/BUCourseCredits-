"""
LangGraph pipeline (design section 9):

    intake -> policy_gate -+-> matching -> evidence -+-> human_review (interrupt) -> END
                           +-------------------------+

The policy gate routes only on flags computed by the .NET rules engine. The graph always pauses for a human
decision; nothing is finalised here.
"""

import json
from typing import Any, Optional, TypedDict

from langchain_core.messages import HumanMessage, SystemMessage
from langgraph.checkpoint.memory import MemorySaver
from langgraph.graph import END, START, StateGraph
from langgraph.types import interrupt

from app.models import Candidate, CatalogueCourse, EvaluateRequest, EvaluateResponse, Evidence
from app.settings import Settings
from graph import ranking
from graph.services import EvidenceReport, Ranking, chat_model, load_prompt, search_candidates

GRAPH_VERSION = "1.0"
MOCK_MODEL = "mock-keyword-overlap (not AI)"
STUDENT_MESSAGE = "Your course is under department review."


class State(TypedDict, total=False):
    request: dict[str, Any]
    syllabus: str
    route: str
    candidates: list[dict[str, Any]]
    evidence: list[dict[str, Any]]
    explanation: str
    insufficient: bool
    model: str
    decision: Optional[dict[str, Any]]


def build_graph(settings: Settings, checkpointer=None):
    def req(state: State) -> EvaluateRequest:
        return EvaluateRequest.model_validate(state["request"])

    # ---------- nodes ----------

    def intake(state: State) -> State:
        # Syllabus text is untrusted data: trim it and never treat it as instructions.
        text = (req(state).syllabusText or "").strip()
        return {"syllabus": text[: settings.max_syllabus_chars]}

    def policy_gate(state: State) -> State:
        r = req(state)
        if r.policyFlags.gradeBelowMinimum:
            return {"route": "review", "candidates": [], "evidence": [], "insufficient": True,
                    "explanation": f"Grade {r.sourceCourse.grade} is below the minimum {r.policyFlags.minimumGrade}. "
                                   "Routed straight to an evaluator.", "model": "none (policy gate)"}
        if not state.get("syllabus"):
            return {"route": "review", "candidates": [], "evidence": [], "insufficient": True,
                    "explanation": "No syllabus supplied. Matching skipped; a syllabus is required (BR-08).",
                    "model": "none (policy gate)"}
        return {"route": "match"}

    def matching(state: State) -> State:
        r = req(state)
        source_text = (f"{r.sourceCourse.title} {r.sourceCourse.description or ''} "
                       f"{r.sourceCourse.learningOutcomes or ''} {state['syllabus']}")
        pool = search_candidates(settings, source_text[:2000], r.candidates) if settings.use_azure else r.candidates
        # Pre-filter to keep the prompt small; the model then ranks the shortlist.
        shortlist = [c for c, _, _ in ranking.rank(source_text, pool, top=8)] or pool[:8]
        known = {c.code: c for c in r.candidates}
        bu_only = set(r.policyFlags.buOnlyCourses)

        if settings.use_azure:
            ranked = _rank_with_model(settings, r, state["syllabus"], shortlist)
            model = settings.chat_deployment
        else:
            ranked = _rank_with_keywords(r, source_text, shortlist)
            model = MOCK_MODEL

        candidates = []
        for c in ranked:
            if c.buCourseCode not in known:
                continue  # never return a course the API did not offer
            if c.buCourseCode in bu_only:  # program rule beats the model (BR-10)
                c = c.model_copy(update={"matchLevel": "Must be taken at BU"})
            candidates.append(c.model_dump())
        return {"candidates": candidates[:3], "model": model}

    def evidence(state: State) -> State:
        r = req(state)
        top = state["candidates"][0] if state["candidates"] else None
        if top is None:
            return {"evidence": [], "insufficient": True,
                    "explanation": "No BU course shares enough content with this course. An evaluator should review it."}

        if settings.use_azure:
            report = _evidence_with_model(settings, r, state["syllabus"], top)
        else:
            terms = [t.split(": ", 1)[-1] for t in top["matchedAttributes"] if t.startswith("keywords")]
            words = terms[0].split(", ") if terms else []
            hits = ranking.sentences_with(state["syllabus"], words)
            report = EvidenceReport(
                explanation=f"Keyword overlap suggests {top['buCourseCode']} ({top['matchLevel']}). "
                            "Mock ranking, not an AI judgement and not an official equivalency.",
                evidence=[{"snippet": s, "matchesOutcome": ", ".join(h)} for s, h in hits],
                insufficientEvidence=not hits,
            )

        # Anti-hallucination: keep only snippets that appear verbatim in the syllabus.
        syllabus = state["syllabus"]
        cited = [
            Evidence(source="syllabus", snippet=e.snippet, matchesOutcome=e.matchesOutcome).model_dump()
            for e in report.evidence
            if e.snippet and e.snippet.strip() in syllabus
        ]
        return {"evidence": cited, "explanation": report.explanation,
                "insufficient": report.insufficientEvidence or not cited}

    def human_review(state: State) -> State:
        # Pause until the .NET API reports the evaluator's decision via /agent/resume.
        decision = interrupt({
            "courseId": state["request"]["courseId"],
            "suggested": state["candidates"][0]["buCourseCode"] if state.get("candidates") else None,
        })
        return {"decision": decision}

    # ---------- wiring ----------
    builder = StateGraph(State)
    builder.add_node("intake", intake)
    builder.add_node("policy_gate", policy_gate)
    builder.add_node("matching", matching)
    # Node name must differ from the "evidence" state key (LangGraph rejects the clash).
    builder.add_node("gather_evidence", evidence)
    builder.add_node("human_review", human_review)
    builder.add_edge(START, "intake")
    builder.add_edge("intake", "policy_gate")
    builder.add_conditional_edges("policy_gate", lambda s: s["route"], {"match": "matching", "review": "human_review"})
    builder.add_edge("matching", "gather_evidence")
    builder.add_edge("gather_evidence", "human_review")
    builder.add_edge("human_review", END)
    # MemorySaver keeps paused threads in process memory. For more than one replica, swap in a
    # persistent checkpointer (for example a Postgres or Redis saver). The decision itself is always in SQL.
    return builder.compile(checkpointer=checkpointer or MemorySaver())


def _rank_with_keywords(r: EvaluateRequest, source_text: str, shortlist: list[CatalogueCourse]) -> list[Candidate]:
    out = []
    for course, score, overlap in ranking.rank(source_text, shortlist, top=3):
        level = "Partial - syllabus review" if score >= 0.2 else "No equivalent found"
        confidence = "high" if score >= 0.35 else "medium" if score >= 0.2 else "low"
        attrs = [f"keywords: {', '.join(overlap[:8])}",
                 f"credits {r.policyFlags.convertedCredits} vs {course.credits}"]
        out.append(Candidate(buCourseCode=course.code, matchLevel=level, score=score,
                             confidence=confidence, matchedAttributes=attrs))
    return out


def _rank_with_model(settings: Settings, r: EvaluateRequest, syllabus: str, shortlist: list[CatalogueCourse]) -> list[Candidate]:
    model = chat_model(settings, settings.chat_deployment).with_structured_output(Ranking)
    payload = {
        "sourceCourse": r.sourceCourse.model_dump(),
        "convertedCredits": r.policyFlags.convertedCredits,
        "buCandidates": [c.model_dump() for c in shortlist],
    }
    result: Ranking = model.invoke([
        SystemMessage(load_prompt("match", r.promptVersion)),
        HumanMessage(f"COURSE DATA (JSON):\n{json.dumps(payload)}\n\n"
                     f"<untrusted_syllabus>\n{syllabus}\n</untrusted_syllabus>"),
    ])
    scores = {c.code: s for c, s, _ in ranking.rank(f"{r.sourceCourse.title} {syllabus}", shortlist, top=len(shortlist))}
    return [Candidate(buCourseCode=c.buCourseCode, matchLevel=c.matchLevel, confidence=c.confidence,
                      score=scores.get(c.buCourseCode), matchedAttributes=c.matchedAttributes)
            for c in result.candidates]


def _evidence_with_model(settings: Settings, r: EvaluateRequest, syllabus: str, top: dict[str, Any]) -> EvidenceReport:
    target = next((c for c in r.candidates if c.code == top["buCourseCode"]), None)
    model = chat_model(settings, settings.chat_deployment).with_structured_output(EvidenceReport)
    return model.invoke([
        SystemMessage(load_prompt("evidence", r.promptVersion)),
        HumanMessage(f"BU COURSE (JSON):\n{target.model_dump_json() if target else '{}'}\n"
                     f"MATCH LEVEL: {top['matchLevel']}\n\n"
                     f"<untrusted_syllabus>\n{syllabus}\n</untrusted_syllabus>"),
    ])


def to_response(request: EvaluateRequest, values: State, paused: bool) -> EvaluateResponse:
    candidates = [Candidate.model_validate(c) for c in values.get("candidates", [])]
    top = candidates[0] if candidates else None
    return EvaluateResponse(
        status="paused_for_review" if paused else "completed",
        threadId=request.threadId,
        matchLevel=top.matchLevel if top else "Partial - syllabus review" if not values.get("syllabus") else "No equivalent found",
        suggestedBuCourse=top.buCourseCode if top else None,
        confidence=top.confidence if top else "low",
        candidates=candidates,
        evidence=[Evidence.model_validate(e) for e in values.get("evidence", [])],
        explanation=values.get("explanation", ""),
        studentMessage=STUDENT_MESSAGE,
        insufficientEvidence=bool(values.get("insufficient", True)),
        graphVersion=GRAPH_VERSION,
        promptVersion=request.promptVersion,
        model=values.get("model", MOCK_MODEL),
        cacheKey=request.cacheKey,
    )
