"""Pydantic side of /contracts/agent-contract.schema.json. Keep in step with AgentContracts.cs."""

from typing import Literal, Optional

from pydantic import BaseModel, ConfigDict, Field

MatchLevel = Literal[
    "Likely equivalent",
    "Partial - syllabus review",
    "No equivalent found",
    "Must be taken at BU",
]
Confidence = Literal["low", "medium", "high"]


class Contract(BaseModel):
    model_config = ConfigDict(populate_by_name=True, extra="forbid")


class SourceCourse(Contract):
    institution: str
    code: str
    title: str
    credits: float
    creditSystem: Literal["semester", "quarter"]
    grade: Optional[str] = None
    term: Optional[str] = None
    description: Optional[str] = None
    learningOutcomes: Optional[str] = None


class PolicyFlags(Contract):
    """Computed by the .NET rules engine. The agent routes on these and never recalculates them."""

    convertedCredits: float
    quarterConversionApplied: bool
    gradeBelowMinimum: Optional[bool] = None
    minimumGrade: str
    buOnlyCourses: list[str] = Field(default_factory=list)
    rulesVersion: str


class CatalogueCourse(Contract):
    code: str
    title: str
    credits: float
    description: Optional[str] = None
    verified: str
    learningOutcomes: Optional[str] = None


class EvaluateRequest(Contract):
    courseId: str
    threadId: str
    program: str
    sourceCourse: SourceCourse
    syllabusText: Optional[str] = None
    policyFlags: PolicyFlags
    candidates: list[CatalogueCourse] = Field(default_factory=list)
    promptVersion: str
    cacheKey: str


class Evidence(Contract):
    source: str
    snippet: str
    matchesOutcome: Optional[str] = None


class Candidate(Contract):
    buCourseCode: str
    matchLevel: MatchLevel
    score: Optional[float] = None
    confidence: Confidence
    matchedAttributes: list[str] = Field(default_factory=list)


class EvaluateResponse(Contract):
    status: Literal["paused_for_review", "completed"]
    threadId: str
    matchLevel: MatchLevel
    suggestedBuCourse: Optional[str] = None
    confidence: Confidence
    candidates: list[Candidate]
    evidence: list[Evidence]
    explanation: str
    studentMessage: str
    insufficientEvidence: bool
    needsHumanReview: Literal[True] = True  # BR-01: always a human decision
    graphVersion: str
    promptVersion: str
    model: str
    cacheKey: str


class ExtractRequest(Contract):
    documentId: str
    container: str
    blobPath: str
    documentType: str


class ExtractedCourse(Contract):
    code: Optional[str] = None
    title: Optional[str] = None
    credits: Optional[float] = None
    grade: Optional[str] = None
    term: Optional[str] = None


class ExtractResponse(Contract):
    text: str
    courses: list[ExtractedCourse] = Field(default_factory=list)
    missingFields: list[str] = Field(default_factory=list)
    model: str


class ResumeRequest(Contract):
    threadId: str
    outcomeType: Literal["BuEquivalent", "ElectiveCredit", "NoCredit"]
    buCourseCode: Optional[str] = None
    evaluatorId: str
