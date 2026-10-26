namespace TransferCredit.Api.Documents;

public class IntakeOptions
{
    public bool Enabled { get; set; }
    public string? Mailbox { get; set; }
    public int PollSeconds { get; set; } = 90;
    public int MaxUploadMb { get; set; } = 20;
    public string AllowedFileTypes { get; set; } = "pdf,docx,png,jpg";
}

public record FileCheck(bool Ok, string? Error, string Extension, string ContentType);

/// <summary>Type, size, empty-file and signature checks shared by every intake channel (BR-15).</summary>
public class FileValidator(IntakeOptions options)
{
    private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> Known = new()
    {
        ["pdf"] = ("application/pdf", [[0x25, 0x50, 0x44, 0x46]]),                       // %PDF
        ["docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [[0x50, 0x4B, 0x03, 0x04]]), // zip
        ["png"] = ("image/png", [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
        ["jpg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
        ["jpeg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]])
    };

    public IReadOnlySet<string> Allowed { get; } = options.AllowedFileTypes
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.ToLowerInvariant())
        .SelectMany(x => x == "jpg" ? new[] { "jpg", "jpeg" } : [x])
        .ToHashSet();

    public long MaxBytes => options.MaxUploadMb * 1024L * 1024L;

    public FileCheck Check(string fileName, ReadOnlySpan<byte> content)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (!Allowed.Contains(ext) || !Known.TryGetValue(ext, out var known))
            return new(false, $"File type .{ext} is not allowed. Allowed: {string.Join(", ", Allowed)}.", ext, "");
        if (content.Length == 0) return new(false, "The file is empty.", ext, known.ContentType);
        if (content.Length > MaxBytes) return new(false, $"The file is larger than {options.MaxUploadMb} MB.", ext, known.ContentType);

        foreach (var signature in known.Signatures)
            if (content.Length >= signature.Length && content[..signature.Length].SequenceEqual(signature))
                return new(true, null, ext, known.ContentType);

        return new(false, $"The file content does not match the .{ext} extension.", ext, known.ContentType);
    }
}
