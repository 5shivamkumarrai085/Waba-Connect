using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;

namespace WhatsAppCampaignApi.Services.WhatsApp.Flows;

/// <summary>An HTTP answer for Meta: status, body and content type.</summary>
public sealed record FlowEndpointResponse(int StatusCode, string? Body, string ContentType = "text/plain");

public interface IWhatsAppFlowEndpoint
{
    /// <summary>The real endpoint: verify, decrypt, dispatch, encrypt.</summary>
    Task<FlowEndpointResponse> HandleEncryptedAsync(string endpointKey, byte[] rawBody, string? signatureHeader, CancellationToken ct);

    /// <summary>
    /// The same dispatch with encryption skipped — the development harness, so each screen can be
    /// exercised from Postman. The controller exposes it only in Development.
    /// </summary>
    Task<(int StatusCode, JsonObject? Body)> HandlePlaintextAsync(string endpointKey, JsonObject request, CancellationToken ct);
}

/// <summary>
/// Meta's WhatsApp Flows data-exchange endpoint (data_api_version 3.0).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Signature: <c>X-Hub-Signature-256</c> is checked first, against the app secrets with the
/// same verifier as the messages webhook, so unsigned traffic costs no database work. Anything but
/// a valid signature is refused with 432 outside Development (staging is public too, and the
/// encryption key is public by design).</item>
/// <item>Any unexpected failure answers an empty-bodied 500: Meta can only read encrypted bodies,
/// and the generic JSON error the rest of the API returns showed customers "could not decrypt".</item>
/// <item>421 when the body cannot be decrypted: WhatsApp then re-downloads our public key and retries.</item>
/// <item><c>ping</c> (Meta's health check) answers <c>{"data":{"status":"active"}}</c>; an error
/// notification answers <c>{"data":{"acknowledged":true}}</c>.</item>
/// <item>Everything else goes to the <see cref="IFlowScreenHandler"/> whose prefix the flow token carries.</item>
/// </list>
/// </remarks>
public sealed class WhatsAppFlowEndpoint : IWhatsAppFlowEndpoint
{
    public const int DecryptionFailed = 421;
    public const int SignatureFailed = 432;

    /// <summary>How long a connection's registration and private key are reused before re-reading them.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IWhatsAppWebhookSignatureVerifier _signatures;
    private readonly IEnumerable<IFlowScreenHandler> _handlers;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<WhatsAppFlowEndpoint> _logger;

    public WhatsAppFlowEndpoint(
        AppDbContext db,
        IMemoryCache cache,
        IWhatsAppWebhookSignatureVerifier signatures,
        IEnumerable<IFlowScreenHandler> handlers,
        IWebHostEnvironment environment,
        ILogger<WhatsAppFlowEndpoint> logger)
    {
        _db = db;
        _cache = cache;
        _signatures = signatures;
        _handlers = handlers;
        _environment = environment;
        _logger = logger;
    }

    public async Task<FlowEndpointResponse> HandleEncryptedAsync(string endpointKey, byte[] rawBody, string? signatureHeader, CancellationToken ct)
    {
        if (!await SignatureAcceptableAsync(rawBody, signatureHeader, ct)) return new(SignatureFailed, null);

        var registration = await RegistrationAsync(endpointKey, ct);
        if (registration is null) return new(StatusCodes.Status404NotFound, null);

        var privateKeyPem = await PrivateKeyAsync(registration.ConnectionId, ct);
        if (privateKeyPem is null)
        {
            _logger.LogWarning("Flow request for connection {ConnectionId} arrived, but it has no Flows key.", registration.ConnectionId);
            return new(DecryptionFailed, null);
        }

        string? encryptedData, encryptedKey, iv;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            encryptedData = doc.RootElement.GetProperty("encrypted_flow_data").GetString();
            encryptedKey = doc.RootElement.GetProperty("encrypted_aes_key").GetString();
            iv = doc.RootElement.GetProperty("initial_vector").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new(StatusCodes.Status400BadRequest, null);
        }
        if (encryptedData is null || encryptedKey is null || iv is null) return new(StatusCodes.Status400BadRequest, null);

        var decrypted = TryDecrypt(privateKeyPem, encryptedData, encryptedKey, iv, out var decryptError);
        if (decrypted is null)
        {
            // The cached key may be one that was just replaced: read it again once before failing.
            _cache.Remove(KeyCacheKey(registration.ConnectionId));
            var fresh = await PrivateKeyAsync(registration.ConnectionId, ct);
            if (fresh is not null && fresh != privateKeyPem) decrypted = TryDecrypt(fresh, encryptedData, encryptedKey, iv, out decryptError);
        }
        if (decrypted is null)
        {
            _logger.LogWarning("Could not decrypt a Flow request for connection {ConnectionId}: {Error}", registration.ConnectionId, decryptError);
            return new(DecryptionFailed, null);
        }

        JsonObject? request;
        try
        {
            request = JsonNode.Parse(decrypted.Json) as JsonObject;
        }
        catch (JsonException)
        {
            request = null;
        }
        if (request is null) return new(StatusCodes.Status400BadRequest, null);

        var (status, body) = await DispatchAsync(registration.Id, registration.ConnectionId, request, ct);
        if (body is null) return new(status, null);

