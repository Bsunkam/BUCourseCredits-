// Mirrors src/api/TransferCredit.Api/Contracts/Dtos.cs

export type UserRole = "Staff" | "Evaluator" | "Admin" | "Student" | "Advisor";
export type CaseStatus = "Open" | "AwaitingDocuments" | "InReview" | "Decided" | "Posted";
export type CourseStatus =
  | "Pending" | "Matching" | "ApprovedMatchFound" | "NeedsSyllabus" | "NeedsReview" | "Blocked" | "Decided";
export type CreditSystem = "Semester" | "Quarter";
export type OutcomeType = "BuEquivalent" | "ElectiveCredit" | "NoCredit";
export type ScanStatus = "Pending" | "Clean" | "Quarantined";
export type DocumentType = "Transcript" | "Syllabus" | "Other";

/** The signed-in account (GET /api/me). */
export interface CurrentUser {
  id: string; displayName: string; role: UserRole; email?: string | null; studentRef?: string | null; mustChangePassword: boolean;
}
/** A row on the admin Users page. */
export interface UserAccount {
  id: string; displayName: string; role: UserRole; email?: string | null; studentRef?: string | null; isActive: boolean;
  isLocked: boolean; hasPassword: boolean; mustChangePassword: boolean; lastSignInAt?: string | null;
}
export interface DemoAccount { username: string; displayName: string; role: UserRole }
export interface Institution { id: number; name: string; creditSystem: CreditSystem; catalogueSource?: string | null }

export interface BuCourse {
  id: number; code: string; title: string; program: string; requirementType: string; credits: number;
  description?: string | null; verifiedStatus: "Yes" | "Check"; sourceUrl?: string | null; buOnlyRule?: string | null;
  learningOutcomes?: string | null;
}

export interface CaseSummary {
  id: number; caseRef: string; studentName: string; studentRef: string; program: string; startDate: string;
  deadlineDate: string; daysRemaining: number; overdue: boolean; status: CaseStatus; courseCount: number;
  openCourseCount: number; createdAt: string;
}

export interface AppliedRule {
  id: number; outcomeType: OutcomeType; buCourse?: BuCourse | null; verifiedStatus: "Yes" | "Check";
  decisionSource: string; fromEvaluation: boolean;
}

export interface Evidence { source: string; snippet: string; matchesOutcome?: string | null }

export interface Candidate {
  id: number; buCourse: BuCourse; matchLevel: string; score?: number | null; confidence?: string | null;
  evidence?: Evidence[] | string[] | null; explanation?: string | null; source: "ApprovedRule" | "Cache" | "Agent" | "Stub";
  isBlocked: boolean; matchVersion: string;
}

export interface Evaluation {
  id: number; sourceCourseId: number; evaluatorId: string; outcomeType: OutcomeType; buCourse?: BuCourse | null;
  awardedUnits: number; rationale: string; status: "Draft" | "Final"; createdAt: string; decisionAt?: string | null;
}

export interface Course {
  id: number; institutionId: number; institution: string; code: string; title: string; credits: number;
  creditSystem: CreditSystem; convertedCredits: number; grade?: string | null; term?: string | null;
  description?: string | null; hasSyllabus: boolean; status: CourseStatus; matchLevel?: string | null;
  appliedRule?: AppliedRule | null; topCandidate?: Candidate | null; evaluation?: Evaluation | null;
  learningOutcomes?: string | null;
  /** "T" once the case is posted and the course earned units. */
  postedGrade?: string | null;
  /** Always 0: transfer courses do not earn BU Hub units (BR-05). */
  hubUnits: number;
}

export interface AgentRun {
  id: number; source: "ApprovedRule" | "Cache" | "NewRun"; status: string; model?: string | null;
  graphVersion?: string | null; promptVersion?: string | null; latencyMs: number; note?: string | null;
  cacheKey: string; createdAt: string;
}

export interface PolicyFlags {
  convertedCredits: number; quarterConversionApplied: boolean; gradeBelowMinimum?: boolean | null;
  minimumGrade: string; buOnlyCourses: string[]; rulesVersion: string;
}

