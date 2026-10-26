using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Auth;
using TransferCredit.Api.Contracts;
using TransferCredit.Api.Documents;
using TransferCredit.Api.Intake;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        // Portal and staff upload: multipart/form-data with file, documentType and optional sourceCourseId.
        app.MapPost("/api/cases/{id:int}/documents", async (int id, HttpRequest http, TransferCreditDbContext db,
            DocumentIntakeService intake, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (!user.CanUploadTo(c)) return Results.Forbid();
            if (!http.HasFormContentType) return Results.Problem("Expected multipart/form-data.", statusCode: 400);

            var form = await http.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.Problem("No file supplied.", statusCode: 400);

            var type = Enum.TryParse<DocumentType>(form["documentType"], true, out var t) ? t : DocumentType.Other;
            int? courseId = int.TryParse(form["sourceCourseId"], out var cid) ? cid : null;
            var source = user.IsReviewer() ? DocumentSource.StaffUpload : DocumentSource.Portal;

            await using var stream = file.OpenReadStream();
            var result = await intake.IngestAsync(new IngestRequest(stream, file.FileName, id, courseId, type, source, null, user.Id()), ct);
            return Results.Ok(new { document = result.Document.ToDto(), duplicate = result.Duplicate });
        }).RequireAuthorization().WithTags("Documents");

        var docs = app.MapGroup("/api/documents").WithTags("Documents").RequireAuthorization();

        docs.MapGet("/{id:int}/metadata", async (int id, TransferCreditDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            await FindViewableAsync(db, id, user, ct) is { } doc ? Results.Ok(doc.ToDto()) : Results.NotFound());

        docs.MapGet("/{id:int}/link", async (int id, TransferCreditDbContext db, IDocumentStorage storage,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var doc = await FindViewableAsync(db, id, user, ct);
            if (doc is null) return Results.NotFound();
            if (doc.ScanStatus != ScanStatus.Clean) return Results.Problem("Only clean documents can be opened.", statusCode: 409);

            var link = await storage.GetReadLinkAsync(doc.Container, doc.StorageKey, TimeSpan.FromMinutes(10), ct)
                       ?? new DocumentLinkDto($"/api/documents/{id}/content", "api", DateTimeOffset.UtcNow.AddMinutes(10));
            return Results.Ok(link);
        });

        docs.MapGet("/{id:int}/content", async (int id, TransferCreditDbContext db, IDocumentStorage storage,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var doc = await FindViewableAsync(db, id, user, ct);
            if (doc is null) return Results.NotFound();
            if (doc.ScanStatus != ScanStatus.Clean) return Results.Problem("Only clean documents can be opened.", statusCode: 409);
            var stream = await storage.OpenReadAsync(doc.Container, doc.StorageKey, ct);
            return Results.File(stream, doc.ContentType, doc.FileName);
        });

        var intake = app.MapGroup("/api/intake").WithTags("Email intake").RequireAuthorization(Policies.Staff);

        intake.MapGet("", async (IntakeMatchStatus? status, TransferCreditDbContext db, CancellationToken ct) =>
        {
            var query = db.IntakeMessages.AsNoTracking().Include(m => m.Documents).AsQueryable();
            query = status is null
                ? query.Where(m => m.MatchStatus != IntakeMatchStatus.Matched)
                : query.Where(m => m.MatchStatus == status);
            var list = await query.OrderByDescending(m => m.Id).Take(200).ToListAsync(ct);
            return list.Select(m => new IntakeMessageDto(m.Id, m.Channel, m.SenderAddress, m.Subject, m.ReceivedAt,
                m.CaseId, m.MatchStatus, m.AssignedBy, m.Documents.Select(d => d.ToDto()).ToList()));
        });

        intake.MapPost("/{id:int}/assign", async (int id, AssignIntakeRequest request, EmailIntakeProcessor processor,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            await processor.AssignAsync(id, request.CaseId, user.Id(), ct);
            return Results.NoContent();
        });

        // Email simulator: same pipeline as the live mailbox, fed from an uploaded .eml file.
        intake.MapPost("/import-eml", async (HttpRequest http, EmailIntakeProcessor processor, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!http.HasFormContentType) return Results.Problem("Expected multipart/form-data.", statusCode: 400);
            var form = await http.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || !file.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
                return Results.Problem("Upload an .eml file.", statusCode: 400);

            await using var stream = file.OpenReadStream();
            var email = await EmlParser.ParseAsync(stream, ct);
            return Results.Ok(await processor.ProcessAsync(email, user.Id(), ct));
        });
    }

    private static async Task<Document?> FindViewableAsync(TransferCreditDbContext db, int id, ClaimsPrincipal user, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().Include(d => d.Case).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return null;
        if (doc.Case is null) return user.IsReviewer() ? doc : null; // triage items: staff only
        return user.CanView(doc.Case) ? doc : null;
    }
}
