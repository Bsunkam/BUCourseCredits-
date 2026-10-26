"""
Match-quality evaluation (design section 16): run labelled cases through the graph and compare the top
suggestion with the evaluator's decision. Produces the agreement table kept as evidence.

    python -m eval.run_eval eval/labelled_cases.json [--out eval/results.csv]

Each case is an EvaluateRequest plus "expectedBuCourse" (null when the expected outcome is no equivalent).
Use past decisions only if the sponsor approves; otherwise use the labelled synthetic cases.
"""

import argparse
import csv
import json
import sys
from pathlib import Path

from app.models import EvaluateRequest
from app.settings import load_settings
from graph.pipeline import build_graph, to_response


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("cases", type=Path)
    parser.add_argument("--out", type=Path, default=Path("eval/results.csv"))
    args = parser.parse_args()

    settings = load_settings()
    graph = build_graph(settings)
    cases = json.loads(args.cases.read_text(encoding="utf-8"))

    rows, agree = [], 0
    for i, case in enumerate(cases):
        expected = case.pop("expectedBuCourse", None)
        case.setdefault("threadId", f"eval-{i}")
        request = EvaluateRequest.model_validate(case)
        config = {"configurable": {"thread_id": request.threadId}}
        graph.invoke({"request": request.model_dump()}, config)
        result = to_response(request, graph.get_state(config).values, paused=True)
        ok = result.suggestedBuCourse == expected
        agree += ok
        rows.append({"course": f"{request.sourceCourse.institution} {request.sourceCourse.code}",
                     "expected": expected or "none", "suggested": result.suggestedBuCourse or "none",
                     "matchLevel": result.matchLevel, "confidence": result.confidence, "agree": ok,
                     "model": result.model})

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ["course"])
        writer.writeheader()
        writer.writerows(rows)

    total = len(rows)
    print(f"Agreement: {agree}/{total} ({(agree / total * 100) if total else 0:.0f}%) -> {args.out}")
    for row in rows:
        if not row["agree"]:
            print(f"  DISAGREE {row['course']}: expected {row['expected']}, got {row['suggested']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
