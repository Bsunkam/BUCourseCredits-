using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using TransferCredit.Api.Documents;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Intake;

/// <summary>
/// Polls the intake mailbox with a Graph delta query (Mail.Read, scoped by Exchange RBAC for Applications).
/// Read-only: messages are never moved or marked. The delta link is stored so restarts do not reprocess mail.
/// </summary>
public class GraphMailPoller(IServiceScopeFactory scopes, IOptions<IntakeOptions> options, ILogger<GraphMailPoller> log)
    : BackgroundService
{
    private static readonly string[] Fields = ["id", "internetMessageId", "subject", "from", "receivedDateTime", "hasAttachments"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled || string.IsNullOrWhiteSpace(o.Mailbox))
        {
            log.LogInformation("Email intake disabled (INTAKE_ENABLED=false). Use the .eml simulator instead.");
            return;
        }

        var graph = new GraphServiceClient(new DefaultAzureCredential(), ["https://graph.microsoft.com/.default"]);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(30, o.PollSeconds)));
        do
        {
            try
            {
                await PollOnceAsync(graph, o.Mailbox!, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Mailbox poll failed");
                await RecordStatusAsync(o.Mailbox!, $"Error: {ex.Message}", stoppingToken);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(GraphServiceClient graph, string mailbox, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransferCreditDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<EmailIntakeProcessor>();

        var state = await db.MailSyncStates.FirstOrDefaultAsync(s => s.MailboxAddress == mailbox && s.FolderId == "inbox", ct);
        if (state is null)
        {
            state = new MailSyncState { MailboxAddress = mailbox, FolderId = "inbox" };
            db.MailSyncStates.Add(state);
        }

        var delta = graph.Users[mailbox].MailFolders["inbox"].Messages.Delta;
        var page = state.DeltaLink is null
            ? await delta.GetAsDeltaGetResponseAsync(r => r.QueryParameters.Select = Fields, ct)
            : await delta.WithUrl(state.DeltaLink).GetAsDeltaGetResponseAsync(cancellationToken: ct);

        var processed = 0;
        while (page is not null)
        {
            foreach (var message in page.Value ?? [])
            {
                if (message.AdditionalData?.ContainsKey("@removed") == true || message.Id is null) continue;
                var email = await ToInboundAsync(graph, mailbox, message, ct);
                await processor.ProcessAsync(email, "intake-worker", ct);
                processed++;
            }

            if (page.OdataNextLink is { } next)
            {
                page = await delta.WithUrl(next).GetAsDeltaGetResponseAsync(cancellationToken: ct);
            }
            else
            {
                state.DeltaLink = page.OdataDeltaLink ?? state.DeltaLink;
                page = null;
            }
        }

        state.LastRunAt = DateTimeOffset.UtcNow;
        state.LastStatus = $"OK: {processed} message(s)";
        await db.SaveChangesAsync(ct);
    }

    private static async Task<InboundEmail> ToInboundAsync(GraphServiceClient graph, string mailbox, Message message, CancellationToken ct)
    {
        var attachments = new List<InboundAttachment>();
        var skipped = 0;
        if (message.HasAttachments == true)
        {
            var response = await graph.Users[mailbox].Messages[message.Id].Attachments.GetAsync(cancellationToken: ct);
            foreach (var attachment in response?.Value ?? [])
            {
                switch (attachment)
                {
                    case FileAttachment { IsInline: true }:
                        break; // inline images (signatures, logos) are ignored
                    case FileAttachment file when file.ContentBytes is not null:
                        attachments.Add(new InboundAttachment(file.Name ?? "attachment", file.ContentBytes));
                        break;
                    default:
                        skipped++; // item (forwarded) and reference (cloud link) attachments go to triage
                        break;
                }
            }
        }

        return new InboundEmail(
            IntakeChannel.Email, message.Id, message.InternetMessageId ?? message.Id!,
            message.From?.EmailAddress?.Address ?? "unknown@unknown", message.Subject ?? "",
            message.ReceivedDateTime ?? DateTimeOffset.UtcNow, attachments, skipped);
    }

    private async Task RecordStatusAsync(string mailbox, string status, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransferCreditDbContext>();
        var state = await db.MailSyncStates.FirstOrDefaultAsync(s => s.MailboxAddress == mailbox, ct);
        if (state is null) return;
        state.LastRunAt = DateTimeOffset.UtcNow;
        state.LastStatus = status.Length > 500 ? status[..500] : status;
        await db.SaveChangesAsync(ct);
    }
}
