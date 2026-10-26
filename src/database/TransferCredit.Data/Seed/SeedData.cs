using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Data.Entities;

namespace TransferCredit.Data.Seed;

public record CatalogueSeed(
    List<InstitutionSeed> Institutions,
    List<BUCourseSeed> BuCourses,
    List<ProgramRuleSeed> ProgramRules,
    List<EquivalencySeed> Equivalencies);

public record InstitutionSeed(string Name, CreditSystem CreditSystem, string? CatalogueSource);

public record BUCourseSeed(
    string Code, string Title, string Program, string RequirementType, decimal Credits,
    VerifiedStatus Verified, string? SourceUrl, string? Description);

public record ProgramRuleSeed(string Program, string BuCourseCode, ProgramRuleType RuleType, string Source);

public record EquivalencySeed(
    string Institution, string SourceCode, string SourceTitle, string Program,
    OutcomeType Outcome, string? BuCourseCode, VerifiedStatus Verified);

public record DemoSeed(List<DemoUserSeed> Users, List<DemoCaseSeed> Cases);

public record DemoUserSeed(string Id, string DisplayName, UserRole Role, string? Email, string? StudentRef);

public record DemoCaseSeed(
    string CaseRef, string StudentRef, string StudentName, string? StudentEmail,
    string Program, DateOnly StartDate, List<DemoCourseSeed> Courses);

public record DemoCourseSeed(
    string Institution, string Code, string Title, decimal Credits, string? Grade, string? Term,
    string? Description, string? Syllabus);

/// <summary>Loads the embedded JSON seed files and writes reference data into an empty database.</summary>
public static class SeedData
{
    public const string RulesVersion = "r-001";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static CatalogueSeed LoadCatalogue() => Load<CatalogueSeed>("catalogue.json");

    public static DemoSeed LoadDemo() => Load<DemoSeed>("demo.json");

    private static T Load<T>(string fileName)
    {
        var assembly = typeof(SeedData).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Seed." + fileName));
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<T>(stream, Json)
            ?? throw new InvalidOperationException($"Seed file {fileName} is empty.");
    }

    public static IReadOnlyList<RuleSetting> DefaultSettings() =>
    [
        new() { Key = "RulesVersion", Value = RulesVersion, Description = "Bump when any rule value changes; part of the reuse cache key." },
        new() { Key = "MinimumGrade:Default", Value = "C", Description = "BR-04: minimum grade for transfer. Confirm per school." },
        new() { Key = "MinimumGrade:Computer Science", Value = "C", Description = "BR-04: CS program threshold (sponsor to confirm)." },
        new() { Key = "CreditRounding", Value = "Truncate", Description = "BR-03: Truncate (4 quarter -> 2.66) or Round (-> 2.67). Discovery question 8." },
        new() { Key = "CreditDecimals", Value = "2", Description = "Decimal places for converted credits." },
        new() { Key = "TransferDeadlineMonths", Value = "12", Description = "BR-06: credits must transfer within one year of starting at BU." }
    ];

    /// <summary>Seeds reference data and demo users. Safe to call on every start: it only fills empty tables.</summary>
    public static async Task SeedReferenceDataAsync(TransferCreditDbContext db, CancellationToken ct = default)
    {
        if (!await db.RuleSettings.AnyAsync(ct))
            db.RuleSettings.AddRange(DefaultSettings());

        if (!await db.DemoUsers.AnyAsync(ct))
        {
            db.DemoUsers.AddRange(LoadDemo().Users.Select(u => new DemoUser
            {
                Id = u.Id, DisplayName = u.DisplayName, Role = u.Role, Email = u.Email, StudentRef = u.StudentRef
            }));
        }

        if (await db.BUCourses.AnyAsync(ct))
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var catalogue = LoadCatalogue();
        var now = DateTimeOffset.UtcNow;
        var effective = new DateOnly(2025, 9, 1);

        var institutions = catalogue.Institutions.ToDictionary(
            i => i.Name,
            i => new SourceInstitution { Name = i.Name, CreditSystem = i.CreditSystem, CatalogueSource = i.CatalogueSource });
        db.Institutions.AddRange(institutions.Values);

        var buCourses = catalogue.BuCourses.Select(c => new BUCourse
        {
            Code = c.Code, Title = c.Title, Program = c.Program, RequirementType = c.RequirementType,
            Credits = c.Credits, Description = c.Description, VerifiedStatus = c.Verified,
            SourceUrl = c.SourceUrl, EffectiveDate = effective
        }).ToList();
        db.BUCourses.AddRange(buCourses);

        BUCourse Find(string code) => buCourses.First(c => c.Code == code);

        db.ProgramRules.AddRange(catalogue.ProgramRules.Select(r => new ProgramRule
        {
            Program = r.Program, BUCourse = Find(r.BuCourseCode), RuleType = r.RuleType, Source = r.Source
        }));

        db.EquivalencyRules.AddRange(catalogue.Equivalencies.Select(e => new EquivalencyRule
        {
            Institution = institutions[e.Institution],
            SourceCode = e.SourceCode,
            SourceTitle = e.SourceTitle,
            Program = e.Program,
            OutcomeType = e.Outcome,
            BUCourse = e.BuCourseCode is null ? null : Find(e.BuCourseCode),
            ApprovalStatus = ApprovalStatus.Approved,
            VerifiedStatus = e.Verified,
            EffectiveFrom = effective,
            DecisionSource = "Seed: course-match workbook (synthetic placeholder)",
            CreatedAt = now
        }));

        await db.SaveChangesAsync(ct);
    }
}
