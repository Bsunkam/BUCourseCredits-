using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransferCredit.Api.Contracts;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Auth;

public class AuthOptions
{
    /// <summary>Password given to seeded accounts that have none yet. Empty: seeded accounts cannot sign in.</summary>
    public string SeedPassword { get; set; } = "";
    /// <summary>Seeded accounts must choose their own password at first sign-in.</summary>
    public bool SeedPasswordMustChange { get; set; } = true;
    /// <summary>Lists the seeded usernames on the sign-in page. Development only.</summary>
    public bool ShowDemoAccounts { get; set; }
    public int MaxFailedSignIns { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int SessionHours { get; set; } = 8;
    public int SignInAttemptsPerMinute { get; set; } = 10;
    public int MinPasswordLength { get; set; } = 10;
}

public record SignInOutcome(AppUser? User, string? Error);

/// <summary>Sign-in, passwords and account administration. Roles come from the Users table, never from the client.</summary>
public partial class AccountService(
    TransferCreditDbContext db,
    IPasswordHasher<AppUser> hasher,
    AuditService audit,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    private const string BadCredentials = "Incorrect username or password.";

    // Verified against when the account does not exist, so a wrong username takes as long as a wrong password.
    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString()));

    private AuthOptions O => options.Value;

    public async Task<SignInOutcome> SignInAsync(string? username, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new(null, "Enter your username and password.");

        var name = username.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == name || u.Email == name, ct);
        var now = clock.GetUtcNow();

        if (user?.PasswordHash is null)
        {
            hasher.VerifyHashedPassword(new AppUser(), DummyHash.Value, password);
            audit.Record("anonymous", null, AuditActions.SignInFailed, "Sign-in failed: unknown account or no password set.");
            await db.SaveChangesAsync(ct);
            return new(null, BadCredentials);
        }

        if (user.LockoutEnd > now)
            return new(null, LockedMessage(user.LockoutEnd.Value, now));

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedSignInCount++;
            var locked = user.FailedSignInCount >= O.MaxFailedSignIns;
            if (locked)
            {
                user.LockoutEnd = now.AddMinutes(O.LockoutMinutes);
                user.FailedSignInCount = 0;
            }
            audit.Record(user.Id, null, AuditActions.SignInFailed,
                locked ? $"Wrong password; account locked for {O.LockoutMinutes} minutes." : "Wrong password.");
            await db.SaveChangesAsync(ct);
            return new(null, locked ? LockedMessage(user.LockoutEnd!.Value, now) : BadCredentials);
        }

        // Only someone who knows the password learns that the account is disabled.
        if (!user.IsActive)
            return new(null, "This account is disabled. Contact an administrator.");

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);
        user.FailedSignInCount = 0;
        user.LockoutEnd = null;
        user.LastSignInAt = now;
        audit.Record(user.Id, null, AuditActions.UserSignedIn, $"{user.DisplayName} signed in as {user.Role}.");
        await db.SaveChangesAsync(ct);
        return new(user, null);
    }

    private static string LockedMessage(DateTimeOffset end, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((end - now).TotalMinutes));
        return $"Too many failed attempts. This account is locked for {minutes} more minute{(minutes == 1 ? "" : "s")}.";
    }

    public static ClaimsPrincipal CreatePrincipal(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AppClaims.SecurityStamp, user.SecurityStamp)
        };
        if (user.StudentRef is not null) claims.Add(new Claim(AppClaims.StudentRef, user.StudentRef));
        if (user.MustChangePassword) claims.Add(new Claim(AppClaims.MustChangePassword, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>Checks the session against the account on every request: disabled or changed accounts lose access at once.</summary>
    public async Task<bool> IsSessionValidAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue(AppClaims.SecurityStamp);
        if (id is null || stamp is null) return false;
        return await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.IsActive && u.SecurityStamp == stamp, ct);
    }

    public async Task<AppUser> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException("Account not found.");
        if (user.PasswordHash is null ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? "") == PasswordVerificationResult.Failed)
            throw new ValidationFailedException("Your current password is incorrect.");
        if (currentPassword == newPassword)
            throw new ValidationFailedException("Choose a password you have not just used.");

        SetPassword(user, newPassword, mustChange: false);
        audit.Record(user.Id, null, AuditActions.PasswordChanged, $"{user.DisplayName} changed their password.");
        await db.SaveChangesAsync(ct);
        return user;
    }

    private void SetPassword(AppUser user, string password, bool mustChange)
    {
        ValidatePassword(user.Id, password);
        user.PasswordHash = hasher.HashPassword(user, password);
        user.MustChangePassword = mustChange;
        user.FailedSignInCount = 0;
        user.LockoutEnd = null;
        user.SecurityStamp = NewStamp();
    }

    /// <summary>Length matters most (NIST SP 800-63B); a letter plus a digit or symbol rules out the weakest choices.</summary>
    public void ValidatePassword(string username, string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < O.MinPasswordLength)
            throw new ValidationFailedException($"The password must be at least {O.MinPasswordLength} characters.");
        if (password.Length > 128)
            throw new ValidationFailedException("The password must be at most 128 characters.");
        if (!password.Any(char.IsLetter) || password.All(char.IsLetter))
            throw new ValidationFailedException("The password must contain a letter and a digit or symbol.");
        if (password.Contains(username, StringComparison.OrdinalIgnoreCase))
            throw new ValidationFailedException("The password must not contain the username.");
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");

    /// <summary>Startup: gives seeded accounts with no password the configured seed password.</summary>
    public async Task SeedPasswordsAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(O.SeedPassword)) return;
        var users = await db.Users.Where(u => u.PasswordHash == null).ToListAsync(ct);
        foreach (var user in users)
        {
            user.PasswordHash = hasher.HashPassword(user, O.SeedPassword);
            user.MustChangePassword = O.SeedPasswordMustChange;
            user.SecurityStamp = NewStamp();
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------- account administration (admin only) ----------

    public async Task<List<AppUser>> ListAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.Id).ToListAsync(ct);

    public async Task<AppUser> CreateAsync(CreateUserRequest request, string actorId, CancellationToken ct)
    {
        var id = (request.Id ?? "").Trim().ToLowerInvariant();
        if (!IsValidUsername(id)) throw new ValidationFailedException(UsernameRule);
        if (await db.Users.AnyAsync(u => u.Id == id, ct))
            throw new ValidationFailedException($"The username {id} is already taken.");

        var user = new AppUser { Id = id };
        await ApplyProfileAsync(user, request.DisplayName, request.Role, request.Email, request.StudentRef, ct);
        SetPassword(user, request.Password, mustChange: true);
        db.Users.Add(user);
        audit.Record(actorId, null, AuditActions.UserCreated, $"Account {id} created as {user.Role}.", newValue: user.Role.ToString());
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<AppUser> UpdateAsync(string id, UpdateUserRequest request, string actorId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"Account {id} not found.");

        if (user.Id == actorId && (request.Role != UserRole.Admin || !request.IsActive))
            throw new ValidationFailedException("You cannot remove your own admin role or disable your own account.");
        if (user.Role == UserRole.Admin && user.IsActive && (request.Role != UserRole.Admin || !request.IsActive)
            && !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct))
            throw new ValidationFailedException("At least one active admin account must remain.");

        var before = $"{user.Role}, {(user.IsActive ? "active" : "disabled")}, student ref {user.StudentRef ?? "none"}";
        var accessChanged = user.Role != request.Role || user.IsActive != request.IsActive;
        var studentRef = user.StudentRef;
        await ApplyProfileAsync(user, request.DisplayName, request.Role, request.Email, request.StudentRef, ct);
        user.IsActive = request.IsActive;

        // Role, status or case access changed: end the account's open sessions so the new rights apply at once.
        if (accessChanged || studentRef != user.StudentRef) user.SecurityStamp = NewStamp();

        var after = $"{user.Role}, {(user.IsActive ? "active" : "disabled")}, student ref {user.StudentRef ?? "none"}";
        audit.Record(actorId, null, AuditActions.UserUpdated, $"Account {user.Id} updated.", before, after);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task ResetPasswordAsync(string id, string newPassword, string actorId, CancellationToken ct)
    {
        if (id == actorId) throw new ValidationFailedException("Use Change password for your own account.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"Account {id} not found.");
        SetPassword(user, newPassword, mustChange: true);
        audit.Record(actorId, null, AuditActions.PasswordReset,
            $"Password for {user.Id} reset; the user must choose a new one at next sign-in.");
        await db.SaveChangesAsync(ct);
    }

    public async Task UnlockAsync(string id, string actorId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"Account {id} not found.");
        user.LockoutEnd = null;
        user.FailedSignInCount = 0;
        audit.Record(actorId, null, AuditActions.UserUpdated, $"Account {user.Id} unlocked.");
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyProfileAsync(AppUser user, string? displayName, UserRole role, string? email, string? studentRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
            throw new ValidationFailedException("Display name is required (at most 200 characters).");
        if (!Enum.IsDefined(role)) throw new ValidationFailedException("Unknown role.");

        var mail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        if (mail is not null && (mail.Length > 320 || !mail.Contains('@')))
            throw new ValidationFailedException("Enter a valid email address.");
        if (mail is not null && await db.Users.AnyAsync(u => u.Id != user.Id && u.Email == mail, ct))
            throw new ValidationFailedException("Another account already uses that email address.");

        // A student account is linked to its cases by student reference; other roles never carry one.
        var reference = role == UserRole.Student ? studentRef?.Trim() : null;
        if (role == UserRole.Student && string.IsNullOrEmpty(reference))
            throw new ValidationFailedException("A student account needs a student reference (for example DS-0003).");
        if (reference is { Length: > 64 }) throw new ValidationFailedException("Student reference is at most 64 characters.");

        user.DisplayName = displayName.Trim();
        user.Role = role;
        user.Email = mail;
        user.StudentRef = reference;
    }

    public const string UsernameRule = "Username must be 3 to 64 characters: lowercase letters, digits, '.', '_' or '-'.";

    public static bool IsValidUsername(string id) => UsernameRegex().IsMatch(id);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,63}$")]
    private static partial Regex UsernameRegex();
}
