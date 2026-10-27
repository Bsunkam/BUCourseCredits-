using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Rules;

public enum CreditRounding { Truncate, Round }

/// <summary>
/// Deterministic academic rules. These are never delegated to a model and are unit tested.
/// </summary>
public static class RulesEngine
{
    /// <summary>Step 7 of the brief: transfer credit is posted to MyBU Student with a grade of "T".</summary>
    public const string TransferGrade = "T";

    /// <summary>BR-05: transfer courses never earn BU Hub general education units.</summary>
    public const decimal HubUnits = 0m;

    /// <summary>Actor recorded when an existing approved equivalency is applied without a new human decision.</summary>
    public const string EquivalencyDatabaseActor = "system:equivalency-db";

    // Highest first. Anything not in this list (P, CR, W, IP...) cannot be compared and needs review.
    private static readonly string[] GradeScale =
        ["A+", "A", "A-", "B+", "B", "B-", "C+", "C", "C-", "D+", "D", "D-", "F"];

    /// <summary>
    /// BR-03: quarter credits convert at two-thirds of a semester credit.
    /// With Truncate and 2 decimals, 4 quarter credits become 2.66 (the figure in the brief).
    /// </summary>
    public static decimal ConvertCredits(decimal credits, CreditSystem system, CreditRounding rounding, int decimals)
    {
        if (credits < 0) throw new ArgumentOutOfRangeException(nameof(credits), "Credits cannot be negative.");
        if (system == CreditSystem.Semester) return credits;

        var semester = credits * 2m / 3m;
        return rounding == CreditRounding.Truncate
            ? Truncate(semester, decimals)
            : Math.Round(semester, decimals, MidpointRounding.AwayFromZero);
    }

    private static decimal Truncate(decimal value, int decimals)
    {
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Truncate(value * factor) / factor;
    }

    /// <summary>
    /// BR-04: true if the grade is below the minimum, false if it meets it,
    /// null if either grade is missing or not on the letter scale (an evaluator must decide).
    /// </summary>
    public static bool? IsGradeBelowMinimum(string? grade, string minimum)
    {
        var g = Rank(grade);
        var m = Rank(minimum);
        if (g is null || m is null) return null;
        return g > m;
    }

    private static int? Rank(string? grade)
    {
        if (string.IsNullOrWhiteSpace(grade)) return null;
        var index = Array.IndexOf(GradeScale, grade.Trim().ToUpperInvariant());
        return index < 0 ? null : index;
    }

    /// <summary>BR-06: all credits must be transferred within the deadline period after starting at BU.</summary>
    public static DateOnly Deadline(DateOnly startDate, int months) => startDate.AddMonths(months);

    public static int DaysRemaining(DateOnly deadline, DateOnly today) => deadline.DayNumber - today.DayNumber;

    public static string NormaliseCode(string code) =>
        string.Join(' ', code.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
