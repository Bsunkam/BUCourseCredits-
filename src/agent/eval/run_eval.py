"""
Match-quality evaluation (design section 16): run labelled cases through the graph and compare the suggestion with
the evaluator's decision. Produces the agreement table kept as evidence.

    python -m eval.run_eval eval/labelled_cases.json [--out eval/results.csv] [--catalogue PATH]

Each case is an EvaluateRequest plus:
    "expectedOutcome":  "BuEquivalent", "ElectiveCredit" or "NoCredit" (the evaluator's decision)
    "expectedBuCourse": the BU course for a BU equivalent, otherwise null
Cases without "candidates" are matched against the whole BU catalogue, as the API does.

labelled_cases.json is generated with the synthetic past decisions by src/database/scripts/generate_synthetic_data.py.
Use real past decisions only if the sponsor approves.

Scoring:
    BU equivalent expected -> agree when the top suggestion is that course; "top 3" when it is among the candidates.
    Elective or no credit  -> agree when the agent reports "No equivalent found" (no BU course should be suggested
                              as a match). A top candidate at "No equivalent found" level is not a suggestion.
"""

import argparse
import csv
import json
import sys
from pathlib import Path

from app.models import CatalogueCourse, EvaluateRequest
from app.settings import load_settings
from graph.pipeline import build_graph, to_response

DEFAULT_CATALOGUE = Path(__file__).resolve().parents[2] / "database" / "TransferCredit.Data" / "Seed" / "catalogue.json"
NO_EQUIVALENT = "No equivalent found"


def load_catalogue(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    return [CatalogueCourse(code=c["code"], title=c["title"], credits=c["credits"], description=c.get("description"),
                            verified=c["verified"], learningOutcomes=c.get("learningOutcomes")).model_dump()
            for c in data["buCourses"]]


def score(expected_outcome: str, expected_bu: str | None, result) -> tuple[bool, bool]:
    """(agree, in_top3)"""
    if expected_outcome == "BuEquivalent":
        codes = [c.buCourseCode for c in result.candidates]
        agree = result.suggestedBuCourse == expected_bu and result.matchLevel != NO_EQUIVALENT
        return agree, expected_bu in codes
    agree = result.suggestedBuCourse is None or result.matchLevel == NO_EQUIVALENT
    return agree, agree


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("cases", type=Path)
    parser.add_argument("--out", type=Path, default=Path("eval/results.csv"))
    parser.add_argument("--catalogue", type=Path, default=DEFAULT_CATALOGUE)
    args = parser.parse_args()

    settings = load_settings()
    graph = build_graph(settings)
    cases = json.loads(args.cases.read_text(encoding="utf-8"))
    catalogue = None

    rows = []
    for i, case in enumerate(cases):
        expected_bu = case.pop("expectedBuCourse", None)
        expected_outcome = case.pop("expectedOutcome", "BuEquivalent" if expected_bu else "NoCredit")
        if not case.get("candidates"):
            catalogue = catalogue or load_catalogue(args.catalogue)
            case["candidates"] = catalogue
        case.setdefault("threadId", f"eval-{i}")
        request = EvaluateRequest.model_validate(case)
        config = {"configurable": {"thread_id": request.threadId}}
        graph.invoke({"request": request.model_dump()}, config)
        result = to_response(request, graph.get_state(config).values, paused=True)
        agree, top3 = score(expected_outcome, expected_bu, result)
        rows.append({"course": f"{request.sourceCourse.institution} {request.sourceCourse.code}",
                     "expectedOutcome": expected_outcome, "expected": expected_bu or "none",
                     "suggested": result.suggestedBuCourse or "none", "matchLevel": result.matchLevel,
                     "confidence": result.confidence, "agree": agree, "inTop3": top3, "model": result.model})

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ["course"])
        writer.writeheader()
        writer.writerows(rows)

    def line(label: str, subset: list[dict], key: str = "agree") -> str:
        n = len(subset)
        hits = sum(r[key] for r in subset)
        return f"| {label} | {hits}/{n} | {(hits / n * 100) if n else 0:.0f}% |"

    equivalent = [r for r in rows if r["expectedOutcome"] == "BuEquivalent"]
    other = [r for r in rows if r["expectedOutcome"] != "BuEquivalent"]
    model = rows[0]["model"] if rows else "n/a"
    table = "\n".join([
        f"Model: {model}. Cases: {len(rows)} from {args.cases.name}.",
        "",
        "| Measure | Agree | Rate |",
        "| --- | --- | --- |",
        line("All cases (top suggestion agrees with the evaluator)", rows),
        line("BU equivalent expected: top suggestion is that course", equivalent),
        line("BU equivalent expected: course is among the 3 candidates", equivalent, "inTop3"),
        line("Elective or no credit expected: no BU match suggested", other),
    ])
    summary = args.out.with_suffix(".md")
    summary.write_text(table + "\n", encoding="utf-8")

    print(table)
    print(f"\nRows -> {args.out}; summary -> {summary}")
    for row in rows:
        if not row["agree"]:
            print(f"  DISAGREE {row['course']}: expected {row['expectedOutcome']} {row['expected']}, "
                  f"got {row['suggested']} ({row['matchLevel']})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
