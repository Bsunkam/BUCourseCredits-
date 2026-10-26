using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Documents;

/// <summary>
/// Polls pending documents for a scan result. Clean files are copied from intake-raw to documents;
/// malicious files stay quarantined and are flagged. Nothing is extracted before a clean result.
/// </summary>
public class ScanWorker(IServiceScopeFactory scopes, ILogger<ScanWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await ProcessPendingAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Scan worker iteration failed");
            }
        }
    }

    public static async Task ProcessPendingAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<TransferCreditDbContext>();
        var storage = services.GetRequiredService<IDocumentStorage>();
        var audit = services.GetRequiredService<AuditService>();
        var extraction = services.GetRequiredService<ExtractionService>();

        var pending = await db.Documents.Where(d => d.ScanStatus == ScanStatus.Pending)
            .OrderBy(d => d.Id).Take(20).ToListAsync(ct);

        foreach (var doc in pending)
        {
            var result = await storage.GetScanResultAsync(doc.Container, doc.StorageKey, ct);
            if (result == ScanResult.Pending) continue;

            if (result == ScanResult.Clean)
            {
                var ext = Path.GetExtension(doc.StorageKey);
                var key = $"{doc.CaseId?.ToString() ?? "triage"}/{doc.Id}/{doc.Sha256}{ext}";
                await storage.CopyAsync(doc.Container, doc.StorageKey, Containers.Documents, key, ct);
                doc.Container = Containers.Documents;
                doc.StorageKey = key;
                doc.ScanStatus = ScanStatus.Clean;
                audit.Record("system", doc.CaseId, AuditActions.DocumentClean, $"{doc.FileName}: malware scan clean.");
                await extraction.ExtractAsync(doc, ct);
            }
            else
            {
                doc.ScanStatus = ScanStatus.Quarantined;
                audit.Record("system", doc.CaseId, AuditActions.DocumentQuarantined,
                    $"{doc.FileName}: malware scan flagged the file. It stays in quarantine and is never extracted.",
                    studentMessage: $"{doc.FileName} could not be accepted. Please send a new copy.");
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
