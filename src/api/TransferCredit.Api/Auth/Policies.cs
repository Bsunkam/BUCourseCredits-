using System.Security.Claims;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Auth;

public static class AppClaims
{
    public const string StudentRef = "student_ref";
    public const string SecurityStamp = "tc_stamp";
    public const string MustChangePassword = "tc_must_change";
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