        return new(status, FlowCrypto.EncryptResponse(body.ToJsonString(), decrypted.AesKey, decrypted.Iv));
    }

    public async Task<(int StatusCode, JsonObject? Body)> HandlePlaintextAsync(string endpointKey, JsonObject request, CancellationToken ct)
    {
        var registration = await RegistrationAsync(endpointKey, ct);
        if (registration is null) return (StatusCodes.Status404NotFound, null);
        return await DispatchAsync(registration.Id, registration.ConnectionId, request, ct);
    }

    private static string KeyCacheKey(int connectionId) => $"wa-flow-key:{connectionId}";

    private sealed record RegistrationRef(int Id, int ConnectionId);

    private async Task<RegistrationRef?> RegistrationAsync(string endpointKey, CancellationToken ct)
    {
        var cacheKey = $"wa-flow-endpoint:{endpointKey}";
        if (_cache.TryGetValue(cacheKey, out RegistrationRef? cached)) return cached;

        var found = await _db.WhatsAppFlowRegistrations.AsNoTracking()
            .Where(r => r.EndpointKey == endpointKey)
            .Select(r => new RegistrationRef(r.Id, r.ConnectionId))
            .FirstOrDefaultAsync(ct);
        // Misses are cached briefly too, so random endpoint keys cannot each cost a query.
        _cache.Set(cacheKey, found, found is null ? TimeSpan.FromSeconds(10) : CacheFor);
        return found;
    }

    /// <summary>The connection's private key (decrypted from storage), cached so each screen does not re-read and decrypt it.</summary>
    private async Task<string?> PrivateKeyAsync(int connectionId, CancellationToken ct)
    {
        if (_cache.TryGetValue(KeyCacheKey(connectionId), out string? cached)) return cached;
        var pem = await _db.WhatsAppFlowEncryptionKeys.AsNoTracking()
            .Where(k => k.ConnectionId == connectionId).Select(k => k.PrivateKeyPem).FirstOrDefaultAsync(ct);
        if (pem is not null) _cache.Set(KeyCacheKey(connectionId), pem, CacheFor);
        return pem;
    }

    private static FlowCrypto.DecryptedRequest? TryDecrypt(string pem, string data, string key, string iv, out string? error)
    {
        try
        {
            using var rsa = FlowCrypto.LoadKey(pem);
            error = null;
            return FlowCrypto.DecryptRequest(data, key, iv, rsa);
        }
        catch (Exception ex) when (ex is FlowCrypto.FlowDecryptionException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }

    private async Task<(int StatusCode, JsonObject? Body)> DispatchAsync(int registrationId, int connectionId, JsonObject request, CancellationToken ct)
    {
        var action = request["action"]?.GetValue<string>() ?? string.Empty;
        var data = request["data"] as JsonObject;

        if (action.Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            await _db.WhatsAppFlowRegistrations.Where(r => r.Id == registrationId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.LastPingAt, DateTime.UtcNow), ct);
            return (200, new JsonObject { ["data"] = new JsonObject { ["status"] = "active" } });
        }

        // WhatsApp reporting that our previous response broke the Flow on the device.
        if (data is not null && data.ContainsKey("error"))
        {
            _logger.LogWarning("WhatsApp reported a Flow error on connection {ConnectionId}: {Error} {Message}",
                connectionId, data["error"]?.ToJsonString(), data["error_message"]?.ToJsonString());
            return (200, new JsonObject { ["data"] = new JsonObject { ["acknowledged"] = true } });
        }

        var token = request["flow_token"]?.GetValue<string>() ?? string.Empty;
        var separator = token.IndexOf('_');
        var prefix = separator > 0 ? token[..separator] : string.Empty;
        var handler = _handlers.FirstOrDefault(h => h.TokenPrefix == prefix);
        if (handler is null) return (FlowHandlerResult.InvalidFlowToken, null);

        var screen = request["screen"]?.GetValue<string>();
        // Shape only — field names, never values (codes, account numbers) — to diagnose what a
        // client actually sent when a screen misbehaves.
        _logger.LogInformation("Flow request on connection {ConnectionId}: action {Action}, screen {Screen}, op {Op}, data fields [{Fields}]",
            connectionId, action, screen ?? "-", data?["op"]?.ToJsonString() ?? "-", data is null ? "" : string.Join(", ", data.Select(p => p.Key)));

        FlowHandlerResult result;
        try
        {
            result = await handler.HandleAsync(new FlowRequest(action, screen, data, token, connectionId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // An admin removing a corridor mid-session, a database timeout, a provider throwing:
            // the customer sees WhatsApp's own "something went wrong" and can try again, instead of
            // an unreadable body. Logged in full here because the customer sees nothing useful.
            _logger.LogError(ex, "Flow request on connection {ConnectionId} failed (action {Action}, screen {Screen}).", connectionId, action, screen ?? "-");
            return (StatusCodes.Status500InternalServerError, null);
        }
        _logger.LogInformation("Flow response on connection {ConnectionId}: HTTP {Status}, screen {Screen}", connectionId, result.StatusCode, result.Body?["screen"]?.GetValue<string>() ?? "-");
        return (result.StatusCode, result.Body);
    }

    private async Task<bool> SignatureAcceptableAsync(byte[] body, string? header, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            if (!_environment.IsDevelopment())
            {
                _logger.LogWarning("Refused a Flow request without X-Hub-Signature-256.");
                return false;
            }
            _logger.LogWarning("Flow request accepted WITHOUT a signature (allowed in Development only).");
            return true;
        }

        var result = await _signatures.VerifyAsync(body, header, ct);
        switch (result)
        {
            case WebhookSignatureResult.Valid:
                return true;
            case WebhookSignatureResult.NotConfigured when _environment.IsDevelopment():
                _logger.LogWarning("Flow request accepted WITHOUT signature verification: no app secret is configured.");
                return true;
            default:
                _logger.LogWarning("Refused a Flow request with an invalid X-Hub-Signature-256 ({Result}).", result);
                return false;
        }
    }
}