export interface CourseDetail {
  course: Course; caseId: number; caseRef: string; program: string; syllabusText?: string | null;
  policyFlags?: PolicyFlags | null; candidates: Candidate[]; evaluations: Evaluation[]; agentRuns: AgentRun[];
  cacheKey: string;
}

export interface DocumentInfo {
  id: number; caseId?: number | null; sourceCourseId?: number | null; documentType: DocumentType;
  source: "Portal" | "Email" | "StaffUpload"; fileName: string; contentType: string; sizeBytes: number;
  sha256: string; scanStatus: ScanStatus; extractionStatus: string; uploadedAt: string;
}

export interface AuditEntry {
  id: number; timestamp: string; actorId: string; action: string; detail?: string | null; studentMessage?: string | null;
}

export interface CaseDetail {
  summary: CaseSummary; studentEmail?: string | null; postedAt?: string | null; courses: Course[];
  documents: DocumentInfo[]; timeline: AuditEntry[];
}

export type Standing = "Secured" | "Pending review" | "At risk" | "No credit";

export interface StudentCourse {
  id: number; code: string; title: string; institution: string; state: string; syllabusNeeded: boolean;
  awardedUnits: number; outcome?: string | null;
  /** What the course could be worth after conversion. Never awarded until a final decision (BR-02). */
  potentialUnits: number; standing: Standing; standingReason?: string | null; requirement?: string | null;
  syllabusReason?: string | null; postedGrade?: string | null; hubUnits: number;
}

export interface DegreeProgress {
  securedUnits: number; pendingUnits: number; atRiskUnits: number; coursesNeedingAction: number;
  securedByRequirement: { requirement: string; units: number; courses: number }[];
}

export interface StudentStatus {
  caseId: number; caseRef: string; program: string; startDate: string; deadlineDate: string; daysRemaining: number;
  overdue: boolean; status: CaseStatus; nextAction: string; awardedUnits: number; pendingUnitsShown: number;
  courses: StudentCourse[]; timeline: { timestamp: string; message: string }[]; documents: DocumentInfo[];
  progress: DegreeProgress; policyNotes: string[]; postedAt?: string | null;
}

export interface IntakeMessage {
  id: number; channel: "Email" | "EmlImport"; senderAddress: string; subject: string; receivedAt: string;
  caseId?: number | null; matchStatus: "Matched" | "Unmatched" | "Ambiguous"; assignedBy?: string | null;
  documents: DocumentInfo[];
}

export interface Metrics {
  cases: number; casesByStatus: Record<string, number>; courses: number; matchRuns: number; aiCalls: number;
  approvedRuleHits: number; cacheHits: number; reuseRate: number; finalDecisions: number;
  finalDecisionsWithReviewer: number; agentRunsWithModelRecord: number; failedAgentRuns: number; casesWithDeadline: number;
  coursesByStatus: Record<string, number>;
  /** Existing equivalencies applied without a new review (not counted in finalDecisions). */
  appliedFromEquivalencyDb: number;
  /** Primary outcome: median hours a course shows 0 units, case opened to final decision. */
  medianHoursAtZeroUnits?: number | null;
  medianHoursAtZeroUnitsReviewed?: number | null;
  coursesStillAtZeroUnits: number;
}

export interface Overview {
  totals: {
    cases: number; courses: number; institutions: number; catalogueCourses: number; approvedEquivalencies: number;
    documents: number; finalDecisions: number; awardedUnits: number;
  };
  casesByStatus: Record<string, number>; coursesByStatus: Record<string, number>; matchLevels: Record<string, number>;
  decisionOutcomes: Record<string, number>; documentsByType: Record<string, number>;
  resolution: { approvedRule: number; cache: number; newRun: number; failed: number };
  casesByProgram: { name: string; count: number }[]; coursesByInstitution: { name: string; count: number }[];
  activity: { date: string; cases: number; documents: number; decisions: number }[];
}

/** One page of a server-paged list. */
export interface Paged<T> {
  items: T[]; total: number; page: number; pageSize: number;
}
