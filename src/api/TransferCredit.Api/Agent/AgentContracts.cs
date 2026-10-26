using System.Text.Json.Serialization;
using TransferCredit.Api.Rules;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Agent;

// C# side of /contracts/agent-contract.schema.json. Keep both in step with the Pydantic models in src/agent.

public record AgentSourceCourse(
    [property: JsonPropertyName("institution")] string Institution,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("credits")] decimal Credits,
    [property: JsonPropertyName("creditSystem")] string CreditSystem,
    [property: JsonPropertyName("grade")] string? Grade,
    [property: JsonPropertyName("term")] string? Term,
    [property: JsonPropertyName("description")] string? Description);

public record AgentCatalogueCourse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("credits")] decimal Credits,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("verified")] string Verified);

public record AgentEvaluateRequest(
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("program")] string Program,
    [property: JsonPropertyName("sourceCourse")] AgentSourceCourse SourceCourse,
    [property: JsonPropertyName("syllabusText")] string? SyllabusText,
    [property: JsonPropertyName("policyFlags")] PolicyFlags PolicyFlags,
    [property: JsonPropertyName("candidates")] IReadOnlyList<AgentCatalogueCourse> Candidates,
    [property: JsonPropertyName("promptVersion")] string PromptVersion,
    [property: JsonPropertyName("cacheKey")] string CacheKey);

public record AgentEvidence(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("snippet")] string Snippet,
    [property: JsonPropertyName("matchesOutcome")] string? MatchesOutcome);

public record AgentCandidate(
    [property: JsonPropertyName("buCourseCode")] string BuCourseCode,
    [property: JsonPropertyName("matchLevel")] string MatchLevel,
    [property: JsonPropertyName("score")] decimal? Score,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("matchedAttributes")] IReadOnlyList<string> MatchedAttributes);

public record AgentEvaluateResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("matchLevel")] string MatchLevel,
    [property: JsonPropertyName("suggestedBuCourse")] string? SuggestedBuCourse,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("candidates")] IReadOnlyList<AgentCandidate> Candidates,
    [property: JsonPropertyName("evidence")] IReadOnlyList<AgentEvidence> Evidence,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("studentMessage")] string StudentMessage,
    [property: JsonPropertyName("insufficientEvidence")] bool InsufficientEvidence,
    [property: JsonPropertyName("needsHumanReview")] bool NeedsHumanReview,
    [property: JsonPropertyName("graphVersion")] string GraphVersion,
    [property: JsonPropertyName("promptVersion")] string PromptVersion,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("cacheKey")] string CacheKey);

public record AgentExtractRequest(
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("container")] string Container,
    [property: JsonPropertyName("blobPath")] string BlobPath,
    [property: JsonPropertyName("documentType")] string DocumentType);

public record AgentExtractedCourse(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("credits")] decimal? Credits,
    [property: JsonPropertyName("grade")] string? Grade,
    [property: JsonPropertyName("term")] string? Term);

public record AgentExtractResponse(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("courses")] IReadOnlyList<AgentExtractedCourse> Courses,
    [property: JsonPropertyName("missingFields")] IReadOnlyList<string> MissingFields,
    [property: JsonPropertyName("model")] string Model);

public record AgentResumeRequest(
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("outcomeType")] string OutcomeType,
    [property: JsonPropertyName("buCourseCode")] string? BuCourseCode,
    [property: JsonPropertyName("evaluatorId")] string EvaluatorId);

/// <summary>The agent returned something that does not match the shared contract.</summary>
public class AgentContractException(string message) : Exception(message);

public static class MatchLevelLabels
{
    // BR-11: keep the workbook labels on the wire and in the UI.
    private static readonly Dictionary<MatchLevel, string> Labels = new()
    {
        [MatchLevel.LikelyEquivalent] = "Likely equivalent",
        [MatchLevel.PartialSyllabusReview] = "Partial - syllabus review",
        [MatchLevel.NoEquivalentFound] = "No equivalent found",
        [MatchLevel.MustBeTakenAtBu] = "Must be taken at BU"
    };

    public static string ToLabel(MatchLevel level) => Labels[level];

    public static MatchLevel Parse(string label) =>
        Labels.FirstOrDefault(kv => string.Equals(kv.Value, label, StringComparison.OrdinalIgnoreCase)) is { Value: not null } hit
            ? hit.Key
            : throw new AgentContractException($"Unknown match level '{label}'.");
}
