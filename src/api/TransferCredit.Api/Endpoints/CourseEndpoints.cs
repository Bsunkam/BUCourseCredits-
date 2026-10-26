using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Auth;
using TransferCredit.Api.Contracts;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Endpoints;

public static class CourseEndpoints
{
    public static void MapCourseEndpoints(this IEndpointRouteBuilder app)
    {
        var courses = app.MapGroup("/api/courses").WithTags("Courses & matching").RequireAuthorization(Policies.Reviewer);

        courses.MapGet("/{id:int}", async (int id, TransferCreditDbContext db, CaseService cases,
            MatchingService matching, CancellationToken ct) =>
        {
            var course = await LoadAsync(db, id, ct);
            if (course is null) return Results.NotFound();

            var buOnly = await cases.BuOnlyRulesAsync(course.Case!.Program, ct);
            var runs = await db.AgentRuns.AsNoTracking().Where(r => r.SourceCourseId == id)
                .OrderByDescending(r => r.Id).Take(20).ToListAsync(ct);
            var key = await matching.CacheKeyForAsync(course, course.Case.Program, ct);

            return Results.Ok(new CourseDetailDto(
                course.ToDto(buOnly), course.CaseId, course.Case.CaseRef, course.Case.Program, course.SyllabusText,
                Mapping.ParseJson(course.PolicyFlagsJson),
                course.Candidates.OrderBy(c => c.Id).Select(c => c.ToDto(buOnly)).ToList(),
                course.Evaluations.OrderByDescending(e => e.Id).Select(e => e.ToDto()).ToList(),
                runs.Select(r => r.ToDto()).ToList(),
                key));
        });

        courses.MapGet("/{id:int}/candidates", async (int id, TransferCreditDbContext db, CaseService cases, CancellationToken ct) =>
        {
            var course = await LoadAsync(db, id, ct);
            if (course is null) return Results.NotFound();
            var buOnly = await cases.BuOnlyRulesAsync(course.Case!.Program, ct);
            return Results.Ok(course.Candidates.OrderBy(c => c.Id).Select(c => c.ToDto(buOnly)));
        });

        courses.MapPut("/{id:int}/syllabus", async (int id, UpdateSyllabusRequest request, CaseService cases,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            await cases.UpdateSyllabusAsync(id, request.Text, user.Id(), ct);
            return Results.NoContent();
        });

        courses.MapPost("/{id:int}/match", async (int id, TransferCreditDbContext db, MatchQueue queue,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var course = await db.SourceCourses.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (course is null) return Results.NotFound();
            if (course.Status == CourseStatus.Decided)
                return Results.Problem("This course already has a final decision.", statusCode: 409);

            course.Status = CourseStatus.Matching;
            await db.SaveChangesAsync(ct);
            await queue.EnqueueAsync(new MatchJob(id, user.Id()), ct);
            return Results.Accepted($"/api/courses/{id}", new { status = course.Status });
        });

        var evaluations = app.MapGroup("/api/evaluations").WithTags("Evaluations").RequireAuthorization(Policies.Reviewer);

        evaluations.MapPost("", async (CreateEvaluationRequest request, EvaluationService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok((await svc.SaveDraftAsync(request, user, ct)).ToDto()));

        evaluations.MapPost("/{id:int}/finalize", async (int id, EvaluationService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok((await svc.FinaliseAsync(id, user, ct)).ToDto()));
    }

    private static Task<SourceCourse?> LoadAsync(TransferCreditDbContext db, int id, CancellationToken ct) =>
        db.SourceCourses.AsNoTracking().AsSplitQuery()
            .Include(c => c.Case)
            .Include(c => c.Institution)
            .Include(c => c.Candidates).ThenInclude(c => c.BUCourse)
            .Include(c => c.Evaluations).ThenInclude(e => e.BUCourse)
            .Include(c => c.AppliedEquivalencyRule).ThenInclude(r => r!.BUCourse)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
}
