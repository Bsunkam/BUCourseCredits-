using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransferCredit.Api.Agent;
using TransferCredit.Api.Rules;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Services;

public record MatchOutcome(CourseStatus Status, string Path, bool AiCalled);

/// <summary>
/// Runs the cheapest checks first and calls the agent service only as a last resort (design 9.3):
/// grade gate, approved equivalency, missing syllabus, cached suggestion, then a full agent run.
/// </summary>
public class MatchingService(
    TransferCreditDbContext db,
    RuleSettingsService rules,
    IAgentClient agent,
    AuditService audit,
    CaseService cases,
    IOptions<AgentOptions> options,
    TimeProvider clock,
    ILogger<MatchingService> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<string> CacheKeyForAsync(SourceCourse course, string program, CancellationToken ct)
    {
        var settings = await rules.GetAsync(ct);
        return CacheKey.For(course.Institution!.Name, course.Code, course.Title, course.Credits, course.CreditSystem,
            course.SyllabusText, program, settings.RulesVersion, options.Value.PromptVersion);
    }

    public async Task<MatchOutcome> MatchAsync(int courseId, string actorId, CancellationToken ct)
    {
        var course = await db.SourceCourses
            .Include(c => c.Case)
            .Include(c => c.Institution)
            .Include(c => c.Candidates)
            .FirstAsync(c => c.Id == courseId, ct);
        var program = course.Case!.Program;

        var flags = await rules.ComputeFlagsAsync(course, program, ct);
        var key = await CacheKeyForAsync(course, program, ct);
        var settings = await rules.GetAsync(ct);

        course.PolicyFlagsJson = JsonSerializer.Serialize(flags, Json);
        course.AppliedEquivalencyRuleId = null;
        db.MatchCandidates.RemoveRange(course.Candidates);
        course.Candidates.Clear();

        MatchOutcome outcome;
        if (flags.GradeBelowMinimum == true)
        {
            // Policy gate: the grade decides the path; no lookup or AI call is useful.
            course.Status = CourseStatus.NeedsReview;
            course.MatchLevel = null;
            AddSkippedRun(course, key, AgentRunSource.NewRun, $"Grade {course.Grade} is below the minimum {flags.MinimumGrade}.");
            audit.Record(actorId, course.CaseId, AuditActions.MatchCompleted,
                $"{course.Code}: grade {course.Grade} below minimum {flags.MinimumGrade}; routed to evaluator.",
                studentMessage: $"{course.Code} is under department review.");
            outcome = new(course.Status, "GradeBelowMinimum", false);
        }
        else if (await FindApprovedRuleAsync(course, program, key, ct) is { } rule)
        {
            outcome = await ApplyApprovedRuleAsync(course, program, rule, key, flags, actorId, ct);
        }
        else if (string.IsNullOrWhiteSpace(course.SyllabusText))
        {
            // BR-08: unknown match becomes "maybe" and needs a syllabus. Never infer from the title alone.
            course.Status = CourseStatus.NeedsSyllabus;
            course.MatchLevel = MatchLevel.PartialSyllabusReview;
            AddSkippedRun(course, key, AgentRunSource.NewRun, "No syllabus: matching agent skipped.");
            audit.Record(actorId, course.CaseId, AuditActions.SyllabusRequested,
                $"{course.Code}: no approved equivalency and no syllabus.",
                studentMessage: $"Please send the syllabus for {course.Code} {course.Title}.");
            outcome = new(course.Status, "NeedsSyllabus", false);
        }
        else if (await FindCachedRunAsync(key, ct) is { } cached)
        {
            var response = JsonSerializer.Deserialize<AgentEvaluateResponse>(cached.OutputJson!, Json)!;
            var run = AddRun(course, key, AgentRunSource.Cache, AgentRunStatus.Skipped, response, 0, null);
            await db.SaveChangesAsync(ct);
            await ApplyAgentResponseAsync(course, response, flags, CandidateSource.Cache, run.Id, ct);
            audit.Record(actorId, course.CaseId, AuditActions.MatchCompleted,
                $"{course.Code}: reused cached suggestion from run #{cached.Id} (no AI call). Still needs review.",
                studentMessage: $"{course.Code} is under department review.");
            outcome = new(course.Status, "Cache", false);
        }
        else
        {
            outcome = await RunAgentAsync(course, program, key, flags, settings, actorId, ct);
        }

        await db.SaveChangesAsync(ct);
        await cases.RefreshStatusAsync(course.CaseId, ct);
        return outcome;
    }

    private async Task<EquivalencyRule?> FindApprovedRuleAsync(SourceCourse course, string program, string key, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var code = RulesEngine.NormaliseCode(course.Code);
        var rules = await db.EquivalencyRules
            .Include(r => r.BUCourse)
            .Where(r => r.InstitutionId == course.InstitutionId && r.Program == program
                        && r.ApprovalStatus == ApprovalStatus.Approved
                        && r.EffectiveFrom <= today && (r.EffectiveTo == null || r.EffectiveTo >= today))
            .ToListAsync(ct);

        // Seeded reference mappings have no cache key. Evaluator decisions apply only to identical evidence (BR-14).
        return rules
            .Where(r => RulesEngine.NormaliseCode(r.SourceCode) == code && (r.CacheKey == null || r.CacheKey == key))
            .OrderByDescending(r => r.CacheKey == key)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<MatchOutcome> ApplyApprovedRuleAsync(
        SourceCourse course, string program, EquivalencyRule rule, string key, PolicyFlags flags, string actorId, CancellationToken ct)
    {
        course.AppliedEquivalencyRuleId = rule.Id;
        var blockedBy = rule.BUCourseId is { } buId ? await rules.BuOnlyRuleAsync(buId, program, ct) : null;

        if (rule.BUCourseId is not null)
        {
            course.Candidates.Add(new MatchCandidate
            {
                BUCourseId = rule.BUCourseId.Value,
                MatchLevel = blockedBy is null ? MatchLevel.LikelyEquivalent : MatchLevel.MustBeTakenAtBu,
                Confidence = "approved",
                Explanation = $"Approved equivalency #{rule.Id} ({rule.DecisionSource}). Catalogue verification: {rule.VerifiedStatus}." +
                              (blockedBy is null ? "" : $" Blocked: {blockedBy}"),
                RuleFlagsJson = JsonSerializer.Serialize(flags, Json),
                MatchVersion = flags.RulesVersion,
                Source = CandidateSource.ApprovedRule,
                IsBlocked = blockedBy is not null,
                CreatedAt = clock.GetUtcNow()
            });
        }

        course.Status = blockedBy is null ? CourseStatus.ApprovedMatchFound : CourseStatus.Blocked;
        course.MatchLevel = rule.OutcomeType switch
        {
            OutcomeType.BuEquivalent => blockedBy is null ? MatchLevel.LikelyEquivalent : MatchLevel.MustBeTakenAtBu,
            _ => MatchLevel.NoEquivalentFound
        };
        AddSkippedRun(course, key, AgentRunSource.ApprovedRule, $"Approved equivalency #{rule.Id} applied.");
        audit.Record(actorId, course.CaseId, AuditActions.MatchCompleted,
            $"{course.Code}: approved equivalency #{rule.Id} found (no AI call).",
            studentMessage: $"{course.Code}: an existing equivalency was found and is awaiting confirmation.");
        return new(course.Status, "ApprovedRule", false);
    }

    private Task<AgentRun?> FindCachedRunAsync(string key, CancellationToken ct)
    {
        var runs = db.AgentRuns.AsNoTracking()
            .Where(r => r.CacheKey == key && r.Source == AgentRunSource.NewRun && r.OutputJson != null
                        && (r.Status == AgentRunStatus.Succeeded || r.Status == AgentRunStatus.PausedForReview));
        // With the agent service, never pass off a keyword ranking ("... (not AI)") from mock or stub mode as an AI
        // suggestion: the course is matched again. The local stub may still reuse its own runs.
        if (agent.Mode != "stub")
            runs = runs.Where(r => r.Model != null && !r.Model.EndsWith(NotAiSuffix));
        return runs.OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
    }

    private const string NotAiSuffix = "(not AI)";

    private async Task<MatchOutcome> RunAgentAsync(
        SourceCourse course, string program, string key, PolicyFlags flags, RuleSettingsSnapshot settings,
        string actorId, CancellationToken ct)
    {
        var catalogue = await db.BUCourses.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
        var request = new AgentEvaluateRequest(
            CourseId: $"c-{course.Id}",
            ThreadId: $"course-{course.Id}-{Guid.NewGuid():N}",
            Program: program,
            SourceCourse: new AgentSourceCourse(course.Institution!.Name, course.Code, course.Title, course.Credits,
                course.CreditSystem.ToString().ToLowerInvariant(), course.Grade, course.Term, course.Description),
            SyllabusText: course.SyllabusText,
            PolicyFlags: flags,
            Candidates: catalogue.Select(c => new AgentCatalogueCourse(c.Code, c.Title, c.Credits, c.Description,
                c.VerifiedStatus.ToString())).ToList(),
            PromptVersion: options.Value.PromptVersion,
            CacheKey: key);

        var watch = Stopwatch.StartNew();
        try
        {
            var response = await agent.EvaluateAsync(request, ct);
            Validate(response, catalogue);
            var run = AddRun(course, key, AgentRunSource.NewRun,
                response.Status == "paused_for_review" ? AgentRunStatus.PausedForReview : AgentRunStatus.Succeeded,
                response, (int)watch.ElapsedMilliseconds, null);
            run.ThreadId = response.ThreadId;
            await db.SaveChangesAsync(ct);

            var source = agent.Mode == "stub" ? CandidateSource.Stub : CandidateSource.Agent;
            await ApplyAgentResponseAsync(course, response, flags, source, run.Id, ct);
            audit.Record(actorId, course.CaseId, AuditActions.MatchCompleted,
                $"{course.Code}: agent run #{run.Id} ({response.Model}) suggested {response.SuggestedBuCourse ?? "no match"} " +
                $"[{response.MatchLevel}, {response.Confidence}]. Needs human review.",
                studentMessage: $"{course.Code} is under department review.");
            return new(course.Status, "Agent", agent.Mode != "stub");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or AgentContractException or JsonException
                                       && !ct.IsCancellationRequested)
        {
            // The system must stay usable without AI: fall back to manual review and make the failure visible.
            log.LogWarning(ex, "Agent run failed for course {CourseId}", course.Id);
            AddRun(course, key, AgentRunSource.NewRun, AgentRunStatus.Failed, null, (int)watch.ElapsedMilliseconds, ex.Message);
            course.Status = CourseStatus.NeedsReview;
            course.MatchLevel = MatchLevel.PartialSyllabusReview;
            audit.Record(actorId, course.CaseId, AuditActions.AgentFailed,
                $"{course.Code}: agent service unavailable or invalid response ({ex.GetType().Name}). Manual review required.",
                studentMessage: $"{course.Code} is under department review.");
            return new(course.Status, "AgentFailed", false);
        }
    }

    private static void Validate(AgentEvaluateResponse r, List<BUCourse> catalogue)
    {
        var codes = catalogue.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        MatchLevelLabels.Parse(r.MatchLevel);
        if (r.Candidates is null) throw new AgentContractException("Missing candidates.");
        foreach (var c in r.Candidates)
        {
            MatchLevelLabels.Parse(c.MatchLevel);
            if (!codes.Contains(c.BuCourseCode))
                throw new AgentContractException($"Candidate {c.BuCourseCode} is not in the BU catalogue.");
        }
        if (r.SuggestedBuCourse is not null && !codes.Contains(r.SuggestedBuCourse))
            throw new AgentContractException($"Suggested course {r.SuggestedBuCourse} is not in the BU catalogue.");
        if (!r.NeedsHumanReview)
            throw new AgentContractException("Agent responses must always require human review (BR-01).");
    }

    private async Task ApplyAgentResponseAsync(
        SourceCourse course, AgentEvaluateResponse response, PolicyFlags flags,
        CandidateSource source, int runId, CancellationToken ct)
    {
        var codes = response.Candidates.Select(c => c.BuCourseCode).ToList();
        var buCourses = await db.BUCourses.Where(c => codes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, ct);
        var buOnly = flags.BuOnlyCourses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var evidenceJson = JsonSerializer.Serialize(response.Evidence, Json);
        var flagsJson = JsonSerializer.Serialize(flags, Json);

        var first = true;
        foreach (var c in response.Candidates)
        {
            var blocked = buOnly.Contains(c.BuCourseCode);
            course.Candidates.Add(new MatchCandidate
            {
                BUCourseId = buCourses[c.BuCourseCode].Id,
                // The program rule wins over whatever the model said (BR-10).
                MatchLevel = blocked ? MatchLevel.MustBeTakenAtBu : MatchLevelLabels.Parse(c.MatchLevel),
                Score = c.Score,
                Confidence = c.Confidence,
                EvidenceJson = first ? evidenceJson : JsonSerializer.Serialize(c.MatchedAttributes, Json),
                Explanation = first ? response.Explanation : string.Join("; ", c.MatchedAttributes),
                RuleFlagsJson = flagsJson,
                MatchVersion = $"{response.GraphVersion}/{response.PromptVersion}",
                Source = source,
                IsBlocked = blocked,
                AgentRunId = runId,
                CreatedAt = clock.GetUtcNow()
            });
            first = false;
        }

        var top = course.Candidates.FirstOrDefault();
        course.MatchLevel = top?.MatchLevel ?? MatchLevel.NoEquivalentFound;
        course.Status = top?.IsBlocked == true ? CourseStatus.Blocked : CourseStatus.NeedsReview;
    }

    private AgentRun AddRun(
        SourceCourse course, string key, AgentRunSource source, AgentRunStatus status,
        AgentEvaluateResponse? response, int latencyMs, string? error)
    {
        var run = new AgentRun
        {
            SourceCourseId = course.Id,
            CacheKey = key,
            Source = source,
            Status = status,
            GraphVersion = response?.GraphVersion,
            PromptVersion = response?.PromptVersion ?? options.Value.PromptVersion,
            Model = response?.Model,
            ThreadId = response?.ThreadId,
            InputHash = key,
            OutputJson = response is null ? null : JsonSerializer.Serialize(response, Json),
            Error = error,
            LatencyMs = latencyMs,
            CreatedAt = clock.GetUtcNow()
        };
        db.AgentRuns.Add(run);
        return run;
    }

    private void AddSkippedRun(SourceCourse course, string key, AgentRunSource source, string reason) =>
        AddRun(course, key, source, AgentRunStatus.Skipped, null, 0, reason);
}
