using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Data.Entities;

namespace TransferCredit.Data.Seed;

public record CatalogueSeed(
    List<InstitutionSeed> Institutions,
    List<BUCourseSeed> BuCourses,
    List<ProgramRuleSeed> ProgramRules,
    List<SourceCatalogueSeed> SourceCatalogue,
    List<EquivalencySeed> Equivalencies);

public record InstitutionSeed(string Name, CreditSystem CreditSystem, string? CatalogueSource);

public record BUCourseSeed(
    string Code, string Title, string Program, string RequirementType, decimal Credits,
    VerifiedStatus Verified, string? SourceUrl, string? Description, string? LearningOutcomes);

public record SourceCatalogueSeed(
    string Institution, string Code, string Title, decimal Credits, string? Description, string? LearningOutcomes);

public record ProgramRuleSeed(string Program, string BuCourseCode, ProgramRuleType RuleType, string Source);

public record EquivalencySeed(
    string Institution, string SourceCode, string SourceTitle, string Program,
    OutcomeType Outcome, string? BuCourseCode, VerifiedStatus Verified);

public record DemoSeed(List<DemoUserSeed> Users, List<DemoCaseSeed> Cases);

public record DemoUserSeed(string Id, string DisplayName, UserRole Role, string? Email, string? StudentRef);

public record DemoCaseSeed(
    string CaseRef, string Template, string StudentRef, string StudentName, string? StudentEmail,
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

        await TopUpCatalogueAsync(db, LoadCatalogue(), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds catalogue rows that are missing, so a database seeded from an older catalogue.json picks up new courses
    /// and past decisions on the next start. Existing rows are never changed, except that missing learning outcomes
    /// are filled in. Evaluator-created equivalencies are left alone.
    /// </summary>
    private static async Task TopUpCatalogueAsync(TransferCreditDbContext db, CatalogueSeed catalogue, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var effective = new DateOnly(2025, 9, 1);

        var institutions = await db.Institutions.ToDictionaryAsync(i => i.Name, ct);
        foreach (var i in catalogue.Institutions.Where(i => !institutions.ContainsKey(i.Name)))
        {
            var added = new SourceInstitution { Name = i.Name, CreditSystem = i.CreditSystem, CatalogueSource = i.CatalogueSource };
            db.Institutions.Add(added);
            institutions[i.Name] = added;
        }

        var buCourses = await db.BUCourses.ToListAsync(ct);
        foreach (var c in catalogue.BuCourses)
        {
            var existing = buCourses.FirstOrDefault(x => x.Code == c.Code && x.Program == c.Program);
            if (existing is not null)
            {
                existing.LearningOutcomes ??= c.LearningOutcomes;
                continue;
            }
            var added = new BUCourse
            {
                Code = c.Code, Title = c.Title, Program = c.Program, RequirementType = c.RequirementType,
                Credits = c.Credits, Description = c.Description, LearningOutcomes = c.LearningOutcomes,
                VerifiedStatus = c.Verified, SourceUrl = c.SourceUrl, EffectiveDate = effective
            };
            db.BUCourses.Add(added);
            buCourses.Add(added);
        }

        BUCourse Find(string code) => buCourses.First(c => c.Code == code);

        var programRules = await db.ProgramRules.Include(r => r.BUCourse).ToListAsync(ct);
        db.ProgramRules.AddRange(catalogue.ProgramRules
            .Where(r => !programRules.Any(x => x.Program == r.Program && x.BUCourse!.Code == r.BuCourseCode && x.RuleType == r.RuleType))
            .Select(r => new ProgramRule { Program = r.Program, BUCourse = Find(r.BuCourseCode), RuleType = r.RuleType, Source = r.Source }));

        var sourceCatalogue = await db.SourceCatalogueCourses.Include(c => c.Institution).ToListAsync(ct);
        db.SourceCatalogueCourses.AddRange(catalogue.SourceCatalogue
            .Where(c => !sourceCatalogue.Any(x => x.Institution!.Name == c.Institution && x.Code == c.Code))
            .Select(c => new SourceCatalogueCourse
            {
                Institution = institutions[c.Institution], Code = c.Code, Title = c.Title, Credits = c.Credits,
                Description = c.Description, LearningOutcomes = c.LearningOutcomes
            }));

        var equivalencies = await db.EquivalencyRules.Include(r => r.Institution).ToListAsync(ct);
        db.EquivalencyRules.AddRange(catalogue.Equivalencies
            .Where(e => !equivalencies.Any(x => x.Institution!.Name == e.Institution && x.SourceCode == e.SourceCode && x.Program == e.Program))
            .Select(e => new EquivalencyRule
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
                DecisionSource = "Seed: synthetic past decision",
                CreatedAt = now
            }));
    }
}
