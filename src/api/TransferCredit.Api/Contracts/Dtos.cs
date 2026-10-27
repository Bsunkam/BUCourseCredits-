using System.Text.Json;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Contracts;

// ---------- Requests ----------

public record CreateCaseRequest(string StudentRef, string StudentName, string? StudentEmail, string Program, DateOnly StartDate);

public record ImportCourse(
    int InstitutionId, string Code, string Title, decimal Credits,
    string? Grade, string? Term, string? Description, string? SyllabusText, string? LearningOutcomes = null);

public record ImportCoursesRequest(List<ImportCourse> Courses);

public record UpdateSyllabusRequest(string Text);

public record CreateEvaluationRequest(
    int SourceCourseId, OutcomeType OutcomeType, int? BuCourseId, decimal? AwardedUnits, string Rationale);

public record AssignIntakeRequest(int CaseId);

public record CreateInstitutionRequest(string Name, CreditSystem CreditSystem, string? CatalogueSource);

/// <summary>Template: "community-college" (the brief's six-course scenario, default) or "northeastern".</summary>
public record CreateDemoCaseRequest(string StudentRef, string? Template = null);

public record UpdateSettingRequest(string Value);

// ---------- Responses ----------

public record DemoUserDto(string Id, string DisplayName, UserRole Role, string? StudentRef);

public record InstitutionDto(int Id, string Name, CreditSystem CreditSystem, string? CatalogueSource);

public record BuCourseDto(
    int Id, string Code, string Title, string Program, string RequirementType, decimal Credits,
    string? Description, VerifiedStatus VerifiedStatus, string? SourceUrl, string? BuOnlyRule,
    string? LearningOutcomes = null);

public record CaseSummaryDto(
    int Id, string CaseRef, string StudentName, string StudentRef, string Program, DateOnly StartDate,
    DateOnly DeadlineDate, int DaysRemaining, bool Overdue, CaseStatus Status, int CourseCount,
    int OpenCourseCount, DateTimeOffset CreatedAt);

public record AppliedRuleDto(
    int Id, OutcomeType OutcomeType, BuCourseDto? BuCourse, VerifiedStatus VerifiedStatus,
    string DecisionSource, bool FromEvaluation);

public record CandidateDto(
    int Id, BuCourseDto BuCourse, string MatchLevel, decimal? Score, string? Confidence,
    JsonElement? Evidence, string? Explanation, CandidateSource Source, bool IsBlocked, string MatchVersion);

public record EvaluationDto(
    int Id, int SourceCourseId, string EvaluatorId, OutcomeType OutcomeType, BuCourseDto? BuCourse,
    decimal AwardedUnits, string Rationale, EvaluationStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? DecisionAt);

public record CourseDto(
    int Id, int InstitutionId, string Institution, string Code, string Title, decimal Credits,
    CreditSystem CreditSystem, decimal ConvertedCredits, string? Grade, string? Term, string? Description,
    bool HasSyllabus, CourseStatus Status, string? MatchLevel, AppliedRuleDto? AppliedRule,
    CandidateDto? TopCandidate, EvaluationDto? Evaluation, string? LearningOutcomes, string? PostedGrade,
    decimal HubUnits);

public record AgentRunDto(
    int Id, AgentRunSource Source, AgentRunStatus Status, string? Model, string? GraphVersion,
    string? PromptVersion, int LatencyMs, string? Note, string CacheKey, DateTimeOffset CreatedAt);

public record CourseDetailDto(
    CourseDto Course, int CaseId, string CaseRef, string Program, string? SyllabusText,
    JsonElement? PolicyFlags, List<CandidateDto> Candidates, List<EvaluationDto> Evaluations,
    List<AgentRunDto> AgentRuns, string CacheKey);

public record DocumentDto(
    int Id, int? CaseId, int? SourceCourseId, DocumentType DocumentType, DocumentSource Source,
    string FileName, string ContentType, long SizeBytes, string Sha256, ScanStatus ScanStatus,
    ExtractionStatus ExtractionStatus, DateTimeOffset UploadedAt);

public record AuditDto(long Id, DateTimeOffset Timestamp, string ActorId, string Action, string? Detail, string? StudentMessage);

public record CaseDetailDto(
    CaseSummaryDto Summary, string? StudentEmail, DateTimeOffset? PostedAt,
    List<CourseDto> Courses, List<DocumentDto> Documents, List<AuditDto> Timeline);

/// <summary>
/// One course in the student view. AwardedUnits stays 0 until a final decision (BR-02); PotentialUnits is what the
/// course could be worth after conversion, shown separately and never as awarded credit.
/// Standing: "Secured", "Pending review", "At risk" or "No credit".
/// </summary>
public record StudentCourseDto(
    int Id, string Code, string Title, string Institution, string State, bool SyllabusNeeded,
    decimal AwardedUnits, string? Outcome, decimal PotentialUnits, string Standing, string? StandingReason,
    string? Requirement, string? SyllabusReason, string? PostedGrade, decimal HubUnits);

public record TimelineEntryDto(DateTimeOffset Timestamp, string Message);

public record RequirementProgressDto(string Requirement, decimal Units, int Courses);

/// <summary>How much of the student's transfer credit is secure: decided, under review, or at risk.</summary>
public record DegreeProgressDto(
    decimal SecuredUnits, decimal PendingUnits, decimal AtRiskUnits, int CoursesNeedingAction,
    List<RequirementProgressDto> SecuredByRequirement);

/// <summary>Student-safe view: no reviewer notes, candidates or rationale (least privilege).</summary>
public record StudentStatusDto(
    int CaseId, string CaseRef, string Program, DateOnly StartDate, DateOnly DeadlineDate, int DaysRemaining,
    bool Overdue, CaseStatus Status, string NextAction, decimal AwardedUnits, decimal PendingUnitsShown,
    List<StudentCourseDto> Courses, List<TimelineEntryDto> Timeline, List<DocumentDto> Documents,
    DegreeProgressDto Progress, List<string> PolicyNotes, DateTimeOffset? PostedAt);

public record IntakeMessageDto(
    int Id, IntakeChannel Channel, string SenderAddress, string Subject, DateTimeOffset ReceivedAt,
    int? CaseId, IntakeMatchStatus MatchStatus, string? AssignedBy, List<DocumentDto> Documents);

public record DocumentLinkDto(string Url, string Kind, DateTimeOffset ExpiresAt);

/// <summary>
/// FinalDecisions counts human decisions only; AppliedFromEquivalencyDb counts existing equivalencies applied without a
/// new review. MedianHoursAtZeroUnits is the primary outcome: how long a course shows 0 units, from case opened to
/// its final decision (null until there is one).
/// </summary>
public record MetricsDto(
    int Cases, Dictionary<string, int> CasesByStatus, int Courses, int MatchRuns, int AiCalls, int ApprovedRuleHits,
    int CacheHits, double ReuseRate, int FinalDecisions, int FinalDecisionsWithReviewer, int AgentRunsWithModelRecord,
    int FailedAgentRuns, int CasesWithDeadline, Dictionary<string, int> CoursesByStatus, int AppliedFromEquivalencyDb,
    double? MedianHoursAtZeroUnits, double? MedianHoursAtZeroUnitsReviewed, int CoursesStillAtZeroUnits);
