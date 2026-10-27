"""Deterministic keyword ranker used in mock mode (tests, local runs) and as a retrieval pre-filter."""

import math
import re

from app.models import CatalogueCourse

STOP_WORDS = {
    "the", "and", "for", "with", "into", "from", "this", "that", "are", "its", "including", "introduction",
    "intro", "course", "students", "student", "topics", "use", "using", "basic", "weekly", "two", "will",
    "fundamentals", "principles", "foundations", "class", "based", "outcomes", "write", "design", "how",
    # Generic learning-outcome verbs: every course "explains" and "applies", so they say nothing about content.
    "learning", "explain", "apply", "analyse", "analyze", "evaluate", "describe", "understand", "able",
}


def _stem(word: str) -> str:
    if word.endswith("ies") and len(word) > 4:
        return word[:-3] + "y"
    if word.endswith("s") and not word.endswith("ss") and len(word) > 3:
        return word[:-1]
    return word


def tokens(text: str | None) -> set[str]:
    words = (_stem(w) for w in re.findall(r"[a-z]+", (text or "").lower()))
    return {w for w in words if len(w) > 2 and w not in STOP_WORDS}


def rank(source_text: str, catalogue: list[CatalogueCourse], top: int = 5) -> list[tuple[CatalogueCourse, float, list[str]]]:
    """Cosine similarity on token sets. Returns (course, score, overlapping terms) best first."""
    source = tokens(source_text)
    scored = []
    for course in catalogue:
        target = tokens(f"{course.title} {course.description or ''} {course.learningOutcomes or ''}")
        if not source or not target:
            continue
        overlap = sorted(source & target)
        score = len(overlap) / math.sqrt(len(source) * len(target))
        if score > 0:
            scored.append((course, round(score, 3), overlap))
    scored.sort(key=lambda x: x[1], reverse=True)
    return scored[:top]


def sentences_with(text: str, terms: list[str], limit: int = 2) -> list[tuple[str, list[str]]]:
    """Verbatim syllabus sentences that contain the matched terms, most hits first."""
    parts = [s.strip() for s in re.split(r"(?<=[.;:])\s+", text or "") if s.strip()]
    hits = []
    for sentence in parts:
        found = [t for t in terms if t in tokens(sentence)]
        if found:
            hits.append((sentence[:300], found))
    hits.sort(key=lambda x: len(x[1]), reverse=True)
    return hits[:limit]
