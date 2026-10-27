using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransferCredit.Api.Auth;
using TransferCredit.Api.Contracts;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Endpoints;

public static class AuthEndpoints
{
    public const string SignInRateLimit = "sign-in";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Sign-in");

        auth.MapPost("/login", async (SignInRequest request, AccountService accounts, HttpContext http, CancellationToken ct) =>
        {
            var outcome = await accounts.SignInAsync(request.Username, request.Password, ct);
            if (outcome.User is not { } user)
                return Results.Problem(title: "Sign-in failed", detail: outcome.Error, statusCode: StatusCodes.Status401Unauthorized);

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AccountService.CreatePrincipal(user));
            return Results.Ok(ToCurrent(user));
        }).AllowAnonymous().RequireRateLimiting(SignInRateLimit);

        auth.MapPost("/logout", async (HttpContext http, ClaimsPrincipal user, AuditService audit,
            TransferCreditDbContext db, CancellationToken ct) =>
        {
            if (user.Identity?.IsAuthenticated == true)
            {
                audit.Record(user.Id(), null, AuditActions.UserSignedOut, $"{user.Identity.Name} signed out.");
                await db.SaveChangesAsync(ct);
            }
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        auth.MapPost("/change-password", async (ChangePasswordRequest request, AccountService accounts, HttpContext http,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var updated = await accounts.ChangePasswordAsync(user.Id(), request.CurrentPassword, request.NewPassword, ct);
            // The new security stamp ends every other session; renew this one so the user stays signed in here.
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AccountService.CreatePrincipal(updated));
            return Results.Ok(ToCurrent(updated));
        }).RequireAuthorization().RequireRateLimiting(SignInRateLimit);

        auth.MapGet("/options", async (IOptions<AuthOptions> options, TransferCreditDbContext db, CancellationToken ct) =>
        {
            var demo = options.Value.ShowDemoAccounts
                ? await db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Role).ThenBy(u => u.Id)
                    .Select(u => new DemoAccountDto(u.Id, u.DisplayName, u.Role)).ToListAsync(ct)
                : [];
            return Results.Ok(new { demoAccounts = demo });
        }).AllowAnonymous();

        // Read from the database, not the cookie, so a renamed account shows its new name at once.
        app.MapGet("/api/me", async (ClaimsPrincipal user, TransferCreditDbContext db, CancellationToken ct) =>
        {
            var id = user.Id();
            var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            return account is null ? Results.Unauthorized() : Results.Ok(ToCurrent(account));
        }).RequireAuthorization().WithTags("Sign-in");

        // ---------- Account administration ----------
        var users = app.MapGroup("/api/admin/users").WithTags("Admin").RequireAuthorization(Policies.Admin);

        users.MapGet("", async (AccountService accounts, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            return (await accounts.ListAsync(ct)).Select(u => ToAccount(u, now));
        });

        users.MapPost("", async (CreateUserRequest request, AccountService accounts, ClaimsPrincipal user, TimeProvider clock,
            CancellationToken ct) =>
        {
            var created = await accounts.CreateAsync(request, user.Id(), ct);
            return Results.Created($"/api/admin/users/{created.Id}", ToAccount(created, clock.GetUtcNow()));
        });

        users.MapPut("/{id}", async (string id, UpdateUserRequest request, AccountService accounts, ClaimsPrincipal user,
            TimeProvider clock, CancellationToken ct) =>
            Results.Ok(ToAccount(await accounts.UpdateAsync(id, request, user.Id(), ct), clock.GetUtcNow())));

        users.MapPost("/{id}/reset-password", async (string id, ResetPasswordRequest request, AccountService accounts,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            await accounts.ResetPasswordAsync(id, request.NewPassword, user.Id(), ct);
            return Results.NoContent();
        });

        users.MapPost("/{id}/unlock", async (string id, AccountService accounts, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await accounts.UnlockAsync(id, user.Id(), ct);
            return Results.NoContent();
        });
    }

    private static CurrentUserDto ToCurrent(AppUser u) =>
        new(u.Id, u.DisplayName, u.Role, u.Email, u.StudentRef, u.MustChangePassword);

    private static UserAccountDto ToAccount(AppUser u, DateTimeOffset now) =>
        new(u.Id, u.DisplayName, u.Role, u.Email, u.StudentRef, u.IsActive, u.LockoutEnd > now,
            u.PasswordHash is not null, u.MustChangePassword, u.LastSignInAt);
}
