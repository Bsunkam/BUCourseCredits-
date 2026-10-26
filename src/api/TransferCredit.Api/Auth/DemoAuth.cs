using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Auth;

/// <summary>
/// DEMO ONLY: the UI picks a seeded synthetic user and sends its id in the X-Demo-User header.
/// Replace with Microsoft Entra ID (AddMicrosoftIdentityWebApi) before any real use.
/// </summary>
public class DemoAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TransferCreditDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Demo";
    public const string Header = "X-Demo-User";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var values) || string.IsNullOrWhiteSpace(values))
            return AuthenticateResult.NoResult();

        var id = values.ToString();
        var user = await db.DemoUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return AuthenticateResult.Fail("Unknown demo user.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (user.StudentRef is not null) claims.Add(new Claim(AppClaims.StudentRef, user.StudentRef));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

public static class AppClaims
{
    public const string StudentRef = "student_ref";
}

public static class Policies
{
    public const string Staff = "Staff";           // Credit Evaluation staff, admin
    public const string Evaluator = "Evaluator";   // department evaluator, admin
    public const string Reviewer = "Reviewer";     // staff, evaluator, admin
    public const string Internal = "Internal";     // reviewer roles plus advisor (read-only)
    public const string Admin = "Admin";

    public static void Register(Microsoft.AspNetCore.Authorization.AuthorizationOptions o)
    {
        o.AddPolicy(Staff, p => p.RequireRole(nameof(UserRole.Staff), nameof(UserRole.Admin)));
        o.AddPolicy(Evaluator, p => p.RequireRole(nameof(UserRole.Evaluator), nameof(UserRole.Admin)));
        o.AddPolicy(Reviewer, p => p.RequireRole(nameof(UserRole.Staff), nameof(UserRole.Evaluator), nameof(UserRole.Admin)));
        o.AddPolicy(Internal, p => p.RequireRole(
            nameof(UserRole.Staff), nameof(UserRole.Evaluator), nameof(UserRole.Admin), nameof(UserRole.Advisor)));
        o.AddPolicy(Admin, p => p.RequireRole(nameof(UserRole.Admin)));
    }
}

public static class UserExtensions
{
    public static string Id(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";

    public static string? StudentRef(this ClaimsPrincipal user) => user.FindFirstValue(AppClaims.StudentRef);

    public static bool IsReviewer(this ClaimsPrincipal user) =>
        user.IsInRole(nameof(UserRole.Staff)) || user.IsInRole(nameof(UserRole.Evaluator)) || user.IsInRole(nameof(UserRole.Admin));

    public static bool IsInternal(this ClaimsPrincipal user) => user.IsReviewer() || user.IsInRole(nameof(UserRole.Advisor));

    /// <summary>Least privilege: students see only their own case.</summary>
    public static bool CanView(this ClaimsPrincipal user, TransferCase c) =>
        user.IsInternal() || (user.StudentRef() is { } r && r == c.DemoStudentRef);

    public static bool CanUploadTo(this ClaimsPrincipal user, TransferCase c) =>
        user.IsReviewer() || (user.StudentRef() is { } r && r == c.DemoStudentRef);
}
