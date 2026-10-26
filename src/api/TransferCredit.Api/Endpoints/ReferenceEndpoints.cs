using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Agent;
using TransferCredit.Api.Auth;
using TransferCredit.Api.Contracts;
using TransferCredit.Api.Documents;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Endpoints;

public static class ReferenceEndpoints
{
    public static void MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var catalogue = app.MapGroup("/api/catalogue").WithTags("Catalogue & rules").RequireAuthorization();

        catalogue.MapGet("/search", async (string? q, string? program, TransferCreditDbContext db, CaseService cases, CancellationToken ct) =>
        {
            var query = db.BUCourses.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(program)) query = query.Where(c => c.Program == program);
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(c => c.Code.Contains(q) || c.Title.Contains(q) || (c.Description != null && c.Description.Contains(q)));
            var list = await query.OrderBy(c => c.Code).Take(100).ToListAsync(ct);
            var buOnly = string.IsNullOrWhiteSpace(program) ? new Dictionary<int, string>() : await cases.BuOnlyRulesAsync(program, ct);
            return list.Select(c => c.ToDto(buOnly.GetValueOrDefault(c.Id)));
        });

        catalogue.MapGet("/programs", async (TransferCreditDbContext db, CancellationToken ct) =>
            await db.BUCourses.Select(c => c.Program).Distinct().OrderBy(p => p).ToListAsync(ct));

        catalogue.MapGet("/equivalencies", async (string? program, TransferCreditDbContext db, CancellationToken ct) =>
        {
            var query = db.EquivalencyRules.AsNoTracking().Include(r => r.BUCourse).Include(r => r.Institution).AsQueryable();
            if (!string.IsNullOrWhiteSpace(program)) query = query.Where(r => r.Program == program);
            var list = await query.OrderByDescending(r => r.Id).Take(500).ToListAsync(ct);
            return list.Select(r => new
            {
                r.Id, Institution = r.Institution!.Name, r.SourceCode, r.SourceTitle, r.Program, r.OutcomeType,
                BuCourse = r.BUCourse?.ToDto(), r.ApprovalStatus, r.VerifiedStatus, r.EffectiveFrom, r.DecisionSource,
                FromEvaluation = r.CreatedFromEvaluationId != null, HasCacheKey = r.CacheKey != null
            });
        }).RequireAuthorization(Policies.Internal);

        catalogue.MapGet("/program-rules", async (TransferCreditDbContext db, CancellationToken ct) =>
            await db.ProgramRules.AsNoTracking().Include(r => r.BUCourse)
                .Select(r => new { r.Id, r.Program, BuCourse = r.BUCourse!.Code, r.BUCourse.Title, r.RuleType, r.Source })
                .ToListAsync(ct));

        var institutions = app.MapGroup("/api/institutions").WithTags("Catalogue & rules").RequireAuthorization();

        institutions.MapGet("", async (TransferCreditDbContext db, CancellationToken ct) =>
            await db.Institutions.AsNoTracking().OrderBy(i => i.Name)
                .Select(i => new InstitutionDto(i.Id, i.Name, i.CreditSystem, i.CatalogueSource)).ToListAsync(ct));

        institutions.MapPost("", async (CreateInstitutionRequest request, TransferCreditDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ValidationFailedException("Name is required.");
            if (await db.Institutions.AnyAsync(i => i.Name == request.Name.Trim(), ct))
                throw new ValidationFailedException("Institution already exists.");
            var i = new SourceInstitution { Name = request.Name.Trim(), CreditSystem = request.CreditSystem, CatalogueSource = request.CatalogueSource };
            db.Institutions.Add(i);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new InstitutionDto(i.Id, i.Name, i.CreditSystem, i.CatalogueSource));
        }).RequireAuthorization(Policies.Staff);

        var admin = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(Policies.Admin);

        admin.MapGet("/settings", async (TransferCreditDbContext db, CancellationToken ct) =>
            await db.RuleSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct));

        admin.MapPut("/settings/{key}", async (string key, UpdateSettingRequest request, TransferCreditDbContext db,
            AuditService audit, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var setting = await db.RuleSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (setting is null) return Results.NotFound();
            audit.Record(user.Id(), null, "RuleSettingChanged", key, setting.Value, request.Value);
            setting.Value = request.Value.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(setting);
        });

        app.MapGet("/api/metrics", async (TransferCreditDbContext db, CancellationToken ct) =>
        {
            var byStatus = await db.Cases.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var coursesByStatus = await db.SourceCourses.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var runs = await db.AgentRuns.AsNoTracking().Select(r => new { r.Source, r.Status, r.Model }).ToListAsync(ct);
            var finals = await db.Evaluations.Where(e => e.Status == EvaluationStatus.Final)
                .Select(e => e.EvaluatorId).ToListAsync(ct);

            var aiCalls = runs.Count(r => r.Source == AgentRunSource.NewRun && r.Status != AgentRunStatus.Skipped);
            var approved = runs.Count(r => r.Source == AgentRunSource.ApprovedRule);
            var cache = runs.Count(r => r.Source == AgentRunSource.Cache);
            var resolvable = aiCalls + approved + cache;

            return new MetricsDto(
                byStatus.Sum(x => x.Count),
                byStatus.ToDictionary(x => x.Key.ToString(), x => x.Count),
                await db.SourceCourses.CountAsync(ct),
                runs.Count,
                aiCalls,
                approved,
                cache,
                resolvable == 0 ? 0 : Math.Round((double)(approved + cache) / resolvable, 3),
                finals.Count,
                finals.Count(id => !string.IsNullOrWhiteSpace(id)),
                runs.Count(r => r.Source == AgentRunSource.NewRun && r.Status != AgentRunStatus.Skipped && r.Model != null),
                runs.Count(r => r.Status == AgentRunStatus.Failed),
                await db.Cases.CountAsync(ct),
                coursesByStatus.ToDictionary(x => x.Key.ToString(), x => x.Count));
        }).RequireAuthorization(Policies.Internal).WithTags("Reporting");

        app.MapGet("/api/agent-runs", async (int? take, TransferCreditDbContext db, CancellationToken ct) =>
            (await db.AgentRuns.AsNoTracking().OrderByDescending(r => r.Id).Take(Math.Clamp(take ?? 50, 1, 500)).ToListAsync(ct))
                .Select(r => r.ToDto()))
            .RequireAuthorization(Policies.Internal).WithTags("Reporting");

        // ---------- Demo helpers ----------
        var demo = app.MapGroup("/api/demo").WithTags("Demo");

        demo.MapGet("/users", async (TransferCreditDbContext db, CancellationToken ct) =>
            await db.DemoUsers.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.Id)
                .Select(u => new DemoUserDto(u.Id, u.DisplayName, u.Role, u.StudentRef)).ToListAsync(ct));

        demo.MapPost("/cases", async (CreateDemoCaseRequest request, CaseService cases, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var c = await cases.CreateDemoCaseAsync(request.StudentRef, user.Id(), ct);
            return Results.Ok(new { c.Id, c.CaseRef });
        }).RequireAuthorization(Policies.Staff);

        app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = user.Id(), name = user.Identity?.Name, role = user.FindFirstValue(ClaimTypes.Role), studentRef = user.StudentRef()
        })).RequireAuthorization().WithTags("Demo");

        // ---------- Health (used by the pipeline smoke test; no auth) ----------
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Health");

        app.MapGet("/api/health/agent", async (IAgentClient agent, CancellationToken ct) =>
            await agent.IsHealthyAsync(ct)
                ? Results.Ok(new { status = "ok", mode = agent.Mode })
                : Results.Json(new { status = "unreachable", mode = agent.Mode }, statusCode: 503)).WithTags("Health");

        app.MapGet("/api/health/intake", async (IDocumentStorage storage, Microsoft.Extensions.Options.IOptions<IntakeOptions> intake,
            TransferCreditDbContext db, CancellationToken ct) =>
        {
            var storageOk = await storage.IsReachableAsync(ct);
            string mailbox;
            var mailboxOk = true;
            if (!intake.Value.Enabled) mailbox = "disabled";
            else
            {
                var state = await db.MailSyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.MailboxAddress == intake.Value.Mailbox, ct);
                mailbox = state?.LastStatus ?? "not polled yet";
                mailboxOk = state?.LastStatus?.StartsWith("Error") != true;
            }
            var body = new { status = storageOk && mailboxOk ? "ok" : "degraded", storage = storage.Kind, storageOk, mailbox };
            return storageOk && mailboxOk ? Results.Ok(body) : Results.Json(body, statusCode: 503);
        }).WithTags("Health");
    }
}
