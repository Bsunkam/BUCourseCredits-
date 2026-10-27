namespace TransferCredit.Data.Entities;

public class BUCourse
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Program { get; set; } = "";
    public string RequirementType { get; set; } = "";
    public decimal Credits { get; set; }
    public string? Description { get; set; }

    /// <summary>Learning outcomes, compared with the source course by the evaluator and the agent.</summary>
    public string? LearningOutcomes { get; set; }
    public VerifiedStatus VerifiedStatus { get; set; }
    public string? SourceUrl { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

/// <summary>
/// A course from another institution's public catalogue. Used to fill in the description and learning
/// outcomes when a transcript only lists code and title, so evaluators have evidence before a syllabus arrives.
/// </summary>
public class SourceCatalogueCourse
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public SourceInstitution? Institution { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal Credits { get; set; }
    public string? Description { get; set; }
    public string? LearningOutcomes { get; set; }
}

/// <summary>Program-specific rules such as "must be taken at BU" (BR-10).</summary>
public class ProgramRule
{
    public int Id { get; set; }
    public string Program { get; set; } = "";
    public int BUCourseId { get; set; }
    public BUCourse? BUCourse { get; set; }
    public ProgramRuleType RuleType { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>Configurable rule values (grade thresholds, rounding, versions).</summary>
public class RuleSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>An approved mapping from a source course to a BU outcome (BR-07, BR-14).</summary>
public class EquivalencyRule
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public SourceInstitution? Institution { get; set; }
    public string SourceCode { get; set; } = "";
    public string SourceTitle { get; set; } = "";
    public string Program { get; set; } = "";
    public OutcomeType OutcomeType { get; set; }
    public int? BUCourseId { get; set; }
    public BUCourse? BUCourse { get; set; }
    public ApprovalStatus ApprovalStatus { get; set; }
    public VerifiedStatus VerifiedStatus { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string DecisionSource { get; set; } = "";
    public int? CreatedFromEvaluationId { get; set; }

    /// <summary>Cache key of the course evidence the decision was made on. Null for seeded reference mappings.</summary>
    public string? CacheKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class MatchCandidate
{
    public int Id { get; set; }
    public int SourceCourseId { get; set; }
    public int BUCourseId { get; set; }
    public BUCourse? BUCourse { get; set; }
    public MatchLevel MatchLevel { get; set; }
    public decimal? Score { get; set; }
    public string? Confidence { get; set; }
    public string? EvidenceJson { get; set; }
    public string? Explanation { get; set; }
    public string? RuleFlagsJson { get; set; }
    public string MatchVersion { get; set; } = "";
    public CandidateSource Source { get; set; }
    public bool IsBlocked { get; set; }
    public int? AgentRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class AgentRun
{
    public int Id { get; set; }
    public int SourceCourseId { get; set; }
    public string CacheKey { get; set; } = "";
    public AgentRunSource Source { get; set; }
    public AgentRunStatus Status { get; set; }
    public string? GraphVersion { get; set; }
    public string? PromptVersion { get; set; }
    public string? Model { get; set; }
    public string? ThreadId { get; set; }
    public string? InputHash { get; set; }
    public string? OutputJson { get; set; }
    public string? Error { get; set; }
    public int LatencyMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Evaluation
{
    public int Id { get; set; }
    public int SourceCourseId { get; set; }
    public SourceCourse? SourceCourse { get; set; }
    public string EvaluatorId { get; set; } = "";
    public OutcomeType OutcomeType { get; set; }
    public int? BUCourseId { get; set; }
    public BUCourse? BUCourse { get; set; }
    public decimal AwardedUnits { get; set; }
    public string Rationale { get; set; } = "";
    public EvaluationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
}
