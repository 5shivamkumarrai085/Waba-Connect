using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WhatsAppCampaignApi.Services.Storage;

/// <summary>Where uploaded files live and what may be stored or fetched.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Media that must be reachable by URL (campaign images and documents Meta fetches, avatars,
    /// bot-flow media). Relative paths resolve against the content root. Default: wwwroot/uploads,
    /// which is where every existing <c>/uploads/...</c> reference in the database points.
    /// </summary>
    public string? PublicUploadsPath { get; set; }

    /// <summary>
    /// Files that must never be served: CSV imports holding customer PII. Relative paths resolve
    /// against the content root. Default: App_Data/private.
    /// </summary>
    public string? PrivatePath { get; set; }

    /// <summary>Largest media upload accepted, in bytes.</summary>
    public long MaxMediaBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>
    /// Hosts remote attachments/media may be downloaded from. Empty means none: an operator-typed
    /// URL is otherwise a server-side request forgery primitive into the internal network.
    /// </summary>
    public string[] AllowedRemoteHosts { get; set; } = [];

    public int RemoteFetchTimeoutSeconds { get; set; } = 15;
}

/// <summary>Cloudinary cloud storage configuration for public media assets.</summary>
public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";
    public string? CloudName { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(CloudName)
                             && !string.IsNullOrWhiteSpace(ApiKey)
                             && !string.IsNullOrWhiteSpace(ApiSecret);
}

/// <summary>A stored media file.</summary>
public sealed record StoredFile(string RelativeUrl, string FileName, string ContentType, long Length);

/// <summary>
/// The only component that touches the upload directories. Everything else passes references
/// (an <c>/uploads/...</c> path, an absolute URL on this host, or a private key) and gets back
/// bytes or a validated path — never a raw path it built itself, which is how a reference like
/// <c>/uploads/../../appsettings.json</c> used to reach the file system.
/// </summary>
public interface IFileStorage
{
    string PublicRoot { get; }
    string PrivateRoot { get; }

    /// <summary>Validates and stores a media file under the public uploads root.</summary>
    Task<StoredFile> SavePublicMediaAsync(Stream content, string originalFileName, string? declaredContentType, string subfolder, CancellationToken ct);

    /// <summary>Stores a file outside the web root. Returns its private key.</summary>
    Task<string> SavePrivateAsync(Stream content, string originalFileName, string subfolder, CancellationToken ct);

    /// <summary>Full path of a public upload reference, or null when it is outside the root or missing.</summary>
    string? ResolvePublicPath(string reference);

    /// <summary>Full path of a private key, or null when it is outside the root or missing.</summary>
    string? ResolvePrivatePath(string key);

    /// <summary>
    /// Bytes for an attachment/media reference: a local upload, or a remote URL on an allow-listed
    /// host. Null when the reference is not allowed, missing, or over <paramref name="maxBytes"/>.
    /// </summary>
    Task<byte[]?> ReadAsync(string reference, long maxBytes, CancellationToken ct);
}

/// <inheritdoc />
public sealed class FileStorage : IFileStorage
{
    public const string HttpClientName = "file-storage";

    private readonly IOptionsMonitor<StorageOptions> _options;
    private readonly IOptionsMonitor<CloudinaryOptions> _cloudinaryOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FileStorage> _logger;

    public FileStorage(
        IOptionsMonitor<StorageOptions> options,
        IOptionsMonitor<CloudinaryOptions> cloudinaryOptions,
        IHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        ILogger<FileStorage> logger)
    {
        _options = options;
        _cloudinaryOptions = cloudinaryOptions;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        PublicRoot = ResolveRoot(options.CurrentValue.PublicUploadsPath, environment.ContentRootPath, Path.Combine("wwwroot", "uploads"));
        PrivateRoot = ResolveRoot(options.CurrentValue.PrivatePath, environment.ContentRootPath, Path.Combine("App_Data", "private"));

        Directory.CreateDirectory(PublicRoot);
        Directory.CreateDirectory(PrivateRoot);
    }

    public string PublicRoot { get; }
    public string PrivateRoot { get; }

