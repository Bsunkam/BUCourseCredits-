using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Services;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Auth;

/// <summary>One CSV row. The password is held only long enough to hash it and is never logged or audited.</summary>
public record UserImportRow(int Line, string CurrentUsername, string? NewUsername, string? Email, string Password);

public record UserImportResult(bool Applied, List<string> Errors, List<string> Changes);

/// <summary>
/// Sets usernames, emails and passwords for existing accounts from a CSV file:
/// <c>currentUsername,newUsername,email,password</c>. Every row is checked first; one bad row changes nothing.
/// Roles and student references stay as they are. Past audit events keep the old username; a
/// <see cref="AuditActions.UserImported"/> event records each rename.
/// </summary>
public class UserImportService(
    TransferCreditDbContext db,
    IPasswordHasher<AppUser> hasher,
    AccountService accounts,
    AuditService audit)
{
    public const string Actor = "system:user-import";
    private static readonly string[] Header = ["currentusername", "newusername", "email", "password"];

    public static List<UserImportRow> Parse(string csv, List<string> errors)
    {
        var records = ReadCsv(csv);
        var rows = new List<UserImportRow>();
        if (records.Count == 0)
        {
            errors.Add("The file is empty.");
            return rows;
        }

        var header = records[0].Fields.Select(f => f.Trim().ToLowerInvariant()).ToArray();
        if (!header.SequenceEqual(Header))
        {
            errors.Add("Line 1: the header must be exactly: currentUsername,newUsername,email,password");
            return rows;
        }

        foreach (var (line, fields) in records.Skip(1))
        {
            if (fields.All(string.IsNullOrWhiteSpace)) continue; // blank line
            if (fields.Count != Header.Length)
            {
                errors.Add($"Line {line}: expected 4 columns, found {fields.Count}. Put a password containing a comma in double quotes.");
                continue;
            }
            static string? Opt(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            // The password is taken exactly as written: leading or trailing spaces are part of it.
            rows.Add(new UserImportRow(line, fields[0].Trim().ToLowerInvariant(), Opt(fields[1])?.ToLowerInvariant(),
                Opt(fields[2])?.ToLowerInvariant(), fields[3]));
        }
        if (rows.Count == 0 && errors.Count == 0) errors.Add("The file has a header but no rows.");
        return rows;
    }

    public async Task<UserImportResult> ImportAsync(List<UserImportRow> rows, bool mustChangePassword, bool dryRun, CancellationToken ct)
    {
        var errors = new List<string>();
        var users = await db.Users.ToListAsync(ct);
        var byId = users.ToDictionary(u => u.Id);

        // Where every account ends up, so a rename can be checked against the whole result (swaps included).
        var finalId = users.ToDictionary(u => u.Id, u => u.Id);
        var finalEmail = users.ToDictionary(u => u.Id, u => u.Email);
        var seen = new HashSet<string>();

        foreach (var r in rows)
        {
            if (!byId.ContainsKey(r.CurrentUsername))
            {
                errors.Add($"Line {r.Line}: no account named '{r.CurrentUsername}'.");
                continue;
            }
            if (!seen.Add(r.CurrentUsername))
            {
                errors.Add($"Line {r.Line}: '{r.CurrentUsername}' appears more than once.");
                continue;
            }
            var id = r.NewUsername ?? r.CurrentUsername;
            if (!AccountService.IsValidUsername(id)) errors.Add($"Line {r.Line}: '{id}': {AccountService.UsernameRule}");
            // A new name may not reuse any current one, even one being renamed away (no swaps in one run).
            if (id != r.CurrentUsername && byId.ContainsKey(id))
                errors.Add($"Line {r.Line}: '{id}' is already the username of another account.");
            if (r.Email is { } mail && (mail.Length > 320 || !mail.Contains('@') || mail.StartsWith('@') || mail.EndsWith('@')))
                errors.Add($"Line {r.Line}: '{mail}' is not a valid email address.");
            try { accounts.ValidatePassword(id, r.Password); }
            catch (ValidationFailedException ex) { errors.Add($"Line {r.Line}: {ex.Message}"); }

            finalId[r.CurrentUsername] = id;
            if (r.Email is not null) finalEmail[r.CurrentUsername] = r.Email;
        }

        foreach (var clash in finalId.GroupBy(x => x.Value).Where(g => g.Count() > 1))
            errors.Add($"Two accounts would both be named '{clash.Key}': {string.Join(" and ", clash.Select(x => x.Key))}.");
        foreach (var clash in finalEmail.Where(x => x.Value is not null).GroupBy(x => x.Value).Where(g => g.Count() > 1))
            errors.Add($"Two accounts would share the email '{clash.Key}': {string.Join(" and ", clash.Select(x => x.Key))}.");

        if (errors.Count > 0) return new(false, errors, []);

        var changes = new List<string>();
        foreach (var r in rows)
        {
            var user = byId[r.CurrentUsername];
            var id = finalId[r.CurrentUsername];
            var notes = new List<string>();
            if (id != user.Id) notes.Add($"renamed to {id}");
            if (r.Email is not null && r.Email != user.Email) notes.Add($"email {user.Email ?? "none"} -> {r.Email}");
            notes.Add(mustChangePassword ? "password set (must change at first sign-in)" : "password set");
            changes.Add($"{user.Id} ({user.Role}): {string.Join("; ", notes)}");
            if (dryRun) continue;

            var target = user;
            if (id != user.Id)
            {
                // The username is the key: replace the row, keeping everything else about the account.
                db.Users.Remove(user);
                target = new AppUser
                {
                    Id = id, DisplayName = user.DisplayName, Role = user.Role, StudentRef = user.StudentRef,
                    IsActive = user.IsActive, LastSignInAt = user.LastSignInAt, Email = user.Email
                };
                db.Users.Add(target);
            }
            if (r.Email is not null) target.Email = r.Email;
            target.PasswordHash = hasher.HashPassword(target, r.Password);
            target.MustChangePassword = mustChangePassword;
            target.FailedSignInCount = 0;
            target.LockoutEnd = null;
            target.SecurityStamp = Guid.NewGuid().ToString("N"); // ends any open session

            audit.Record(Actor, null, AuditActions.UserImported, $"Credentials imported for {user.Id}: {string.Join("; ", notes)}.",
                oldValue: user.Id, newValue: id);
        }

        if (!dryRun) await db.SaveChangesAsync(ct); // one save: all rows apply, or none do
        return new(!dryRun, [], changes);
    }

    /// <summary>Minimal RFC 4180 reader: quoted fields may hold commas, quotes ("") and line breaks.</summary>
    private static List<(int Line, List<string> Fields)> ReadCsv(string text)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;
        int line = 1, start = 1;

        void EndField() { fields.Add(field.ToString()); field.Clear(); }
        void EndRecord()
        {
            EndField();
            if (any || fields.Count > 1 || fields[0].Length > 0) records.Add((start, fields));
            fields = [];
            any = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i == 0 && c == '﻿') continue; // byte order mark from Excel
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { if (c == '\n') line++; field.Append(c); }
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; any = true; break;
                case ',': EndField(); any = true; break;
                case '\r': break;
                case '\n': EndRecord(); line++; start = line; break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || fields.Count > 0 || any) EndRecord();
        return records;
    }
}

