using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using TransferCredit.Api.Contracts;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Tests;

internal sealed class TightRateLimitFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:SignInAttemptsPerMinute", "3");
    }
}

public sealed class AuthTests : IDisposable
{
    private readonly ApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { username, password });

    private static async Task<string> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()!;

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_answer()
    {
        var client = factory.CreateClient();

        var wrong = await LoginAsync(client, "staff-1", "not-the-password-1");
        var unknown = await LoginAsync(client, "nobody", "not-the-password-1");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await DetailAsync(wrong), await DetailAsync(unknown));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Sign_in_by_email_returns_the_role_from_the_database()
    {
        var client = factory.CreateClient();
        var response = await LoginAsync(client, "EVAL-CS@demo.local", ApiFactory.SeedPassword);
        response.EnsureSuccessStatusCode();

        var me = (await client.GetFromJsonAsync<CurrentUserDto>("/api/me", WorkflowTests.Json))!;
        Assert.Equal("eval-cs", me.Id);
        Assert.Equal(UserRole.Evaluator, me.Role);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_until_an_admin_unlocks_it()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++) await LoginAsync(client, "staff-1", "wrong-password-1");

        var locked = await LoginAsync(client, "staff-1", ApiFactory.SeedPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Contains("locked", await DetailAsync(locked));

        var admin = await factory.AsAsync("admin-1");
        (await admin.PostAsync("/api/admin/users/staff-1/unlock", null)).EnsureSuccessStatusCode();
        (await LoginAsync(client, "staff-1", ApiFactory.SeedPassword)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Each_role_reaches_only_its_own_endpoints()
    {
        var anonymous = factory.CreateClient();
        var student = await factory.AsAsync("student-a");
        var advisor = await factory.AsAsync("advisor-1");
        var evaluator = await factory.AsAsync("eval-cs");
        var staff = await factory.AsAsync("staff-1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/cases")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/cases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await advisor.GetAsync("/api/cases")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await advisor.GetAsync("/api/cases/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync("/api/intake")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/intake")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task The_old_demo_header_no_longer_signs_anyone_in()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-User", "admin-1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/demo/users")).StatusCode);
    }

    [Fact]
    public async Task Disabling_or_changing_the_role_of_an_account_ends_its_session_at_once()
    {
        var admin = await factory.AsAsync("admin-1");
        var staff = await factory.AsAsync("staff-1");
        var advisor = await factory.AsAsync("advisor-1");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/cases")).StatusCode);

        (await admin.PutAsJsonAsync("/api/admin/users/staff-1",
            new UpdateUserRequest("Sam Rivera", UserRole.Staff, "staff@demo.local", null, IsActive: false), WorkflowTests.Json))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/cases")).StatusCode);
        var disabled = await LoginAsync(factory.CreateClient(), "staff-1", ApiFactory.SeedPassword);
        Assert.Contains("disabled", await DetailAsync(disabled));

        (await admin.PutAsJsonAsync("/api/admin/users/advisor-1",
            new UpdateUserRequest("Jordan Lee", UserRole.Staff, "advisor@demo.local", null, IsActive: true), WorkflowTests.Json))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await advisor.GetAsync("/api/cases")).StatusCode);
        var promoted = await factory.AsAsync("advisor-1");
        Assert.Equal(HttpStatusCode.OK, (await promoted.GetAsync("/api/intake")).StatusCode);
    }

    [Fact]
    public async Task A_new_account_must_replace_its_temporary_password_before_doing_anything()
    {
        var admin = await factory.AsAsync("admin-1");
        var created = await admin.PostAsJsonAsync("/api/admin/users",
            new CreateUserRequest("eval-math", "Dr. Ada Park (Math evaluator)", UserRole.Evaluator, "eval-math@demo.local", null,
                "Temporary-pass-1"), WorkflowTests.Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var user = await factory.AsAsync("eval-math", "Temporary-pass-1");
        var me = (await user.GetFromJsonAsync<CurrentUserDto>("/api/me", WorkflowTests.Json))!;
        Assert.True(me.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/cases")).StatusCode);

        var weak = await user.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temporary-pass-1", "short1"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        (await user.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temporary-pass-1", "Chosen-by-me-2026")))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/cases")).StatusCode); // this session was renewed
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await LoginAsync(factory.CreateClient(), "eval-math", "Temporary-pass-1")).StatusCode);
    }

    [Fact]
    public async Task A_student_account_needs_a_student_reference_and_sees_only_its_own_cases()
    {
        var admin = await factory.AsAsync("admin-1");
        var missingRef = await admin.PostAsJsonAsync("/api/admin/users",
            new CreateUserRequest("student-c", "Demo Student C", UserRole.Student, null, null, "Temporary-pass-1"), WorkflowTests.Json);
        Assert.Equal(HttpStatusCode.BadRequest, missingRef.StatusCode);

        (await admin.PostAsJsonAsync("/api/admin/users",
            new CreateUserRequest("student-c", "Demo Student C", UserRole.Student, null, "DS-0003", "Temporary-pass-1"), WorkflowTests.Json))
            .EnsureSuccessStatusCode();
        var student = await factory.AsAsync("student-c", "Temporary-pass-1");
        (await student.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temporary-pass-1", "Chosen-by-me-2026")))
            .EnsureSuccessStatusCode();

        var cases = (await student.GetFromJsonAsync<List<CaseSummaryDto>>("/api/me/cases", WorkflowTests.Json))!;
        Assert.Empty(cases); // the seeded demo case belongs to DS-0001
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/cases/1/status")).StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_lock_themselves_out()
    {
        var admin = await factory.AsAsync("admin-1");
        var demote = await admin.PutAsJsonAsync("/api/admin/users/admin-1",
            new UpdateUserRequest("Alex Morgan", UserRole.Staff, "admin@demo.local", null, IsActive: true), WorkflowTests.Json);
        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
        var reset = await admin.PostAsJsonAsync("/api/admin/users/admin-1/reset-password", new ResetPasswordRequest("Another-pass-2026"));
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var staff = await factory.AsAsync("staff-1");
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/cases")).StatusCode);
    }

    [Fact]
    public async Task Admin_list_never_exposes_password_hashes_and_demo_accounts_stay_hidden_by_default()
    {
        var admin = await factory.AsAsync("admin-1");
        var body = await admin.GetStringAsync("/api/admin/users");
        Assert.Contains("staff-1", body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", body); // Identity v3 hash prefix

        var options = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/auth/options");
        Assert.Equal(0, options.GetProperty("demoAccounts").GetArrayLength());
    }

    [Fact]
    public async Task Sign_in_attempts_are_rate_limited_per_client()
    {
        using var tight = new TightRateLimitFactory();
        var client = tight.CreateClient();
        for (var i = 0; i < 3; i++) await LoginAsync(client, "staff-1", "wrong-password-1");
        var limited = await LoginAsync(client, "staff-1", ApiFactory.SeedPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
