using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferCredit.Api.Auth;
using TransferCredit.Data;

namespace TransferCredit.Api.Tests;

public sealed class UserImportTests : IDisposable
{
    private readonly ApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private const string Header = "currentUsername,newUsername,email,password\n";

    private async Task<UserImportResult> ImportAsync(string csv, bool mustChange = true, bool dryRun = false)
    {
        factory.CreateClient().Dispose(); // starts the host, which migrates and seeds
        var errors = new List<string>();
        var rows = UserImportService.Parse(csv, errors);
        if (errors.Count > 0) return new(false, errors, []);
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserImportService>().ImportAsync(rows, mustChange, dryRun, default);
    }

    private Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username, password });

    [Fact]
    public async Task Import_renames_accounts_sets_email_and_password_and_keeps_the_role()
    {
        var result = await ImportAsync(Header +
            "staff-1,s.rivera,s.rivera@bu.example,Rivera-pass-2026\n" +
            "eval-cs,,e.chen@bu.example,\"Chen,with comma 9\"\n", mustChange: false);

        Assert.True(result.Applied);
        Assert.Empty(result.Errors);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync("staff-1", ApiFactory.SeedPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync("s.rivera", ApiFactory.SeedPassword)).StatusCode);

        var staff = await factory.AsAsync("s.rivera", "Rivera-pass-2026");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/intake")).StatusCode); // still Credit Evaluation staff
        (await LoginAsync("e.chen@bu.example", "Chen,with comma 9")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransferCreditDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == "staff-1"));
        var audit = await db.AuditEvents.Where(a => a.ActorId == UserImportService.Actor).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.Contains(audit, a => a.OldValue == "staff-1" && a.NewValue == "s.rivera");
        Assert.DoesNotContain(audit, a => (a.Detail ?? "").Contains("Rivera-pass-2026"));
    }

    [Fact]
    public async Task Imported_accounts_must_change_password_by_default()
    {
        (await ImportAsync(Header + "advisor-1,,,Advisor-pass-2026\n")).Errors.ForEach(e => Assert.Fail(e));
        var advisor = await factory.AsAsync("advisor-1", "Advisor-pass-2026");
        Assert.Equal(HttpStatusCode.Forbidden, (await advisor.GetAsync("/api/cases")).StatusCode);
    }

    [Fact]
    public async Task One_bad_row_changes_nothing()
    {
        var result = await ImportAsync(Header +
            "staff-1,s.rivera,s.rivera@bu.example,Rivera-pass-2026\n" +
            "nobody,,,Whatever-pass-2026\n" +
            "eval-cs,,,short1\n" +
            "admin-1,student-a,,Admin-pass-2026\n");

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("Line 3") && e.Contains("nobody"));
        Assert.Contains(result.Errors, e => e.Contains("Line 4") && e.Contains("at least"));
        Assert.Contains(result.Errors, e => e.Contains("Line 5") && e.Contains("already the username"));
        Assert.DoesNotContain(result.Errors, e => e.Contains("short1")); // passwords are never echoed
        (await LoginAsync("staff-1", ApiFactory.SeedPassword)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Duplicate_emails_and_a_wrong_header_are_rejected()
    {
        var dup = await ImportAsync(Header + "staff-1,,same@bu.example,Rivera-pass-2026\neval-cs,,same@bu.example,Chen-pass-2026\n");
        Assert.Contains(dup.Errors, e => e.Contains("share the email"));

        var header = await ImportAsync("user,password\nstaff-1,Rivera-pass-2026\n");
        Assert.Contains(header.Errors, e => e.Contains("header"));
    }

    [Fact]
    public async Task Dry_run_reports_without_saving()
    {
        var result = await ImportAsync(Header + "staff-1,s.rivera,,Rivera-pass-2026\n", dryRun: true);
        Assert.False(result.Applied);
        Assert.Single(result.Changes);
        (await LoginAsync("staff-1", ApiFactory.SeedPassword)).EnsureSuccessStatusCode();
    }
}