    public async Task<StoredFile> SavePublicMediaAsync(
        Stream content, string originalFileName, string? declaredContentType, string subfolder, CancellationToken ct)
    {
        var safeName = SanitizeFileName(originalFileName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();

        if (!FileSignatures.AllowedMedia.TryGetValue(extension, out var contentType))
        {
            throw new InvalidDataException(
                $"Files of type '{extension}' cannot be uploaded. Allowed: {string.Join(", ", FileSignatures.AllowedMedia.Keys)}.");
        }

        var max = _options.CurrentValue.MaxMediaBytes;
        using var memory = new MemoryStream();
        var written = await CopyWithLimitAsync(content, memory, max, ct);

        if (written == 0) throw new InvalidDataException("The file is empty.");

        // The extension is what the client claims; the first bytes are what the file is. An
        // HTML page renamed to .png must not end up served from our origin.
        memory.Position = 0;
        var header = new byte[64];
        var read = await memory.ReadAsync(header, ct);
        if (!FileSignatures.Matches(extension, header.AsSpan(0, read)))
        {
            throw new InvalidDataException($"The file's content does not match its '{extension}' extension.");
        }

        memory.Position = 0;

        // Upload to Cloudinary when configured (preserves media across container restarts)
        if (_cloudinaryOptions.CurrentValue.IsConfigured)
        {
            try
            {
                var cloudUrl = await UploadToCloudinaryAsync(memory, safeName, subfolder, ct);
                if (!string.IsNullOrWhiteSpace(cloudUrl))
                {
                    _logger.LogInformation("Successfully uploaded {FileName} to Cloudinary: {Url}", safeName, cloudUrl);
                    return new StoredFile(cloudUrl, safeName, contentType, written);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload {FileName} to Cloudinary. Falling back to local storage.", safeName);
            }
        }

        // Local storage fallback
        memory.Position = 0;
        var directory = EnsureInside(PublicRoot, subfolder);
        Directory.CreateDirectory(directory);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(directory, storedName);

        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await memory.CopyToAsync(output, ct);
        }

        var relative = Path.GetRelativePath(PublicRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return new StoredFile($"/uploads/{relative}", safeName, contentType, written);
    }

    private async Task<string?> UploadToCloudinaryAsync(Stream stream, string fileName, string subfolder, CancellationToken ct)
    {
        var cloudName = _cloudinaryOptions.CurrentValue.CloudName?.Trim();
        var apiKey = _cloudinaryOptions.CurrentValue.ApiKey?.Trim();
        var apiSecret = _cloudinaryOptions.CurrentValue.ApiSecret?.Trim();

        if (string.IsNullOrWhiteSpace(cloudName) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            return null;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var folder = string.IsNullOrWhiteSpace(subfolder) ? "waba_connect" : $"waba_connect/{subfolder}".TrimEnd('/');

        // Cloudinary requires signature parameters sorted alphabetically
        var toSign = $"folder={folder}&timestamp={timestamp}{apiSecret}";
        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(toSign));
        var signature = Convert.ToHexString(hashBytes).ToLowerInvariant();

        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(stream);
        form.Add(streamContent, "file", fileName);
        form.Add(new StringContent(apiKey), "api_key");
        form.Add(new StringContent(timestamp.ToString()), "timestamp");
        form.Add(new StringContent(folder), "folder");
        form.Add(new StringContent(signature), "signature");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var uploadEndpoint = $"https://api.cloudinary.com/v1_1/{cloudName}/auto/upload";

        using var response = await client.PostAsync(uploadEndpoint, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Cloudinary upload failed with status {StatusCode}: {Error}", response.StatusCode, err);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("secure_url", out var secureUrlProp))
        {
            return secureUrlProp.GetString();
        }

        return null;
    }

    public async Task<string> SavePrivateAsync(Stream content, string originalFileName, string subfolder, CancellationToken ct)
    {
        var safeName = SanitizeFileName(originalFileName);
        var directory = EnsureInside(PrivateRoot, subfolder);
        Directory.CreateDirectory(directory);

        var storedName = $"{Guid.NewGuid():N}_{safeName}";
        var path = Path.Combine(directory, storedName);

        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(output, ct);
        }

        return Path.GetRelativePath(PrivateRoot, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    public string? ResolvePublicPath(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        var value = reference.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            value = uri.AbsolutePath;
        }

        value = Uri.UnescapeDataString(value);
        var marker = value.IndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;

        var relative = value[(marker + "/uploads/".Length)..];
        return ResolveInside(PublicRoot, relative);
    }

    public string? ResolvePrivatePath(string key) =>
        string.IsNullOrWhiteSpace(key) ? null : ResolveInside(PrivateRoot, Uri.UnescapeDataString(key.Trim()));

    public async Task<byte[]?> ReadAsync(string reference, long maxBytes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        var local = ResolvePublicPath(reference);
        if (local is not null)
        {
            var info = new FileInfo(local);
            if (info.Length > maxBytes)
            {
                _logger.LogWarning("Attachment {File} is {Size} bytes, over the {Max}-byte limit.", info.Name, info.Length, maxBytes);
                return null;
            }

            return await File.ReadAllBytesAsync(local, ct);
        }

        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (!await IsAllowedRemoteAsync(uri, ct))
        {
            _logger.LogWarning("Refused to fetch attachment from {Host}: not in Storage:AllowedRemoteHosts.", uri.Host);
            return null;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(_options.CurrentValue.RemoteFetchTimeoutSeconds);

        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength is { } length && length > maxBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        try
        {
            await CopyWithLimitAsync(stream, buffer, maxBytes, ct);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Allow-listed host AND not resolving to a private, loopback or link-local address — the
    /// second check stops an allow-listed name that has been re-pointed at the metadata service.
    /// </summary>
    private async Task<bool> IsAllowedRemoteAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Host.EndsWith("cloudinary.com", StringComparison.OrdinalIgnoreCase)) return true;

        var allowed = _options.CurrentValue.AllowedRemoteHosts;
        if (allowed.Length == 0) return false;

        var hostAllowed = allowed.Any(h =>
            uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
            || (h.StartsWith("*.", StringComparison.Ordinal) && uri.Host.EndsWith(h[1..], StringComparison.OrdinalIgnoreCase)));
        if (!hostAllowed) return false;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            return addresses.Length > 0 && addresses.All(a => !IsPrivateAddress(a));
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || b[0] == 127
            || b[0] == 0;
    }

    private static string ResolveRoot(string? configured, string contentRoot, string fallback)
    {
        var path = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
    }

    private static string? ResolveInside(string root, string relative)
    {
        var candidate = ResolveUnder(root, relative);
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveUnder(string root, string relative)
    {
        var cleaned = relative.Replace('\\', '/').TrimStart('/');
        if (cleaned.Length == 0) return null;

        var full = Path.GetFullPath(Path.Combine(root, cleaned.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string EnsureInside(string root, string subfolder)
    {
        if (string.IsNullOrWhiteSpace(subfolder)) return root;
        return ResolveUnder(root, subfolder + "/x") is { } probe
            ? Path.GetDirectoryName(probe)!
            : throw new InvalidOperationException($"Invalid storage folder '{subfolder}'.");
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        name = name.Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(name) || name.Trim('.').Length == 0) name = "file";
        return name.Length > 120 ? name[^120..] : name;
    }

    private static async Task<long> CopyWithLimitAsync(Stream input, Stream output, long max, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max) throw new InvalidDataException($"The file is larger than the {max / (1024 * 1024)} MB limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    private void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not remove rejected upload {Path}.", path); }
    }
}

/// <summary>Media types accepted for upload and how to recognise each from its first bytes.</summary>
public static class FileSignatures
{
    /// <summary>
    /// Extension → content type. Deliberately excludes anything a browser would execute when
    /// served from our origin: .html, .svg, .js, .xml and friends.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedMedia = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".mp4"] = "video/mp4",
        [".3gp"] = "video/3gpp",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".aac"] = "audio/aac",
        [".amr"] = "audio/amr",
        [".m4a"] = "audio/mp4",
        [".zip"] = "application/zip"
    };

    public static bool Matches(string extension, ReadOnlySpan<byte> header)
    {
        static bool StartsWith(ReadOnlySpan<byte> data, params byte[] sig) => data.Length >= sig.Length && data[..sig.Length].SequenceEqual(sig);

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => StartsWith(header, 0xFF, 0xD8, 0xFF),
            ".png" => StartsWith(header, 0x89, 0x50, 0x4E, 0x47),
            ".gif" => StartsWith(header, (byte)'G', (byte)'I', (byte)'F', (byte)'8'),
            ".webp" => header.Length >= 12 && StartsWith(header, (byte)'R', (byte)'I', (byte)'F', (byte)'F')
                       && header[8..12].SequenceEqual("WEBP"u8),
            ".pdf" => StartsWith(header, (byte)'%', (byte)'P', (byte)'D', (byte)'F'),
            ".docx" or ".xlsx" or ".pptx" or ".zip" => StartsWith(header, 0x50, 0x4B, 0x03, 0x04),
            ".doc" or ".xls" or ".ppt" => StartsWith(header, 0xD0, 0xCF, 0x11, 0xE0),
            ".mp4" or ".3gp" or ".m4a" => header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8),
            ".mp3" => StartsWith(header, (byte)'I', (byte)'D', (byte)'3') || StartsWith(header, 0xFF, 0xFB)
                      || StartsWith(header, 0xFF, 0xF3) || StartsWith(header, 0xFF, 0xF2),
            ".ogg" => StartsWith(header, (byte)'O', (byte)'g', (byte)'g', (byte)'S'),
            ".aac" => StartsWith(header, 0xFF, 0xF1) || StartsWith(header, 0xFF, 0xF9),
            ".amr" => StartsWith(header, (byte)'#', (byte)'!', (byte)'A', (byte)'M', (byte)'R'),
            ".txt" or ".csv" => header.IndexOf((byte)0) < 0 && !LooksLikeMarkup(header),
            _ => false
        };
    }

    /// <summary>A "text" file that opens with a tag is an HTML payload wearing a .txt extension.</summary>
    private static bool LooksLikeMarkup(ReadOnlySpan<byte> header)
    {
        var text = System.Text.Encoding.UTF8.GetString(header).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith('<');
    }
}
