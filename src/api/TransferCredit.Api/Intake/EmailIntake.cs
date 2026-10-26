using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using TransferCredit.Api.Documents;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Intake;

public record InboundAttachment(string FileName, byte[] Content);

/// <summary>An email reduced to what intake needs. The body is deliberately absent: it is never stored or sent to AI.</summary>
public record InboundEmail(
    IntakeChannel Channel, string? GraphMessageId, string InternetMessageId, string From, string Subject,
    DateTimeOffset ReceivedAt, List<InboundAttachment> Attachments, int SkippedAttachments);

public record EmailIntakeResult(int IntakeMessageId, IntakeMatchStatus MatchStatus, int? CaseId, bool Duplicate,
    int Accepted, List<string> Rejected);

public partial class CaseMatcher(TransferCreditDbContext db)
{
    /// <summary>
    /// Exactly one case reference in the subject, or exactly one case whose contact matches the sender.
    /// Anything else goes to the triage queue; a doubtful match is never auto-assigned (BR-15).
    /// </summary>
    public async Task<(IntakeMatchStatus Status, int? CaseId)> MatchAsync(string subject, string sender, CancellationToken ct)
    {
        var refs = CaseRefRegex().Matches(subject ?? "").Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        if (refs.Count > 1) return (IntakeMatchStatus.Ambiguous, null);
        if (refs.Count == 1)
        {
            var byRef = await db.Cases.Where(c => c.CaseRef == refs[0]).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
            return byRef is null ? (IntakeMatchStatus.Unmatched, null) : (IntakeMatchStatus.Matched, byRef);
        }

        var email = sender.Trim().ToLowerInvariant();
        var bySender = await db.Cases.Where(c => c.StudentEmail == email && c.Status != CaseStatus.Posted)
            .Select(c => c.Id).Take(2).ToListAsync(ct);
        return bySender.Count switch
        {
            1 => (IntakeMatchStatus.Matched, bySender[0]),
            0 => (IntakeMatchStatus.Unmatched, null),
            _ => (IntakeMatchStatus.Ambiguous, null)
        };
    }

    [GeneratedRegex(@"\bTC-\d{3,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex CaseRefRegex();
}

public class EmailIntakeProcessor(
    TransferCreditDbContext db,
    CaseMatcher matcher,
    DocumentIntakeService intake,
    AuditService audit,
    TimeProvider clock)
{
    public async Task<EmailIntakeResult> ProcessAsync(InboundEmail email, string actorId, CancellationToken ct)
    {
        var existing = await db.IntakeMessages.FirstOrDefaultAsync(m => m.InternetMessageId == email.InternetMessageId, ct);
        if (existing is not null)
            return new(existing.Id, existing.MatchStatus, existing.CaseId, true, 0, []);

        var (status, caseId) = await matcher.MatchAsync(email.Subject, email.From, ct);
        var message = new IntakeMessage
        {
            Channel = email.Channel,
            GraphMessageId = email.GraphMessageId,
            InternetMessageId = email.InternetMessageId,
            SenderAddress = email.From.Trim().ToLowerInvariant(),
            Subject = email.Subject.Length > 500 ? email.Subject[..500] : email.Subject,
            ReceivedAt = email.ReceivedAt,
            CaseId = caseId,
            // Forwarded emails and cloud links cannot be processed safely: staff must look at them.
            MatchStatus = email.SkippedAttachments > 0 && status == IntakeMatchStatus.Matched ? IntakeMatchStatus.Ambiguous : status,
            ProcessedAt = clock.GetUtcNow()
        };
        if (message.MatchStatus != IntakeMatchStatus.Matched) message.CaseId = null;
        db.IntakeMessages.Add(message);
        await db.SaveChangesAsync(ct);

        var accepted = 0;
        var rejected = new List<string>();
        foreach (var attachment in email.Attachments)
        {
            try
            {
                using var stream = new MemoryStream(attachment.Content);
                await intake.IngestAsync(new IngestRequest(stream, attachment.FileName, message.CaseId, null,
                    GuessType(attachment.FileName), DocumentSource.Email, message.Id, actorId), ct);
                accepted++;
            }
            catch (ValidationFailedException ex)
            {
                rejected.Add(ex.Message);
            }
        }

        if (message.MatchStatus != IntakeMatchStatus.Matched)
        {
            audit.Record(actorId, null, AuditActions.IntakeTriaged,
                $"Email #{message.Id} from {message.SenderAddress} ({message.MatchStatus}) sent to triage. " +
                $"{accepted} file(s) accepted, {rejected.Count} rejected, {email.SkippedAttachments} skipped.");
        }
        await db.SaveChangesAsync(ct);
        return new(message.Id, message.MatchStatus, message.CaseId, false, accepted, rejected);
    }

    public static DocumentType GuessType(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        return name.Contains("syllab") ? DocumentType.Syllabus
            : name.Contains("transcript") ? DocumentType.Transcript
            : DocumentType.Other;
    }

    public async Task AssignAsync(int intakeMessageId, int caseId, string actorId, CancellationToken ct)
    {
        var message = await db.IntakeMessages.Include(m => m.Documents).FirstOrDefaultAsync(m => m.Id == intakeMessageId, ct)
            ?? throw new KeyNotFoundException($"Intake message {intakeMessageId} not found.");
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct)
            ?? throw new ValidationFailedException($"Case {caseId} not found.");

        message.CaseId = c.Id;
        message.MatchStatus = IntakeMatchStatus.Matched;
        message.AssignedBy = actorId;
        foreach (var d in message.Documents) d.CaseId = c.Id;

        audit.Record(actorId, c.Id, AuditActions.IntakeAssigned,
            $"Email #{message.Id} from {message.SenderAddress} assigned to {c.CaseRef} by staff; {message.Documents.Count} document(s).",
            studentMessage: message.Documents.Count > 0
                ? $"Documents received via email: {string.Join(", ", message.Documents.Select(d => d.FileName))}."
                : null);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Email simulator: turns an .eml file into an <see cref="InboundEmail"/> (fallback when no mailbox scope).</summary>
public static class EmlParser
{
    public static async Task<InboundEmail> ParseAsync(Stream eml, CancellationToken ct)
    {
        var message = await MimeMessage.LoadAsync(eml, ct);
        var attachments = new List<InboundAttachment>();
        var skipped = 0;

        foreach (var entity in message.Attachments)
        {
            if (entity is MimePart part && part.Content is not null)
            {
                using var ms = new MemoryStream();
                await part.Content.DecodeToAsync(ms, ct);
                attachments.Add(new InboundAttachment(part.FileName ?? "attachment", ms.ToArray()));
            }
            else
            {
                skipped++; // forwarded message (MessagePart) or other non-file attachment
            }
        }

        var from = message.From.Mailboxes.FirstOrDefault()?.Address ?? "unknown@unknown";
        var id = string.IsNullOrWhiteSpace(message.MessageId)
            ? $"eml-{Guid.NewGuid():N}"
            : message.MessageId;
        return new InboundEmail(IntakeChannel.EmlImport, null, id, from, message.Subject ?? "",
            message.Date == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : message.Date, attachments, skipped);
    }
}
