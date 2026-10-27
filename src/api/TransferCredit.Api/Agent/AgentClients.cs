using System.Net.Http.Json;
using System.Text.RegularExpressions;
using TransferCredit.Api.Rules;

namespace TransferCredit.Api.Agent;

public interface IAgentClient
{
    /// <summary>"http" for the Python service, "stub" for the local keyword stub.</summary>
    string Mode { get; }
    Task<AgentEvaluateResponse> EvaluateAsync(AgentEvaluateRequest request, CancellationToken ct);
    Task<AgentExtractResponse?> ExtractAsync(AgentExtractRequest request, CancellationToken ct);
    Task ResumeAsync(AgentResumeRequest request, CancellationToken ct);
    Task<bool> IsHealthyAsync(CancellationToken ct);
}

public class AgentOptions
{
    /// <summary>Base URL of the Python agent service (internal ingress). Empty means use the stub.</summary>
    public string? BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public string PromptVersion { get; set; } = "p-001";
}

/// <summary>Typed client for the private Python agent service. Only the API calls it.</summary>
public class HttpAgentClient(HttpClient http) : IAgentClient
{
    public string Mode => "http";

    public async Task<AgentEvaluateResponse> EvaluateAsync(AgentEvaluateRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("agent/evaluate", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentEvaluateResponse>(ct)
            ?? throw new AgentContractException("Empty response from agent service.");
    }

    public async Task<AgentExtractResponse?> ExtractAsync(AgentExtractRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("agent/extract", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentExtractResponse>(ct);
    }

    public async Task ResumeAsync(AgentResumeRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("agent/resume", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync("health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// Local stand-in for the agent service (design Phase 2 "stub the agent service").
/// Ranks candidates by keyword overlap. It is NOT AI and labels its output as such.
/// </summary>
public partial class StubAgentClient : IAgentClient
{
    public const string ModelName = "stub-keyword-overlap (not AI)";

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "with", "into", "from", "this", "that", "are", "its", "including", "introduction",
        "intro", "course", "students", "student", "topics", "use", "using", "basic", "weekly", "two", "will",
        "fundamentals", "principles", "foundations", "class", "based", "outcomes", "write", "design", "how",
        // Generic learning-outcome verbs: every course "explains" and "applies", so they say nothing about content.
        "learning", "explain", "apply", "analyse", "analyze", "evaluate", "describe", "understand", "able"
    ];

    public string Mode => "stub";

    public Task<AgentEvaluateResponse> EvaluateAsync(AgentEvaluateRequest request, CancellationToken ct)
    {
        var source = request.SourceCourse;
        var sourceText = $"{source.Title} {source.Description} {source.LearningOutcomes} {request.SyllabusText}";
        var sourceTokens = Tokens(sourceText);
        var buOnly = request.PolicyFlags.BuOnlyCourses.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ranked = request.Candidates
            .Select(c =>
            {
                var buTokens = Tokens($"{c.Title} {c.Description} {c.LearningOutcomes}");
                var overlap = sourceTokens.Intersect(buTokens).ToList();
                var score = buTokens.Count == 0 || sourceTokens.Count == 0
                    ? 0m
                    : (decimal)(overlap.Count / Math.Sqrt(buTokens.Count * (double)sourceTokens.Count));
                return (Course: c, Score: Math.Round(score, 3), Overlap: overlap);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .ToList();

        var candidates = ranked.Select(x =>
        {
            var level = buOnly.Contains(x.Course.Code) ? "Must be taken at BU"
                : x.Score >= 0.2m ? "Partial - syllabus review"
                : "No equivalent found";
            var attributes = new List<string> { $"keywords: {string.Join(", ", x.Overlap.Take(8))}" };
            if (source.Credits > 0) attributes.Add($"credits {request.PolicyFlags.ConvertedCredits} vs {x.Course.Credits}");
            return new AgentCandidate(x.Course.Code, level, x.Score, Confidence(x.Score), attributes);
        }).ToList();

        var top = candidates.FirstOrDefault();
        var evidence = top is null || string.IsNullOrWhiteSpace(request.SyllabusText)
            ? []
            : Evidence(request.SyllabusText!, ranked[0].Overlap);

        var explanation = top is null
            ? "No catalogue course shares enough content with this course. An evaluator should review it."
            : $"Stub ranking by keyword overlap between the source course and BU catalogue descriptions. " +
              $"Top candidate {top.BuCourseCode} shares: {string.Join(", ", ranked[0].Overlap.Take(8))}. " +
              "This is not an AI judgement and not an official equivalency.";

        return Task.FromResult(new AgentEvaluateResponse(
            Status: "paused_for_review",
            ThreadId: request.ThreadId,
            MatchLevel: top?.MatchLevel ?? "No equivalent found",
            SuggestedBuCourse: top?.BuCourseCode,
            Confidence: top?.Confidence ?? "low",
            Candidates: candidates,
            Evidence: evidence,
            Explanation: explanation,
            StudentMessage: "Your course is under department review.",
            InsufficientEvidence: evidence.Count == 0,
            NeedsHumanReview: true,
            GraphVersion: "stub-1.0",
            PromptVersion: request.PromptVersion,
            Model: ModelName,
            CacheKey: request.CacheKey));
    }

    public Task<AgentExtractResponse?> ExtractAsync(AgentExtractRequest request, CancellationToken ct) =>
        Task.FromResult<AgentExtractResponse?>(null);

    public Task ResumeAsync(AgentResumeRequest request, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> IsHealthyAsync(CancellationToken ct) => Task.FromResult(true);

    private static string Confidence(decimal score) => score >= 0.35m ? "high" : score >= 0.2m ? "medium" : "low";

    private static HashSet<string> Tokens(string text) =>
        WordRegex().Matches(text.ToLowerInvariant())
            .Select(m => Stem(m.Value))
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .ToHashSet();

    private static string Stem(string word) =>
        word.EndsWith("ies") && word.Length > 4 ? word[..^3] + "y"
        : word.EndsWith('s') && !word.EndsWith("ss") && word.Length > 3 ? word[..^1]
        : word;

    private static List<AgentEvidence> Evidence(string syllabus, List<string> overlap) =>
        SentenceRegex().Split(syllabus)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Select(s => (Sentence: s, Hits: overlap.Where(o => Tokens(s).Contains(o)).ToList()))
            .Where(x => x.Hits.Count > 0)
            .OrderByDescending(x => x.Hits.Count)
            .Take(2)
            .Select(x => new AgentEvidence("syllabus", x.Sentence.Length > 300 ? x.Sentence[..300] : x.Sentence,
                string.Join(", ", x.Hits)))
            .ToList();

    [GeneratedRegex("[a-z]+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"(?<=[.;:])\s+")]
    private static partial Regex SentenceRegex();
}
