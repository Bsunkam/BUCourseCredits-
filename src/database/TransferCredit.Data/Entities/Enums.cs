namespace TransferCredit.Data.Entities;

public enum UserRole { Staff, Evaluator, Admin, Student, Advisor }

public enum CaseStatus { Open, AwaitingDocuments, InReview, Decided, Posted }

public enum CourseStatus
{
    Pending,
    Matching,
    ApprovedMatchFound,
    NeedsSyllabus,
    NeedsReview,
    Blocked,
    Decided
}

public enum CreditSystem { Semester, Quarter }

/// <summary>Workbook match levels (BR-11). Never collapse Partial into approved.</summary>
public enum MatchLevel
{
    LikelyEquivalent,
    PartialSyllabusReview,
    NoEquivalentFound,
    MustBeTakenAtBu
}

/// <summary>Catalogue verification (BR-12): Yes = code and title confirmed, Check = needs confirmation.</summary>
public enum VerifiedStatus { Yes, Check }

public enum ApprovalStatus { Approved, Revoked }

public enum ProgramRuleType { MustBeTakenAtBu }

public enum CandidateSource { ApprovedRule, Cache, Agent, Stub }

public enum AgentRunSource { ApprovedRule, Cache, NewRun }

public enum AgentRunStatus { Succeeded, Failed, Skipped, PausedForReview }

/// <summary>Evaluator outcomes (BR-09).</summary>
public enum OutcomeType { BuEquivalent, ElectiveCredit, NoCredit }

public enum EvaluationStatus { Draft, Final }

public enum DocumentType { Transcript, Syllabus, Other }

public enum DocumentSource { Portal, Email, StaffUpload }

public enum ScanStatus { Pending, Clean, Quarantined }

public enum ExtractionStatus { NotStarted, Extracted, Failed, NotAvailable }

public enum IntakeChannel { Email, EmlImport }

public enum IntakeMatchStatus { Matched, Unmatched, Ambiguous }
