using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TransferCredit.Api.Agent;
using TransferCredit.Api.Contracts;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Tests;

/// <summary>Stands in for the Python agent service: the stub's ranking, reported under a chosen model name.</summary>
internal sealed class FakeServiceAgent(string model) : IAgentClient
{
    private readonly StubAgentClient stub = new();
    private int calls;

    public int Calls => calls;
    public string Mode => "http";

    public async Task<AgentEvaluateResponse> EvaluateAsync(AgentEvaluateRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        return (await stub.EvaluateAsync(request, ct)) with { Model = model };
    }

    public Task<AgentExtractResponse?> ExtractAsync(AgentExtractRequest request, CancellationToken ct) => stub.ExtractAsync(request, ct);
    public Task ResumeAsync(AgentResumeRequest request, CancellationToken ct) => stub.ResumeAsync(request, ct);
    public Task<bool> IsHealthyAsync(CancellationToken ct) => stub.IsHealthyAsync(ct);
}

internal sealed class AgentServiceFactory(IAgentClient agent) : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentClient>();
            services.AddSingleton(agent);
        });
    }
}

public sealed class AgentReuseTests
{
    private static async Task<int> MatchDemoCaseAsync(HttpClient staff, string studentRef)
    {
        var created = await staff.PostAsJsonAsync("/api/demo/cases", new { studentRef });
        created.EnsureSuccessStatusCode();
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await staff.PostAsync($"/api/cases/{caseId}/match", null)).EnsureSuccessStatusCode();
        for (var i = 0; i < 100; i++)
        {
            var detail = (await staff.GetFromJsonAsync<CaseDetailDto>($"/api/cases/{caseId}", WorkflowTests.Json))!;
            if (detail.Courses.All(c => c.Status != CourseStatus.Matching)) return caseId;
            await Task.Delay(100);
        }
        throw new TimeoutException("Matching did not finish.");
    }

    [Fact]
    public async Task Ai_suggestions_are_reused_for_identical_courses()
    {
        var agent = new FakeServiceAgent("gpt-4o");
        using var factory = new AgentServiceFactory(agent);
        var staff = await factory.AsAsync("staff-1");

        await MatchDemoCaseAsync(staff, "DS-0001");
        var firstCalls = agent.Calls;
        await MatchDemoCaseAsync(staff, "DS-0002");

        Assert.True(firstCalls > 0);
        Assert.Equal(firstCalls, agent.Calls);
    }

    [Fact]
    public async Task Mock_rankings_are_never_reused_as_ai_suggestions()
    {
        var agent = new FakeServiceAgent("mock-keyword-overlap (not AI)");
        using var factory = new AgentServiceFactory(agent);
        var staff = await factory.AsAsync("staff-1");

        await MatchDemoCaseAsync(staff, "DS-0001");
        var firstCalls = agent.Calls;
        await MatchDemoCaseAsync(staff, "DS-0002");

        Assert.True(firstCalls > 0);
        Assert.Equal(2 * firstCalls, agent.Calls);
    }
}
