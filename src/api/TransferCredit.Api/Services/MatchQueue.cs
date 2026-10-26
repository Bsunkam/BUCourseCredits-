using System.Threading.Channels;

namespace TransferCredit.Api.Services;

public record MatchJob(int CourseId, string ActorId);

/// <summary>In-process queue: the API answers 202 Accepted and the UI polls course status.</summary>
public class MatchQueue
{
    private readonly Channel<MatchJob> _channel = Channel.CreateUnbounded<MatchJob>();

    public ValueTask EnqueueAsync(MatchJob job, CancellationToken ct) => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<MatchJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public class MatchWorker(MatchQueue queue, IServiceScopeFactory scopes, ILogger<MatchWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var matching = scope.ServiceProvider.GetRequiredService<MatchingService>();
                await matching.MatchAsync(job.CourseId, job.ActorId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Match job failed for course {CourseId}", job.CourseId);
                await ResetAsync(job.CourseId, stoppingToken);
            }
        }
    }

    private async Task ResetAsync(int courseId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransferCredit.Data.TransferCreditDbContext>();
        var course = await db.SourceCourses.FindAsync([courseId], ct);
        if (course is { Status: TransferCredit.Data.Entities.CourseStatus.Matching })
        {
            course.Status = TransferCredit.Data.Entities.CourseStatus.NeedsReview;
            await db.SaveChangesAsync(ct);
        }
    }
}
