using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Agent;
using TransferCredit.Api.Auth;
using TransferCredit.Api.Contracts;
using TransferCredit.Api.Rules;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Services;

public class ForbiddenActionException(string message) : Exception(message);

/// <summary>
/// Records the human decision. Only an authorised evaluator finalises (BR-01); staff may only confirm
/// an existing approved equivalency (BR-07). Finalising saves the outcome for reuse (BR-14).
/// </summary>
public class EvaluationService(
    TransferCreditDbContext db,
    RuleSettingsService rules,
    MatchingService matching,
    CaseService cases,
    AuditService audit,
    IAgentClient agent,
    TimeProvider clock,
    ILogger<EvaluationService> log)
{
    public async Task<Evaluation> SaveDraftAsync(CreateEvaluationRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var course = await db.SourceCourses.Include(c => c.Case)
            .FirstOrDefaultAsync(c => c.Id == request.SourceCourseId, ct)
            ?? throw new KeyNotFoundException($"Course {request.SourceCourseId} not found.");
        if (course.Status == CourseStatus.Decided)
            throw new ValidationFailedException("This course already has a final decision.");

        var units = await ValidateAsync(request, course, ct);

        var draft = await db.Evaluations.FirstOrDefaultAsync(
            e => e.SourceCourseId == course.Id && e.Status == EvaluationStatus.Draft, ct);
        if (draft is null)
        {
            draft = new Evaluation { SourceCourseId = course.Id, CreatedAt = clock.GetUtcNow() };
            db.Evaluations.Add(draft);
        }
        draft.EvaluatorId = user.Id();
        draft.OutcomeType = request.OutcomeType;
        draft.BUCourseId = request.OutcomeType == OutcomeType.BuEquivalent ? request.BuCourseId : null;
        draft.AwardedUnits = units;
        draft.Rationale = request.Rationale.Trim();
        draft.Status = EvaluationStatus.Draft;

        audit.Record(user.Id(), course.CaseId, AuditActions.EvaluationDrafted,
            $"{course.Code}: draft {request.OutcomeType} ({units} units).");
        await db.SaveChangesAsync(ct);
        return draft;
    }

    private async Task<decimal> ValidateAsync(CreateEvaluationRequest request, SourceCourse course, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Rationale))
            throw new ValidationFailedException("A rationale is required for every decision.");

        if (request.OutcomeType == OutcomeType.BuEquivalent)
        {
            if (request.BuCourseId is not { } buId)
                throw new ValidationFailedException("Choose the BU course for a BU-equivalent outcome.");
            if (!await db.BUCourses.AnyAsync(c => c.Id == buId, ct))
                throw new ValidationFailedException("Unknown BU course.");
            // BR-10: a course that must be taken at BU can never be awarded as a transfer equivalent.
            if (await rules.BuOnlyRuleAsync(buId, course.Case!.Program, ct) is { } rule)
                throw new ValidationFailedException($"Blocked by program rule: {rule}");
        }

        var units = request.OutcomeType == OutcomeType.NoCredit ? 0m : request.AwardedUnits ?? course.ConvertedCredits;
        if (units < 0 || units > 20) throw new ValidationFailedException("Awarded units must be between 0 and 20.");
        // BR-09: elective credit "counts as units". Zero units is the no-credit outcome.
        if (request.OutcomeType != OutcomeType.NoCredit && units == 0)
            throw new ValidationFailedException("A BU equivalent or elective credit must award more than 0 units. Choose No credit for 0 units.");
        return units;
    }

    public async Task<Evaluation> FinaliseAsync(int evaluationId, ClaimsPrincipal user, CancellationToken ct)
    {
        var evaluation = await db.Evaluations
            .Include(e => e.SourceCourse).ThenInclude(c => c!.Case)
            .Include(e => e.SourceCourse).ThenInclude(c => c!.Institution)
            .Include(e => e.BUCourse)
            .FirstOrDefaultAsync(e => e.Id == evaluationId, ct)
            ?? throw new KeyNotFoundException($"Evaluation {evaluationId} not found.");
        if (evaluation.Status == EvaluationStatus.Final)
            throw new ValidationFailedException("Evaluation is already final.");

        var course = evaluation.SourceCourse!;
        var program = course.Case!.Program;
        await EnsureMayFinaliseAsync(user, evaluation, course, ct);

        // Re-validate at finalise time in case rules changed after the draft.
        await ValidateAsync(new CreateEvaluationRequest(course.Id, evaluation.OutcomeType, evaluation.BUCourseId,
            evaluation.AwardedUnits, evaluation.Rationale), course, ct);

        var now = clock.GetUtcNow();
        evaluation.Status = EvaluationStatus.Final;
        evaluation.EvaluatorId = user.Id();
        evaluation.DecisionAt = now;
        course.Status = CourseStatus.Decided;

        var outcomeText = evaluation.OutcomeType switch
        {
            OutcomeType.BuEquivalent => $"BU equivalent {evaluation.BUCourse!.Code}",
            OutcomeType.ElectiveCredit => "elective credit",
            _ => "no credit"
        };
        audit.Record(user.Id(), course.CaseId, AuditActions.EvaluationFinalised,
            $"{course.Code}: FINAL {evaluation.OutcomeType} ({evaluation.AwardedUnits} units) by {user.Identity?.Name}. " +
            $"Rationale: {evaluation.Rationale}",
            newValue: evaluation.OutcomeType.ToString(),
            studentMessage: $"Decision for {course.Code}: {outcomeText}, {evaluation.AwardedUnits} units.");

        await SaveForReuseAsync(evaluation, course, program, user, ct);
        await db.SaveChangesAsync(ct);
        await cases.RefreshStatusAsync(course.CaseId, ct);
        await ResumeAgentGraphAsync(evaluation, course, user, ct);
        return evaluation;
    }

    private async Task EnsureMayFinaliseAsync(ClaimsPrincipal user, Evaluation evaluation, SourceCourse course, CancellationToken ct)
    {
        if (user.IsInRole(nameof(UserRole.Evaluator)) || user.IsInRole(nameof(UserRole.Admin))) return;

        // Staff may only confirm an approved equivalency exactly as it stands, including the units it implies.
        if (user.IsInRole(nameof(UserRole.Staff)) && course.AppliedEquivalencyRuleId is { } ruleId)
        {
            var rule = await db.EquivalencyRules.AsNoTracking().FirstAsync(r => r.Id == ruleId, ct);
            if (rule.OutcomeType == evaluation.OutcomeType && rule.BUCourseId == evaluation.BUCourseId
                && evaluation.AwardedUnits == UnitsFor(rule.OutcomeType, course)) return;
        }
        throw new ForbiddenActionException(
            "Only a department evaluator can finalise this decision. Staff can only confirm an existing approved equivalency as it stands.");
    }

    /// <summary>Units an approved equivalency awards this student: converted credits, or 0 for no credit.</summary>
    public static decimal UnitsFor(OutcomeType outcome, SourceCourse course) =>
        outcome == OutcomeType.NoCredit ? 0m : course.ConvertedCredits;

    /// <summary>Write-back: the next student with identical evidence resolves without an AI call.</summary>
    private async Task SaveForReuseAsync(Evaluation evaluation, SourceCourse course, string program, ClaimsPrincipal user, CancellationToken ct)
    {
        // The reuse key covers the course, not the student. When this student's grade failed or could not be checked,
        // the decision may rest on that grade, so saving it would hand the same outcome to the next student (BR-14).
        var flags = await rules.ComputeFlagsAsync(course, program, ct);
        if (flags.GradeBelowMinimum != false)
        {
            audit.Record(user.Id(), course.CaseId, AuditActions.EquivalencyNotSaved,
                $"{course.Code}: decision not saved for reuse because the grade ({course.Grade ?? "missing"}) " +
                $"{(flags.GradeBelowMinimum == true ? "is below" : "could not be checked against")} the minimum {flags.MinimumGrade}.");
            return;
        }

        if (course.AppliedEquivalencyRuleId is { } appliedId)
        {
            var applied = await db.EquivalencyRules.AsNoTracking().FirstAsync(r => r.Id == appliedId, ct);
            if (applied.OutcomeType == evaluation.OutcomeType && applied.BUCourseId == evaluation.BUCourseId)
                return; // confirmed an existing approved mapping; nothing new to save
        }

        var key = await matching.CacheKeyForAsync(course, program, ct);
        var exists = await db.EquivalencyRules.AnyAsync(r => r.CacheKey == key && r.Program == program
                                                             && r.ApprovalStatus == ApprovalStatus.Approved, ct);
        if (exists) return;

        var rule = new EquivalencyRule
        {
            InstitutionId = course.InstitutionId,
            SourceCode = RulesEngine.NormaliseCode(course.Code),
            SourceTitle = course.Title,
            Program = program,
            OutcomeType = evaluation.OutcomeType,
            BUCourseId = evaluation.BUCourseId,
            ApprovalStatus = ApprovalStatus.Approved,
            VerifiedStatus = VerifiedStatus.Yes,
            EffectiveFrom = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            DecisionSource = $"Evaluator decision #{evaluation.Id} by {user.Identity?.Name}",
            CreatedFromEvaluationId = evaluation.Id,
            CacheKey = key,
            CreatedAt = clock.GetUtcNow()
        };
        db.EquivalencyRules.Add(rule);
        audit.Record(user.Id(), course.CaseId, AuditActions.EquivalencySaved,
            $"{course.Code}: saved as approved equivalency for {program} (reuse key {key[..19]}...).");
    }

    /// <summary>Best effort: let the paused LangGraph thread complete. The decision is already stored.</summary>
    private async Task ResumeAgentGraphAsync(Evaluation evaluation, SourceCourse course, ClaimsPrincipal user, CancellationToken ct)
    {
        var threadId = await db.AgentRuns.AsNoTracking()
            .Where(r => r.SourceCourseId == course.Id && r.Status == AgentRunStatus.PausedForReview && r.ThreadId != null)
            .OrderByDescending(r => r.Id).Select(r => r.ThreadId).FirstOrDefaultAsync(ct);
        if (threadId is null) return;
        try
        {
            await agent.ResumeAsync(new AgentResumeRequest(threadId, evaluation.OutcomeType.ToString(),
                evaluation.BUCourse?.Code, user.Id()), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Could not resume agent thread {ThreadId}", threadId);
        }
    }
}