/// <summary><c>dotnet run -- import-users &lt;file.csv&gt; [--dry-run] [--no-password-change]</c></summary>
public static class UserImportCommand
{
    public const string Name = "import-users";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var path = args.FirstOrDefault(a => !a.StartsWith("--"));
        var dryRun = args.Contains("--dry-run");
        var mustChange = !args.Contains("--no-password-change");
        if (path is null)
        {
            Console.Error.WriteLine("Usage: dotnet run -- import-users <file.csv> [--dry-run] [--no-password-change]");
            return 2;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 2;
        }

        var errors = new List<string>();
        var rows = UserImportService.Parse(await File.ReadAllTextAsync(path), errors);
        UserImportResult result;
        if (errors.Count > 0) result = new(false, errors, []);
        else
        {
            using var scope = services.CreateScope();
            result = await scope.ServiceProvider.GetRequiredService<UserImportService>()
                .ImportAsync(rows, mustChange, dryRun, CancellationToken.None);
        }

        if (result.Errors.Count > 0)
        {
            Console.Error.WriteLine($"No accounts were changed. Fix these {result.Errors.Count} problem(s) and run again:");
            foreach (var e in result.Errors) Console.Error.WriteLine($"  - {e}");
            return 1;
        }

        Console.WriteLine(dryRun ? $"Dry run: {result.Changes.Count} account(s) would change. Nothing was saved." : $"{result.Changes.Count} account(s) updated:");
        foreach (var c in result.Changes) Console.WriteLine($"  - {c}");
        if (!dryRun)
            Console.WriteLine($"Delete {Path.GetFullPath(path)} now: it holds the passwords in plain text.");
        return 0;
    }
}
