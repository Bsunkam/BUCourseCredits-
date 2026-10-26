using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Auth;
using TransferCredit.Api.Contracts;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Endpoints;

public static class CaseEndpoints
{
    public static void MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        var cases = app.MapGroup("/api/cases").WithTags("Cases");

        cases.MapGet("", async (CaseStatus? status, string? q, TransferCreditDbContext db, CaseService svc, CancellationToken ct) =>
        {
            var query = db.Cases.AsNoTracking().Include(c => c.Courses).AsQueryable();
            if (status is not null) query = query.Where(c => c.Status == status);
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(c => c.CaseRef.Contains(q) || c.StudentName.Contains(q) || c.DemoStudentRef.Contains(q));
            var list = await query.OrderBy(c => c.DeadlineDate).ToListAsync(ct);
            return list.Select(c => c.ToSummary(svc.Today));
        }).RequireAuthorization(Policies.Internal);

        cases.MapPost("", async (CreateCaseRequest request, CaseService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var c = await svc.CreateAsync(request, user.Id(), ct);
            return Results.Created($"/api/cases/{c.Id}", c.ToSummary(svc.Today));
        }).RequireAuthorization(Policies.Staff);

        cases.MapGet("/{id:int}", async (int id, CaseService svc, CancellationToken ct) =>
            await svc.GetDetailAsync(id, ct) is { } detail ? Results.Ok(detail) : Results.NotFound()
        ).RequireAuthorization(Policies.Reviewer);

        cases.MapGet("/{id:int}/status", async (int id, TransferCreditDbContext db, CaseService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (!user.CanView(c)) return Results.Forbid();
            return Results.Ok(await svc.GetStudentStatusAsync(id, ct));
        }).RequireAuthorization();

        cases.MapPost("/{id:int}/courses/import", async (int id, ImportCoursesRequest request, CaseService svc,
            TransferCreditDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var added = await svc.ImportCoursesAsync(id, request, user.Id(), ct);
            var program = await db.Cases.Where(c => c.Id == id).Select(c => c.Program).FirstAsync(ct);
            var buOnly = await svc.BuOnlyRulesAsync(program, ct);
            return Results.Ok(added.Select(c => c.ToDto(buOnly)));
        }).RequireAuthorization(Policies.Staff);

        cases.MapPost("/{id:int}/match", async (int id, TransferCreditDbContext db, MatchQueue queue, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var courses = await db.SourceCourses
                .Where(c => c.CaseId == id && c.Status != CourseStatus.Decided && c.Status != CourseStatus.Matching)
                .ToListAsync(ct);
            foreach (var c in courses) c.Status = CourseStatus.Matching;
            await db.SaveChangesAsync(ct);
            foreach (var c in courses) await queue.EnqueueAsync(new MatchJob(c.Id, user.Id()), ct);
            return Results.Accepted($"/api/cases/{id}", new { queued = courses.Count });
        }).RequireAuthorization(Policies.Reviewer);

        cases.MapPost("/{id:int}/post", async (int id, CaseService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await svc.PostSimulatedAsync(id, user.Id(), ct);
            return Results.Ok(new { posted = true, simulated = true });
        }).RequireAuthorization(Policies.Staff);

        // Students list their own cases; internal users get every open case.
        app.MapGet("/api/me/cases", async (TransferCreditDbContext db, CaseService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var query = db.Cases.AsNoTracking().Include(c => c.Courses).AsQueryable();
            if (!user.IsInternal())
            {
                var studentRef = user.StudentRef();
                query = query.Where(c => studentRef != null && c.DemoStudentRef == studentRef);
            }
            var list = await query.OrderByDescending(c => c.Id).ToListAsync(ct);
            return list.Select(c => c.ToSummary(svc.Today));
        }).RequireAuthorization().WithTags("Cases");
    }
}
