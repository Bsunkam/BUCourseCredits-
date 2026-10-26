using TransferCredit.Api.Documents;
using TransferCredit.Api.Rules;
using TransferCredit.Api.Services;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Tests;

public class RulesEngineTests
{
    [Fact]
    public void Quarter_credits_convert_to_two_thirds_truncated_as_in_the_brief() =>
        Assert.Equal(2.66m, RulesEngine.ConvertCredits(4, CreditSystem.Quarter, CreditRounding.Truncate, 2));

    [Fact]
    public void Quarter_credits_round_when_policy_says_round() =>
        Assert.Equal(2.67m, RulesEngine.ConvertCredits(4, CreditSystem.Quarter, CreditRounding.Round, 2));

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(4.5)]
    public void Semester_credits_are_unchanged(decimal credits) =>
        Assert.Equal(credits, RulesEngine.ConvertCredits(credits, CreditSystem.Semester, CreditRounding.Truncate, 2));

    [Fact]
    public void Negative_credits_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RulesEngine.ConvertCredits(-1, CreditSystem.Semester, CreditRounding.Truncate, 2));

    [Theory]
    [InlineData("A", "C", false)]
    [InlineData("C", "C", false)]
    [InlineData("c-", "C", true)]
    [InlineData("D", "C", true)]
    [InlineData("F", "C", true)]
    [InlineData("B+", "B", false)]
    public void Grade_is_compared_against_the_minimum(string grade, string minimum, bool below) =>
        Assert.Equal(below, RulesEngine.IsGradeBelowMinimum(grade, minimum));

    [Theory]
    [InlineData("P")]
    [InlineData("CR")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_letter_or_missing_grades_need_an_evaluator(string? grade) =>
        Assert.Null(RulesEngine.IsGradeBelowMinimum(grade, "C"));

    [Fact]
    public void Deadline_is_one_year_after_start_and_counts_down()
    {
        var deadline = RulesEngine.Deadline(new DateOnly(2026, 9, 2), 12);
        Assert.Equal(new DateOnly(2027, 9, 2), deadline);
        Assert.Equal(-1, RulesEngine.DaysRemaining(deadline, new DateOnly(2027, 9, 3)));
    }

    [Fact]
    public void Course_codes_are_normalised() =>
        Assert.Equal("CS 2500", RulesEngine.NormaliseCode("  cs   2500 "));
}

public class CacheKeyTests
{
    private static string Key(string? syllabus = "s", string program = "Computer Science", string prompt = "p-001",
        string rules = "r-001", decimal credits = 4) =>
        CacheKey.For("Northeastern University", "CS 2510", "Fundamentals 2", credits, CreditSystem.Semester,
            syllabus, program, rules, prompt);

    [Fact]
    public void Identical_inputs_give_the_same_key() => Assert.Equal(Key(), Key());

    [Fact]
    public void Any_key_input_change_forces_a_new_evaluation()
    {
        Assert.NotEqual(Key(), Key(syllabus: "different"));
        Assert.NotEqual(Key(), Key(syllabus: null));
        Assert.NotEqual(Key(), Key(program: "Mathematics"));
        Assert.NotEqual(Key(), Key(prompt: "p-002"));
        Assert.NotEqual(Key(), Key(rules: "r-002"));
        Assert.NotEqual(Key(), Key(credits: 3));
    }
}

public class FileValidatorTests
{
    private readonly FileValidator _validator = new(new IntakeOptions { MaxUploadMb = 1 });

    [Fact]
    public void Valid_pdf_passes() =>
        Assert.True(_validator.Check("syllabus.pdf", "%PDF-1.4 x"u8).Ok);

    [Fact]
    public void Signature_must_match_extension() =>
        Assert.False(_validator.Check("syllabus.pdf", "PK\u0003\u0004"u8).Ok);

    [Fact]
    public void Disallowed_types_and_empty_files_are_rejected()
    {
        Assert.False(_validator.Check("run.exe", "MZ"u8).Ok);
        Assert.False(_validator.Check("empty.pdf", ReadOnlySpan<byte>.Empty).Ok);
    }

    [Fact]
    public void Oversized_files_are_rejected()
    {
        var big = new byte[2 * 1024 * 1024];
        "%PDF"u8.CopyTo(big);
        Assert.False(_validator.Check("big.pdf", big).Ok);
    }
}
