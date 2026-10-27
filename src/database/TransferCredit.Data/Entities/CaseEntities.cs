namespace TransferCredit.Data.Entities;

/// <summary>Demo-only user. Production uses Microsoft Entra ID; see README.</summary>
public class DemoUser
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; }
    public string? Email { get; set; }

    /// <summary>For students: the synthetic student reference that links them to their cases.</summary>
    public string? StudentRef { get; set; }
}

public class TransferCase
{
    public int Id { get; set; }
    public string CaseRef { get; set; } = "";
    public string DemoStudentRef { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string? StudentEmail { get; set; }
    public string Program { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly DeadlineDate { get; set; }
    public CaseStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }

    public List<SourceCourse> Courses { get; set; } = [];
    public List<Document> Documents { get; set; } = [];
}

public class SourceInstitution
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CreditSystem CreditSystem { get; set; }
    public string? CatalogueSource { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}

public class SourceCourse
{
    public int Id { get; set; }
    public int CaseId { get; set; }
    public TransferCase? Case { get; set; }
    public int InstitutionId { get; set; }
    public SourceInstitution? Institution { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal Credits { get; set; }
    public CreditSystem CreditSystem { get; set; }

    /// <summary>Semester-equivalent credits, calculated by the rules engine (BR-03).</summary>
    public decimal ConvertedCredits { get; set; }
    public string? Grade { get; set; }
    public string? Term { get; set; }
    public string? Description { get; set; }
    public string? LearningOutcomes { get; set; }

    /// <summary>Syllabus evidence: extracted from a document or entered by staff. Untrusted text.</summary>
    public string? SyllabusText { get; set; }
    public int? SyllabusDocumentId { get; set; }
    public CourseStatus Status { get; set; }
    public MatchLevel? MatchLevel { get; set; }

    /// <summary>Approved equivalency found for this course by the last match run (BR-07).</summary>
    public int? AppliedEquivalencyRuleId { get; set; }
    public EquivalencyRule? AppliedEquivalencyRule { get; set; }

    /// <summary>Policy flags from the rules engine at the last match run, as JSON.</summary>
    public string? PolicyFlagsJson { get; set; }

    public List<MatchCandidate> Candidates { get; set; } = [];
    public List<Evaluation> Evaluations { get; set; } = [];
}

public class Document
{
    public int Id { get; set; }
    public int? CaseId { get; set; }
    public TransferCase? Case { get; set; }
    public int? SourceCourseId { get; set; }
    public DocumentType DocumentType { get; set; }
    public DocumentSource Source { get; set; }
    public int? IntakeMessageId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Container { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public ScanStatus ScanStatus { get; set; }
    public ExtractionStatus ExtractionStatus { get; set; }
    public string? UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

/// <summary>One row per email (or imported .eml). Stops duplicate processing and drives the triage queue.</summary>
public class IntakeMessage
{
    public int Id { get; set; }
    public IntakeChannel Channel { get; set; }
    public string? GraphMessageId { get; set; }
    public string InternetMessageId { get; set; } = "";
    public string SenderAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public int? CaseId { get; set; }
    public IntakeMatchStatus MatchStatus { get; set; }
    public string? AssignedBy { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public List<Document> Documents { get; set; } = [];
}

public class MailSyncState
{
    public string MailboxAddress { get; set; } = "";
    public string FolderId { get; set; } = "inbox";
    public string? DeltaLink { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastStatus { get; set; }
}

public class AuditEvent
{
    public long Id { get; set; }
    public string ActorId { get; set; } = "";
    public int? CaseId { get; set; }
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>Safe text for the student timeline. Null means the event is internal only.</summary>
    public string? StudentMessage { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? CorrelationId { get; set; }
}
