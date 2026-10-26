using System.Text.Json;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Contracts;

// ---------- Requests ----------

public record CreateCaseRequest(string StudentRef, string StudentName, string? StudentEmail, string Program, DateOnly StartDate);

public record ImportCourse(
    int InstitutionId, string Code, string Title, decimal Credits,
    string? Grade, string? Term, string? Description, string? SyllabusText);

public record ImportCoursesRequest(List<ImportCourse> Courses);

public record UpdateSyllabusRequest(string Text);

public record CreateEvaluationRequest(
    int SourceCourseId, OutcomeType OutcomeType, int? BuCourseId, decimal? AwardedUnits, string Rationale);

public record AssignIntakeRequest(int CaseId);

public record CreateInstitutionRequest(string Name, CreditSystem CreditSystem, string? CatalogueSource);

public record CreateDemoCaseRequest(string StudentRef);

public record UpdateSettingRequest(string Value);

// ---------- Responses ----------

public record DemoUserDto(string Id, string DisplayName, UserRole Role, string? StudentRef);

public record InstitutionDto(int Id, string Name, CreditSystem CreditSystem, string? CatalogueSource);

public record BuCourseDto(
    int Id, string Code, string Title, string Program, string RequirementType, decimal Credits,
    string? Description, VerifiedStatus VerifiedStatus, string? SourceUrl, string? BuOnlyRule);

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
    CandidateDto? TopCandidate, EvaluationDto? Evaluation);

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

public record StudentCourseDto(
    int Id, string Code, string Title, string Institution, string State, bool SyllabusNeeded,
    decimal AwardedUnits, string? Outcome);

public record TimelineEntryDto(DateTimeOffset Timestamp, string Message);

/// <summary>Student-safe view: no reviewer notes, candidates or rationale (least privilege).</summary>
public record StudentStatusDto(
    int CaseId, string CaseRef, string Program, DateOnly StartDate, DateOnly DeadlineDate, int DaysRemaining,
    bool Overdue, CaseStatus Status, string NextAction, decimal AwardedUnits, decimal PendingUnitsShown,
    List<StudentCourseDto> Courses, List<TimelineEntryDto> Timeline, List<DocumentDto> Documents);

public record IntakeMessageDto(
    int Id, IntakeChannel Channel, string SenderAddress, string Subject, DateTimeOffset ReceivedAt,
    int? CaseId, IntakeMatchStatus MatchStatus, string? AssignedBy, List<DocumentDto> Documents);

public record DocumentLinkDto(string Url, string Kind, DateTimeOffset ExpiresAt);

public record MetricsDto(
    int Cases, Dictionary<string, int> CasesByStatus, int Courses, int MatchRuns, int AiCalls, int ApprovedRuleHits,
    int CacheHits, double ReuseRate, int FinalDecisions, int FinalDecisionsWithReviewer, int AgentRunsWithModelRecord,
    int FailedAgentRuns, int CasesWithDeadline, Dictionary<string, int> CoursesByStatus);
