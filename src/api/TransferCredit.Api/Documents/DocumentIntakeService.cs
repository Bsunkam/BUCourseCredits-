using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Agent;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Documents;

public record IngestRequest(
    Stream Content, string FileName, int? CaseId, int? SourceCourseId, DocumentType DocumentType,
    DocumentSource Source, int? IntakeMessageId, string ActorId);

public record IngestResult(Document Document, bool Duplicate);

/// <summary>
/// The one intake pipeline for portal upload, staff upload and email (design 8.2):
/// validate, de-duplicate, store in quarantine, then the scan worker promotes clean files.
/// </summary>
public class DocumentIntakeService(
    TransferCreditDbContext db,
    IDocumentStorage storage,
    FileValidator validator,
    AuditService audit,
    TimeProvider clock)
{
    public async Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await CopyWithLimitAsync(request.Content, buffer, validator.MaxBytes, ct);
        var bytes = buffer.ToArray();

        var fileName = Path.GetFileName(request.FileName);
        var check = validator.Check(fileName, bytes);
        if (!check.Ok) throw new ValidationFailedException($"{fileName}: {check.Error}");

        if (request.SourceCourseId is { } courseId &&
            !await db.SourceCourses.AnyAsync(c => c.Id == courseId && c.CaseId == request.CaseId, ct))
            throw new ValidationFailedException("The course does not belong to this case.");

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // Same file already on this case: link nothing new, store nothing new.
        if (request.CaseId is not null)
        {
            var onCase = await db.Documents.FirstOrDefaultAsync(d => d.Sha256 == sha && d.CaseId == request.CaseId, ct);
            if (onCase is not null)
            {
                audit.Record(request.ActorId, request.CaseId, AuditActions.DocumentDuplicate,
                    $"{fileName} is identical to document #{onCase.Id}; not stored again.");
                await db.SaveChangesAsync(ct);
                return new IngestResult(onCase, true);
            }
        }

        var document = new Document
        {
            CaseId = request.CaseId,
            SourceCourseId = request.SourceCourseId,
            DocumentType = request.DocumentType,
            Source = request.Source,
            IntakeMessageId = request.IntakeMessageId,
            FileName = fileName,
            ContentType = check.ContentType,
            SizeBytes = bytes.Length,
            Sha256 = sha,
            UploadedBy = request.ActorId,
            UploadedAt = clock.GetUtcNow()
        };

        // Same file seen elsewhere and already scanned: reuse the stored copy, scan result and extraction.
        var known = await db.Documents.AsNoTracking()
            .Where(d => d.Sha256 == sha && d.ScanStatus != ScanStatus.Pending)
            .OrderByDescending(d => d.Id).FirstOrDefaultAsync(ct);
        if (known is not null)
        {
            document.Container = known.Container;
            document.StorageKey = known.StorageKey;
            document.ScanStatus = known.ScanStatus;
            document.ExtractionStatus = known.ExtractionStatus;
        }
        else
        {
            document.Container = Containers.IntakeRaw;
            document.StorageKey = $"{Guid.NewGuid():N}/{sha}.{check.Extension}";
            document.ScanStatus = ScanStatus.Pending;
            document.ExtractionStatus = ExtractionStatus.NotStarted;
            buffer.Position = 0;
            await storage.WriteAsync(document.Container, document.StorageKey, buffer, check.ContentType, ct);
        }

        db.Documents.Add(document);
        if (request.CaseId is not null)
        {
            audit.Record(request.ActorId, request.CaseId, AuditActions.DocumentReceived,
                $"{fileName} ({request.DocumentType}) via {request.Source}; sha256 {sha[..12]}...; scan {document.ScanStatus}.",
                studentMessage: $"Document received via {SourceLabel(request.Source)}: {fileName}.");
        }
        await db.SaveChangesAsync(ct);
        return new IngestResult(document, known is not null);
    }

    private static string SourceLabel(DocumentSource source) => source switch
    {
        DocumentSource.Portal => "portal upload",
        DocumentSource.Email => "email",
        _ => "Credit Evaluation staff"
    };

    private static async Task CopyWithLimitAsync(Stream from, Stream to, long max, CancellationToken ct)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await from.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > max) throw new ValidationFailedException($"The file is larger than {max / 1024 / 1024} MB.");
            await to.WriteAsync(chunk.AsMemory(0, read), ct);
        }
    }
}

/// <summary>Extracts text once per file (cached by checksum) and fills a course's syllabus evidence.</summary>
public class ExtractionService(
    TransferCreditDbContext db,
    IDocumentStorage storage,
    IAgentClient agent,
    AuditService audit,
    ILogger<ExtractionService> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task ExtractAsync(Document document, CancellationToken ct)
    {
        if (document.ScanStatus != ScanStatus.Clean) return; // never extract unscanned or quarantined files

        var cacheKey = $"{document.Sha256}.json";
        AgentExtractResponse? result = null;
        try
        {
            if (await storage.ExistsAsync(Containers.Extracted, cacheKey, ct))
            {
                await using var cached = await storage.OpenReadAsync(Containers.Extracted, cacheKey, ct);
                result = await JsonSerializer.DeserializeAsync<AgentExtractResponse>(cached, Json, ct);
            }
            else
            {
                result = await agent.ExtractAsync(new AgentExtractRequest(
                    document.Id.ToString(), document.Container, document.StorageKey, document.DocumentType.ToString()), ct);
                if (result is not null)
                {
                    using var ms = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(result, Json));
                    await storage.WriteAsync(Containers.Extracted, cacheKey, ms, "application/json", ct);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning(ex, "Extraction failed for document {DocumentId}", document.Id);
            document.ExtractionStatus = ExtractionStatus.Failed;
            return;
        }

        if (result is null)
        {
            // Stub mode has no OCR: staff paste syllabus text on the course instead.
            document.ExtractionStatus = ExtractionStatus.NotAvailable;
            return;
        }

        document.ExtractionStatus = ExtractionStatus.Extracted;
        audit.Record("system", document.CaseId, AuditActions.DocumentExtracted,
            $"{document.FileName}: text extracted ({result.Model}). Missing fields: {string.Join(", ", result.MissingFields)}.");

        if (document.DocumentType == DocumentType.Syllabus && document.SourceCourseId is { } courseId)
        {
            var course = await db.SourceCourses.FirstAsync(c => c.Id == courseId, ct);
            if (string.IsNullOrWhiteSpace(course.SyllabusText) && course.Status != CourseStatus.Decided)
            {
                course.SyllabusText = result.Text;
                course.SyllabusDocumentId = document.Id;
                course.Status = CourseStatus.Pending; // re-match with the new evidence
            }
        }
    }
}
