using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using TransferCredit.Api.Contracts;

namespace TransferCredit.Api.Documents;

public static class Containers
{
    public const string IntakeRaw = "intake-raw";   // quarantine: every file as received, before scanning
    public const string Documents = "documents";    // clean files only
    public const string Extracted = "extracted";    // extraction results keyed by checksum
}

public enum ScanResult { Pending, Clean, Malicious }

public class StorageOptions
{
    /// <summary>https://&lt;account&gt;.blob.core.windows.net. Empty means local disk (development).</summary>
    public string? AccountUrl { get; set; }
    public string LocalRoot { get; set; } = "App_Data/storage";

    /// <summary>Defender (read Defender for Storage index tags) or Simulated (EICAR string check, for local runs).</summary>
    public string ScanMode { get; set; } = "Simulated";
}

public interface IDocumentStorage
{
    string Kind { get; }
    Task WriteAsync(string container, string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string container, string key, CancellationToken ct);
    Task<bool> ExistsAsync(string container, string key, CancellationToken ct);
    Task CopyAsync(string fromContainer, string fromKey, string toContainer, string toKey, CancellationToken ct);
    Task<ScanResult> GetScanResultAsync(string container, string key, CancellationToken ct);

    /// <summary>Short-lived read link, or null when the file must be streamed through the API.</summary>
    Task<DocumentLinkDto?> GetReadLinkAsync(string container, string key, TimeSpan ttl, CancellationToken ct);
    Task<bool> IsReachableAsync(CancellationToken ct);
}

internal static class SimulatedScanner
{
    // The EICAR test string lets the intake tests prove quarantine works without real malware.
    private const string Eicar = "EICAR-STANDARD-ANTIVIRUS-TEST-FILE";

    public static async Task<ScanResult> ScanAsync(Stream content, CancellationToken ct)
    {
        using var reader = new StreamReader(content, System.Text.Encoding.Latin1);
        var text = await reader.ReadToEndAsync(ct);
        return text.Contains(Eicar, StringComparison.Ordinal) ? ScanResult.Malicious : ScanResult.Clean;
    }
}

/// <summary>Development storage on local disk with the same container layout as Blob Storage.</summary>
public class LocalDocumentStorage(string root, string scanMode) : IDocumentStorage
{
    public string Kind => "local";

    private string PathFor(string container, string key)
    {
        var full = Path.GetFullPath(Path.Combine(root, container, key));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage key.");
        return full;
    }

    public async Task WriteAsync(string container, string key, Stream content, string contentType, CancellationToken ct)
    {
        var path = PathFor(container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> OpenReadAsync(string container, string key, CancellationToken ct) =>
        Task.FromResult<Stream>(File.OpenRead(PathFor(container, key)));

    public Task<bool> ExistsAsync(string container, string key, CancellationToken ct) =>
        Task.FromResult(File.Exists(PathFor(container, key)));

    public Task CopyAsync(string fromContainer, string fromKey, string toContainer, string toKey, CancellationToken ct)
    {
        var to = PathFor(toContainer, toKey);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(PathFor(fromContainer, fromKey), to, overwrite: true);
        return Task.CompletedTask;
    }

    public async Task<ScanResult> GetScanResultAsync(string container, string key, CancellationToken ct)
    {
        if (!string.Equals(scanMode, "Simulated", StringComparison.OrdinalIgnoreCase)) return ScanResult.Pending;
        await using var stream = await OpenReadAsync(container, key, ct);
        return await SimulatedScanner.ScanAsync(stream, ct);
    }

    public Task<DocumentLinkDto?> GetReadLinkAsync(string container, string key, TimeSpan ttl, CancellationToken ct) =>
        Task.FromResult<DocumentLinkDto?>(null);

    public Task<bool> IsReachableAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        return Task.FromResult(true);
    }
}

/// <summary>Azure Blob Storage with managed identity. No account keys; private containers only.</summary>
public class BlobDocumentStorage(BlobServiceClient service, string scanMode) : IDocumentStorage
{
    // Index tag written by Defender for Storage on-upload malware scanning.
    private const string ScanTag = "Malware Scanning scan result";

    public static BlobDocumentStorage Create(string accountUrl, string scanMode) =>
        new(new BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential()), scanMode);

    public string Kind => "blob";

    private BlobClient Blob(string container, string key) => service.GetBlobContainerClient(container).GetBlobClient(key);

    public async Task WriteAsync(string container, string key, Stream content, string contentType, CancellationToken ct) =>
        await Blob(container, key).UploadAsync(content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);

    public async Task<Stream> OpenReadAsync(string container, string key, CancellationToken ct) =>
        await Blob(container, key).OpenReadAsync(cancellationToken: ct);

    public async Task<bool> ExistsAsync(string container, string key, CancellationToken ct) =>
        await Blob(container, key).ExistsAsync(ct);

    public async Task CopyAsync(string fromContainer, string fromKey, string toContainer, string toKey, CancellationToken ct)
    {
        await using var source = await OpenReadAsync(fromContainer, fromKey, ct);
        var props = await Blob(fromContainer, fromKey).GetPropertiesAsync(cancellationToken: ct);
        await Blob(toContainer, toKey).UploadAsync(source,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = props.Value.ContentType } }, ct);
    }

    public async Task<ScanResult> GetScanResultAsync(string container, string key, CancellationToken ct)
    {
        if (string.Equals(scanMode, "Simulated", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await OpenReadAsync(container, key, ct);
            return await SimulatedScanner.ScanAsync(stream, ct);
        }

        var tags = await Blob(container, key).GetTagsAsync(cancellationToken: ct);
        if (!tags.Value.Tags.TryGetValue(ScanTag, out var result)) return ScanResult.Pending;
        return string.Equals(result, "No threats found", StringComparison.OrdinalIgnoreCase)
            ? ScanResult.Clean
            : ScanResult.Malicious;
    }

    public async Task<DocumentLinkDto?> GetReadLinkAsync(string container, string key, TimeSpan ttl, CancellationToken ct)
    {
        var starts = DateTimeOffset.UtcNow.AddMinutes(-5);
        var expires = DateTimeOffset.UtcNow.Add(ttl);
        var delegationKey = await service.GetUserDelegationKeyAsync(starts, expires, ct);
        var sas = new BlobSasBuilder
        {
            BlobContainerName = container,
            BlobName = key,
            Resource = "b",
            StartsOn = starts,
            ExpiresOn = expires
        };
        sas.SetPermissions(BlobSasPermissions.Read);
        var uri = new BlobUriBuilder(Blob(container, key).Uri)
        {
            Sas = sas.ToSasQueryParameters(delegationKey.Value, service.AccountName)
        }.ToUri();
        return new DocumentLinkDto(uri.ToString(), "sas", expires);
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            return await service.GetBlobContainerClient(Containers.Documents).ExistsAsync(ct);
        }
        catch (Azure.RequestFailedException)
        {
            return false;
        }
    }
}
