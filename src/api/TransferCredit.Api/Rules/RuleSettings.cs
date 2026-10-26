using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Rules;

/// <summary>A snapshot of the configurable rule values in SQL.</summary>
public record RuleSettingsSnapshot(
    string RulesVersion,
    IReadOnlyDictionary<string, string> MinimumGrades,
    CreditRounding Rounding,
    int Decimals,
    int DeadlineMonths)
{
    public string MinimumGradeFor(string program) =>
        MinimumGrades.TryGetValue(program, out var g) ? g
        : MinimumGrades.TryGetValue("Default", out var d) ? d
        : "C";
}

/// <summary>Flags the API computes and passes to the agent service. The agent never calculates these.</summary>
public record PolicyFlags(
    [property: JsonPropertyName("convertedCredits")] decimal ConvertedCredits,
    [property: JsonPropertyName("quarterConversionApplied")] bool QuarterConversionApplied,
    [property: JsonPropertyName("gradeBelowMinimum")] bool? GradeBelowMinimum,
    [property: JsonPropertyName("minimumGrade")] string MinimumGrade,
    [property: JsonPropertyName("buOnlyCourses")] IReadOnlyList<string> BuOnlyCourses,
    [property: JsonPropertyName("rulesVersion")] string RulesVersion);

public class RuleSettingsService(TransferCreditDbContext db)
{
    private RuleSettingsSnapshot? _cached;

    public async Task<RuleSettingsSnapshot> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var all = await db.RuleSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var grades = all.Where(kv => kv.Key.StartsWith("MinimumGrade:"))
            .ToDictionary(kv => kv.Key["MinimumGrade:".Length..], kv => kv.Value);

        _cached = new RuleSettingsSnapshot(
            all.GetValueOrDefault("RulesVersion", "r-001"),
            grades,
            Enum.TryParse<CreditRounding>(all.GetValueOrDefault("CreditRounding"), out var r) ? r : CreditRounding.Truncate,
            int.TryParse(all.GetValueOrDefault("CreditDecimals"), out var d) ? d : 2,
            int.TryParse(all.GetValueOrDefault("TransferDeadlineMonths"), out var m) ? m : 12);
        return _cached;
    }

    public async Task<decimal> ConvertAsync(decimal credits, CreditSystem system, CancellationToken ct = default)
    {
        var s = await GetAsync(ct);
        return RulesEngine.ConvertCredits(credits, system, s.Rounding, s.Decimals);
    }

    public async Task<PolicyFlags> ComputeFlagsAsync(SourceCourse course, string program, CancellationToken ct = default)
    {
        var s = await GetAsync(ct);
        var minimum = s.MinimumGradeFor(program);
        var buOnly = await BuOnlyCodesAsync(program, ct);
        return new PolicyFlags(
            course.ConvertedCredits,
            course.CreditSystem == CreditSystem.Quarter,
            RulesEngine.IsGradeBelowMinimum(course.Grade, minimum),
            minimum,
            buOnly,
            s.RulesVersion);
    }

    public Task<List<string>> BuOnlyCodesAsync(string program, CancellationToken ct = default) =>
        db.ProgramRules.AsNoTracking()
            .Where(r => r.Program == program && r.RuleType == ProgramRuleType.MustBeTakenAtBu)
            .Select(r => r.BUCourse!.Code)
            .ToListAsync(ct);

    /// <summary>BR-10: returns the rule source text if the BU course must be taken at BU for the program.</summary>
    public async Task<string?> BuOnlyRuleAsync(int buCourseId, string program, CancellationToken ct = default) =>
        await db.ProgramRules.AsNoTracking()
            .Where(r => r.Program == program && r.BUCourseId == buCourseId && r.RuleType == ProgramRuleType.MustBeTakenAtBu)
            .Select(r => r.Source)
            .FirstOrDefaultAsync(ct);
}
