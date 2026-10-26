using System.Security.Cryptography;
using System.Text;
using TransferCredit.Api.Rules;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Services;

/// <summary>
/// Reuse key (design 9.3): if any part changes, the course is evaluated again.
/// Per-student facts (grade, term) are deliberately NOT part of the key.
/// </summary>
public static class CacheKey
{
    public static string For(
        string institution, string code, string title, decimal credits, CreditSystem creditSystem,
        string? syllabusText, string program, string rulesVersion, string promptVersion)
    {
        var syllabusHash = string.IsNullOrWhiteSpace(syllabusText) ? "none" : Sha256(syllabusText.Trim());
        var parts = string.Join('|',
            institution.Trim().ToUpperInvariant(),
            RulesEngine.NormaliseCode(code),
            title.Trim().ToUpperInvariant(),
            credits.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            creditSystem,
            syllabusHash,
            program.Trim().ToUpperInvariant(),
            rulesVersion,
            promptVersion);
        return "sha256:" + Sha256(parts);
    }

    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
