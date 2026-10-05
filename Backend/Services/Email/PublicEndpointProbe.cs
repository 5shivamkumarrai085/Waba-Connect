using System.Net;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Services.Catalogs;
using WhatsAppCampaignApi.Services.Integrations;

namespace WhatsAppCampaignApi.Services.Email;

/// <param name="Reachable">True only when the address answered from outside as this very server.</param>
/// <param name="Reason">One of the <see cref="PublicEndpointCatalog"/> reason codes.</param>
public sealed record PublicEndpointStatus(bool Reachable, string BaseUrl, string Reason)
{
    public string Description => PublicEndpointCatalog.Describe(Reason);
}

/// <summary>
/// Answers "will opens, clicks and unsubscribes from real recipients reach this server?".
///
/// Tracking pixels and links are built on <c>Email:Tracking:BaseUrl</c> (which defaults to
/// <c>App:PublicBaseUrl</c>). When that address is localhost, private, plain http or simply not
/// answering, every open and click is lost without any error — the request never arrives. This
/// probe makes that visible in the campaign pre-flight check and in <c>/health</c>.
/// </summary>
public interface IPublicEndpointProbe
{
    Task<PublicEndpointStatus> CheckAsync(CancellationToken ct = default);
}

public sealed class PublicEndpointProbe : IPublicEndpointProbe
{
    /// <summary>Identifies this process, so a probe that reaches a different server is not mistaken for success.</summary>
    public static readonly string InstanceId = Guid.NewGuid().ToString("N");

    private const string CacheKey = "public-endpoint-status";
    private static readonly TimeSpan FailureCacheTime = TimeSpan.FromSeconds(30);

    private readonly IConfiguration _configuration;
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PublicEndpointProbe> _logger;

    public PublicEndpointProbe(IConfiguration configuration, HttpClient http, IMemoryCache cache, ILogger<PublicEndpointProbe> logger)
    {
        _configuration = configuration;
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// The checks that need no network: returns the reason code when the address cannot work for
    /// recipients, or null when it might (only the live probe can then confirm it).
    /// </summary>
    public static string? Classify(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return PublicEndpointCatalog.Empty;
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)) return PublicEndpointCatalog.Unreachable;

        var host = uri.IdnHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return PublicEndpointCatalog.Localhost;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            if (IPAddress.IsLoopback(address)) return PublicEndpointCatalog.Localhost;
            if (WebhookUrlGuard.IsBlocked(address)) return PublicEndpointCatalog.PrivateNetwork;
        }
        if (uri.Scheme != Uri.UriSchemeHttps) return PublicEndpointCatalog.NotHttps;
        return null;
    }

    public async Task<PublicEndpointStatus> CheckAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out PublicEndpointStatus? cached) && cached is not null) return cached;

        var baseUrl = (_configuration["Email:Tracking:BaseUrl"] ?? string.Empty).TrimEnd('/');
        var status = Classify(baseUrl) is { } reason
            ? new PublicEndpointStatus(false, baseUrl, reason)
            : await ProbeAsync(baseUrl, ct);

        _cache.Set(CacheKey, status, status.Reachable ? TimeSpan.FromMinutes(PublicEndpointCatalog.ProbeCacheMinutes) : FailureCacheTime);
        return status;
    }

    private async Task<PublicEndpointStatus> ProbeAsync(string baseUrl, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(PublicEndpointCatalog.ProbeTimeoutSeconds));
        try
        {
            using var response = await _http.GetAsync($"{baseUrl}/api/t/ping/{nonce}", timeout.Token);
            var body = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(timeout.Token) : string.Empty;
            if (body == PingReply(nonce)) return new(true, baseUrl, PublicEndpointCatalog.Ok);
            if (body.StartsWith(nonce + ":", StringComparison.Ordinal)) return new(false, baseUrl, PublicEndpointCatalog.WrongInstance);
            _logger.LogWarning("Public address {BaseUrl} answered the reachability probe with HTTP {Status}", baseUrl, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning("Public address {BaseUrl} did not answer the reachability probe: {Error}", baseUrl, ex.Message);
        }
        return new(false, baseUrl, PublicEndpointCatalog.Unreachable);
    }

    /// <summary>What <c>GET api/t/ping/{nonce}</c> answers; the probe compares against it.</summary>
    public static string PingReply(string nonce) => $"{nonce}:{InstanceId}";
}
