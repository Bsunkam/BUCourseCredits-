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

        // Newest first, one page at a time.
        app.MapGet("/api/agent-runs", async (int? page, int? pageSize, TransferCreditDbContext db, CancellationToken ct) =>
        {
            var size = Math.Clamp(pageSize ?? 10, 1, 100);
            var total = await db.AgentRuns.CountAsync(ct);
            var pages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
            var current = Math.Clamp(page ?? 1, 1, pages);
            var items = await db.AgentRuns.AsNoTracking().OrderByDescending(r => r.Id)
                .Skip((current - 1) * size).Take(size).ToListAsync(ct);
            return new { Items = items.Select(r => r.ToDto()), Total = total, Page = current, PageSize = size };
        }).RequireAuthorization(Policies.Internal).WithTags("Reporting");

        // ---------- Public overview (landing page before sign-in) ----------
        // Aggregate counts only: no student names, emails, case references or document content.
        app.MapGet("/api/overview", async (TransferCreditDbContext db, CancellationToken ct) =>
        {
            var casesByStatus = await db.Cases.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var coursesByStatus = await db.SourceCourses.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var matchLevels = await db.SourceCourses.GroupBy(c => c.MatchLevel).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var outcomes = await db.Evaluations.Where(e => e.Status == EvaluationStatus.Final)
                .GroupBy(e => e.OutcomeType).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var runs = await db.AgentRuns.GroupBy(r => new { r.Source, r.Status }).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var documents = await db.Documents.GroupBy(d => d.DocumentType).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var programs = await db.Cases.GroupBy(c => c.Program).Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ToListAsync(ct);
            var institutions = await db.SourceCourses.GroupBy(c => c.Institution!.Name).Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(8).ToListAsync(ct);

            // Daily activity for the last 14 days (UTC).
            var today = DateTime.UtcNow.Date;
            var since = new DateTimeOffset(today.AddDays(-13), TimeSpan.Zero);
            var caseDays = await db.Cases.Where(c => c.CreatedAt >= since).Select(c => c.CreatedAt).ToListAsync(ct);
            var documentDays = await db.Documents.Where(d => d.UploadedAt >= since).Select(d => d.UploadedAt).ToListAsync(ct);
            var decisionDays = await db.Evaluations.Where(e => e.Status == EvaluationStatus.Final && e.DecisionAt >= since)
                .Select(e => e.DecisionAt!.Value).ToListAsync(ct);
            int On(List<DateTimeOffset> times, DateTime day) => times.Count(t => t.UtcDateTime.Date == day);
            var activity = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13)).Select(day => new
            {
                Date = day.ToString("yyyy-MM-dd"), Cases = On(caseDays, day), Documents = On(documentDays, day), Decisions = On(decisionDays, day)
            });

            int RunsWhere(Func<AgentRunSource, AgentRunStatus, bool> test) => runs.Where(r => test(r.Key.Source, r.Key.Status)).Sum(r => r.Count);

            return new
            {
                Totals = new
                {
                    Cases = casesByStatus.Sum(x => x.Count),
                    Courses = coursesByStatus.Sum(x => x.Count),
                    Institutions = await db.Institutions.CountAsync(ct),
                    CatalogueCourses = await db.BUCourses.CountAsync(ct),
                    ApprovedEquivalencies = await db.EquivalencyRules.CountAsync(r => r.ApprovalStatus == ApprovalStatus.Approved, ct),
                    Documents = documents.Sum(x => x.Count),
                    FinalDecisions = outcomes.Sum(x => x.Count),
                    AwardedUnits = await db.Evaluations.Where(e => e.Status == EvaluationStatus.Final).SumAsync(e => (decimal?)e.AwardedUnits, ct) ?? 0,
                },
                CasesByStatus = casesByStatus.ToDictionary(x => x.Key.ToString(), x => x.Count),
                CoursesByStatus = coursesByStatus.ToDictionary(x => x.Key.ToString(), x => x.Count),
                MatchLevels = matchLevels.ToDictionary(x => x.Key?.ToString() ?? "NotMatched", x => x.Count),
                DecisionOutcomes = outcomes.ToDictionary(x => x.Key.ToString(), x => x.Count),
                DocumentsByType = documents.ToDictionary(x => x.Key.ToString(), x => x.Count),
                Resolution = new
                {
                    ApprovedRule = RunsWhere((s, _) => s == AgentRunSource.ApprovedRule),
                    Cache = RunsWhere((s, _) => s == AgentRunSource.Cache),
                    NewRun = RunsWhere((s, st) => s == AgentRunSource.NewRun && st != AgentRunStatus.Skipped),
                    Failed = RunsWhere((_, st) => st == AgentRunStatus.Failed),
                },
                CasesByProgram = programs,
                CoursesByInstitution = institutions,
                Activity = activity,
            };
        }).WithTags("Reporting");

        // Drill-down table behind each overview tile. Course- and catalogue-level rows only: nothing identifies a student.
        app.MapGet("/api/overview/details/{topic}", async (string topic, TransferCreditDbContext db, CancellationToken ct) =>
        {
            var final = db.Evaluations.AsNoTracking().Where(e => e.Status == EvaluationStatus.Final);
            switch (topic)
            {
                case "cases":
                {
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var cases = await db.Cases.AsNoTracking().Select(c => new { c.Status, c.Program, c.DeadlineDate }).ToListAsync(ct);
                    bool Open(CaseStatus s) => s is not (CaseStatus.Decided or CaseStatus.Posted);
                    return Results.Ok(cases.GroupBy(c => new { c.Status, c.Program }).OrderBy(g => g.Key.Status).ThenBy(g => g.Key.Program)
                        .Select(g => new
                        {
                            g.Key.Status, g.Key.Program, Cases = g.Count(),
                            Overdue = g.Count(c => Open(c.Status) && c.DeadlineDate < today),
                            DueIn30Days = g.Count(c => Open(c.Status) && c.DeadlineDate >= today && c.DeadlineDate <= today.AddDays(30)),
                        }));
                }
                case "courses":
                    return Results.Ok((await db.SourceCourses.AsNoTracking()
                        .GroupBy(c => new { Institution = c.Institution!.Name, c.Status })
                        .Select(g => new { g.Key.Institution, g.Key.Status, Courses = g.Count(), Credits = g.Sum(c => c.ConvertedCredits) })
                        .ToListAsync(ct)).OrderBy(r => r.Institution).ThenBy(r => r.Status));
                case "decisions":
                    return Results.Ok(await final.OrderByDescending(e => e.DecisionAt).Take(200).Select(e => new
                    {
                        e.DecisionAt, Institution = e.SourceCourse!.Institution!.Name,
                        SourceCourse = e.SourceCourse.Code + " " + e.SourceCourse.Title,
                        BuCourse = e.BUCourse != null ? e.BUCourse.Code + " " + e.BUCourse.Title : null,
                        e.OutcomeType, e.AwardedUnits
                    }).ToListAsync(ct));
                case "units":
                {
                    var rows = await final.Select(e => new { BuCourse = e.BUCourse != null ? e.BUCourse.Code + " " + e.BUCourse.Title : null, e.OutcomeType, e.AwardedUnits })
                        .ToListAsync(ct);
                    return Results.Ok(rows.GroupBy(r => new { r.BuCourse, r.OutcomeType })
                        .Select(g => new { g.Key.BuCourse, g.Key.OutcomeType, Decisions = g.Count(), AwardedUnits = g.Sum(r => r.AwardedUnits) })
                        .OrderByDescending(r => r.AwardedUnits));
                }
                case "equivalencies":
                    return Results.Ok(await db.EquivalencyRules.AsNoTracking().Where(r => r.ApprovalStatus == ApprovalStatus.Approved)
                        .OrderBy(r => r.Institution!.Name).ThenBy(r => r.SourceCode).Take(500).Select(r => new
                        {
                            Institution = r.Institution!.Name, SourceCourse = r.SourceCode + " " + r.SourceTitle,
                            BuCourse = r.BUCourse != null ? r.BUCourse.Code + " " + r.BUCourse.Title : null,
                            r.OutcomeType, r.Program, r.VerifiedStatus, r.EffectiveFrom
                        }).ToListAsync(ct));
                case "catalogue":
                {
                    var mustTakeAtBu = (await db.ProgramRules.Select(p => p.BUCourseId).ToListAsync(ct)).ToHashSet();
                    var courses = await db.BUCourses.AsNoTracking().OrderBy(c => c.Program).ThenBy(c => c.Code)
                        .Select(c => new { c.Id, c.Code, c.Title, c.Program, c.RequirementType, c.Credits, c.VerifiedStatus }).ToListAsync(ct);
                    return Results.Ok(courses.Select(c => new
                    {
                        c.Code, c.Title, c.Program, c.RequirementType, c.Credits, c.VerifiedStatus, MustBeTakenAtBu = mustTakeAtBu.Contains(c.Id)
                    }));
                }
                case "institutions":
                    return Results.Ok(await db.Institutions.AsNoTracking().OrderBy(i => i.Name).Select(i => new
                    {
                        i.Name, i.CreditSystem,
                        Courses = db.SourceCourses.Count(c => c.InstitutionId == i.Id),
                        Equivalencies = db.EquivalencyRules.Count(r => r.InstitutionId == i.Id && r.ApprovalStatus == ApprovalStatus.Approved),
                        i.CatalogueSource
                    }).ToListAsync(ct));
                case "documents":
                    return Results.Ok((await db.Documents.AsNoTracking()
                        .GroupBy(d => new { d.DocumentType, d.Source, d.ScanStatus })
                        .Select(g => new { g.Key.DocumentType, g.Key.Source, g.Key.ScanStatus, Documents = g.Count(), SizeBytes = g.Sum(d => d.SizeBytes) })
                        .ToListAsync(ct)).OrderBy(r => r.DocumentType).ThenBy(r => r.Source));
                default:
                    return Results.NotFound(new { title = $"Unknown overview topic '{topic}'." });
            }
        }).WithTags("Reporting");

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
