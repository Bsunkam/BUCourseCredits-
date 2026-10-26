using System.Text.Json;
using TransferCredit.Api.Agent;
using TransferCredit.Api.Rules;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Contracts;

public static class Mapping
{
    public static CaseSummaryDto ToSummary(this TransferCase c, DateOnly today) => new(
        c.Id, c.CaseRef, c.StudentName, c.DemoStudentRef, c.Program, c.StartDate, c.DeadlineDate,
        RulesEngine.DaysRemaining(c.DeadlineDate, today),
        c.Status != CaseStatus.Posted && RulesEngine.DaysRemaining(c.DeadlineDate, today) < 0,
        c.Status, c.Courses.Count, c.Courses.Count(x => x.Status != CourseStatus.Decided), c.CreatedAt);

    public static BuCourseDto ToDto(this BUCourse c, string? buOnlyRule = null) => new(
        c.Id, c.Code, c.Title, c.Program, c.RequirementType, c.Credits, c.Description, c.VerifiedStatus,
        c.SourceUrl, buOnlyRule);

    public static CandidateDto ToDto(this MatchCandidate c, IReadOnlyDictionary<int, string> buOnlyRules) => new(
        c.Id, c.BUCourse!.ToDto(buOnlyRules.GetValueOrDefault(c.BUCourseId)), MatchLevelLabels.ToLabel(c.MatchLevel),
        c.Score, c.Confidence, ParseJson(c.EvidenceJson), c.Explanation, c.Source, c.IsBlocked, c.MatchVersion);

    public static EvaluationDto ToDto(this Evaluation e) => new(
        e.Id, e.SourceCourseId, e.EvaluatorId, e.OutcomeType, e.BUCourse?.ToDto(), e.AwardedUnits, e.Rationale,
        e.Status, e.CreatedAt, e.DecisionAt);

    public static AppliedRuleDto ToDto(this EquivalencyRule r) => new(
        r.Id, r.OutcomeType, r.BUCourse?.ToDto(), r.VerifiedStatus, r.DecisionSource, r.CreatedFromEvaluationId is not null);

    public static CourseDto ToDto(this SourceCourse c, IReadOnlyDictionary<int, string> buOnlyRules)
    {
        var top = c.Candidates.OrderByDescending(x => x.Source == CandidateSource.ApprovedRule)
            .ThenBy(x => x.Id).FirstOrDefault();
        var evaluation = c.Evaluations.OrderByDescending(e => e.Status == EvaluationStatus.Final)
            .ThenByDescending(e => e.Id).FirstOrDefault();
        return new CourseDto(
            c.Id, c.InstitutionId, c.Institution?.Name ?? "", c.Code, c.Title, c.Credits, c.CreditSystem,
            c.ConvertedCredits, c.Grade, c.Term, c.Description, !string.IsNullOrWhiteSpace(c.SyllabusText),
            c.Status, c.MatchLevel is { } l ? MatchLevelLabels.ToLabel(l) : null,
            c.AppliedEquivalencyRule?.ToDto(), top?.ToDto(buOnlyRules), evaluation?.ToDto());
    }

    public static DocumentDto ToDto(this Document d) => new(
        d.Id, d.CaseId, d.SourceCourseId, d.DocumentType, d.Source, d.FileName, d.ContentType, d.SizeBytes,
        d.Sha256, d.ScanStatus, d.ExtractionStatus, d.UploadedAt);

    public static AuditDto ToDto(this AuditEvent a) =>
        new(a.Id, a.Timestamp, a.ActorId, a.Action, a.Detail, a.StudentMessage);

    public static AgentRunDto ToDto(this AgentRun r) => new(
        r.Id, r.Source, r.Status, r.Model, r.GraphVersion, r.PromptVersion, r.LatencyMs, r.Error, r.CacheKey, r.CreatedAt);

    public static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
