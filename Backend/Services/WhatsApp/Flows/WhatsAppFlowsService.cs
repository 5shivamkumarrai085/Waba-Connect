using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Services.WhatsApp.Flows;

public sealed record FlowKeyStatusDto(bool Exists, DateTime? CreatedAt, DateTime? UploadedAt, string? SignatureStatus, string? LastError);

public sealed record FlowRegistrationDto(
    string Purpose,
    string? MetaFlowId,
    string Name,
    string? Status,
    string? JsonVersion,
    JsonNode? ValidationErrors,
    string EndpointUrl,
    string? EndpointUri,
    DateTime? LastSyncedAt,
    DateTime? PublishedAt,
    DateTime? LastPingAt,
    string? LastError);

/// <param name="PublicAddressProblem">Why App:PublicBaseUrl cannot be given to Meta, or null when it looks usable.</param>
public sealed record WhatsAppFlowsStatusDto(
    int ConnectionId,
    string PublicBaseUrl,
    string? PublicAddressProblem,
    bool AllowDraftSend,
    FlowKeyStatusDto Key,
    FlowRegistrationDto? SendMoney);

public interface IWhatsAppFlowsService
{
    Task<WhatsAppFlowsStatusDto> GetStatusAsync(int connectionId, CancellationToken ct);

    /// <summary>Creates the connection's RSA key pair if needed (or a new one) and uploads the public key to each of its numbers.</summary>
    Task<WhatsAppFlowsStatusDto> EnsureKeyAsync(int connectionId, bool regenerate, CancellationToken ct);

    /// <summary>Creates or updates the Send Money Flow on Meta: screens, endpoint URL, app link.</summary>
    Task<WhatsAppFlowsStatusDto> SyncSendMoneyAsync(int connectionId, bool createNew, CancellationToken ct);

    Task<WhatsAppFlowsStatusDto> PublishSendMoneyAsync(int connectionId, CancellationToken ct);

    /// <summary>
    /// Uses a Flow built by hand in WhatsApp Manager as this connection's Send Money Flow: checks the
    /// connection's WABA owns it, records it, and points a draft at our data endpoint.
    /// </summary>
    Task<WhatsAppFlowsStatusDto> LinkSendMoneyAsync(int connectionId, string metaFlowId, CancellationToken ct);

    /// <summary>The Flow JSON exactly as it is uploaded (the version from configuration applied).</summary>
    string SendMoneyFlowJson();
}

/// <summary>Per-connection WhatsApp Flows set-up: key pair, Flow registration, publishing.</summary>
public sealed class WhatsAppFlowsService : IWhatsAppFlowsService
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppFlowsApiClient _graph;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly ILogger<WhatsAppFlowsService> _logger;

    public WhatsAppFlowsService(
        AppDbContext db,
        IWhatsAppFlowsApiClient graph,
        IConfiguration configuration,
        IOptionsMonitor<RemittanceOptions> options,
        ILogger<WhatsAppFlowsService> logger)
    {
        _db = db;
        _graph = graph;
        _configuration = configuration;
        _options = options;
        _logger = logger;
    }

    public async Task<WhatsAppFlowsStatusDto> GetStatusAsync(int connectionId, CancellationToken ct)
    {
        await EnsureConnectionAsync(connectionId, ct);
        var key = await _db.WhatsAppFlowEncryptionKeys.AsNoTracking().FirstOrDefaultAsync(k => k.ConnectionId == connectionId, ct);
        var registration = await _db.WhatsAppFlowRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.Purpose == WhatsAppFlowRegistration.SendMoneyPurpose, ct);

        var baseUrl = PublicBaseUrl();
        return new WhatsAppFlowsStatusDto(
            connectionId,
            baseUrl,
            PublicEndpointProbe.Classify(baseUrl) is { } reason ? PublicEndpointCatalogDescribe(reason) : null,
            _options.CurrentValue.Flows.AllowDraftSend,
            new FlowKeyStatusDto(key is not null, key?.CreatedAt, key?.UploadedAt, key?.SignatureStatus, key?.LastError),
            registration is null ? null : ToDto(registration, baseUrl));
    }

    public async Task<WhatsAppFlowsStatusDto> EnsureKeyAsync(int connectionId, bool regenerate, CancellationToken ct)
    {
        var (accessToken, _) = await CredentialsAsync(connectionId, ct);
        var key = await _db.WhatsAppFlowEncryptionKeys.FirstOrDefaultAsync(k => k.ConnectionId == connectionId, ct);

        if (key is null || regenerate)
        {
            var pair = FlowCrypto.GenerateKeyPair();
            if (key is null)
            {
                key = new WhatsAppFlowEncryptionKey { ConnectionId = connectionId };
                _db.WhatsAppFlowEncryptionKeys.Add(key);
            }
            key.PublicKeyPem = pair.PublicKeyPem;
            key.PrivateKeyPem = pair.PrivateKeyPem;
            key.UploadedAt = null;
            key.SignatureStatus = null;
            await _db.SaveChangesAsync(ct);
        }

        var phones = await _db.WabaPhoneNumbers.AsNoTracking()
            .Where(p => p.ConnectionId == connectionId && p.PhoneNumberId != null && p.PhoneNumberId != "")
            .Select(p => p.PhoneNumberId).ToListAsync(ct);
        if (phones.Count == 0) throw new InvalidOperationException("This connection has no WhatsApp phone number yet. Sync its number first.");

        var errors = new List<string>();
        string? signature = null;
        foreach (var phoneId in phones)
        {
            var upload = await _graph.UploadPublicKeyAsync(accessToken, phoneId, key.PublicKeyPem, ct);
            if (!upload.Success)
            {
                errors.Add($"{phoneId}: {upload.Error}");
                continue;
            }
            var check = await _graph.GetPublicKeyAsync(accessToken, phoneId, ct);
            signature = check.Body?["data"] is JsonArray { Count: > 0 } rows
                ? rows[0]?["business_public_key_signature_status"]?.GetValue<string>()
                : check.Body?["business_public_key_signature_status"]?.GetValue<string>() ?? signature;
        }

        key.UploadedAt = errors.Count < phones.Count ? DateTime.UtcNow : key.UploadedAt;
        key.SignatureStatus = signature ?? key.SignatureStatus;
        key.LastError = errors.Count == 0 ? null : Truncate(string.Join("; ", errors), 500);
        await _db.SaveChangesAsync(ct);

        return await GetStatusAsync(connectionId, ct);
    }

    public async Task<WhatsAppFlowsStatusDto> SyncSendMoneyAsync(int connectionId, bool createNew, CancellationToken ct)
    {
        var (accessToken, wabaId) = await CredentialsAsync(connectionId, ct);

        var baseUrl = PublicBaseUrl();
        if (PublicEndpointProbe.Classify(baseUrl) is { } reason)
        {
            throw new ArgumentException(
                $"App:PublicBaseUrl ({(string.IsNullOrEmpty(baseUrl) ? "not set" : baseUrl)}) can't be used: {PublicEndpointCatalogDescribe(reason)} "
                + "Meta must reach the Flow endpoint over public HTTPS — start ngrok and set App:PublicBaseUrl to its https address, then restart.");
        }

        var registration = await SendMoneyRegistrationAsync(connectionId, ct);

        var endpointUri = EndpointUrl(baseUrl, registration.EndpointKey);
        var flowJson = SendMoneyFlowJson();
        registration.JsonVersion = _options.CurrentValue.Flows.JsonVersion;
        registration.LastError = null;

        if (createNew || registration.MetaFlowId is null || registration.Status is "DEPRECATED")
        {
            // Flow names must be unique on the account; a timestamp keeps re-creations apart.
            var name = $"Send Money {DateTime.UtcNow:yyyyMMdd-HHmm}";
            var created = await _graph.CreateFlowAsync(accessToken, wabaId, name, flowJson, endpointUri, ct);
            if (!created.Success)
            {
                registration.LastError = Truncate($"Create failed: {created.Error}", 500);
                await _db.SaveChangesAsync(ct);
                throw new InvalidOperationException($"Meta refused to create the Flow: {created.Error}");
            }
            registration.MetaFlowId = created.Body?["id"]?.GetValue<string>();
            registration.Name = name;
            registration.ValidationErrorsJson = created.ValidationErrors?.ToJsonString();
            registration.PublishedAt = null;
        }
        else if (registration.Status == "PUBLISHED")
        {
            throw new InvalidOperationException("A published Flow can't be changed. Use \"Create new version\" to make a fresh draft.");
        }
        else
        {
            var upload = await _graph.UploadFlowJsonAsync(accessToken, registration.MetaFlowId, flowJson, ct);
            registration.ValidationErrorsJson = upload.ValidationErrors?.ToJsonString();
            if (!upload.Success) registration.LastError = Truncate($"Upload failed: {upload.Error}", 500);

            var metadata = await _graph.UpdateMetadataAsync(accessToken, registration.MetaFlowId,
                new Dictionary<string, string> { ["name"] = registration.Name, ["endpoint_uri"] = endpointUri }, ct);
            if (!metadata.Success) registration.LastError = Truncate($"Endpoint update failed: {metadata.Error}", 500);
        }

        // Link the Meta app whose secret signs the requests. Best effort: not every API version accepts it.
        var appId = await _db.WabaConfigurations.AsNoTracking().Where(c => c.ConnectionId == connectionId).Select(c => c.FacebookAppId).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(appId) && registration.MetaFlowId is not null)
        {
            var link = await _graph.UpdateMetadataAsync(accessToken, registration.MetaFlowId,
                new Dictionary<string, string> { ["name"] = registration.Name, ["application_id"] = appId }, ct);
            if (!link.Success) _logger.LogInformation("Linking app {AppId} to Flow {FlowId} was not accepted: {Error}", appId, registration.MetaFlowId, link.Error);
        }

        registration.EndpointUri = endpointUri;
        registration.LastSyncedAt = DateTime.UtcNow;
        await RefreshAsync(registration, accessToken, ct);
        await _db.SaveChangesAsync(ct);

        return await GetStatusAsync(connectionId, ct);
    }

    public async Task<WhatsAppFlowsStatusDto> PublishSendMoneyAsync(int connectionId, CancellationToken ct)
    {
        var (accessToken, _) = await CredentialsAsync(connectionId, ct);
        var registration = await _db.WhatsAppFlowRegistrations
            .FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.Purpose == WhatsAppFlowRegistration.SendMoneyPurpose, ct)
            ?? throw new InvalidOperationException("Create the Send Money Flow first.");
        if (registration.MetaFlowId is null) throw new InvalidOperationException("Create the Send Money Flow first.");

        var result = await _graph.PublishAsync(accessToken, registration.MetaFlowId, ct);
        if (!result.Success)
        {
            registration.LastError = Truncate($"Publish failed: {result.Error}", 500);
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException($"Meta refused to publish the Flow: {result.Error}");
        }

        registration.PublishedAt = DateTime.UtcNow;
        await RefreshAsync(registration, accessToken, ct);
        await _db.SaveChangesAsync(ct);
        return await GetStatusAsync(connectionId, ct);
    }

    public async Task<WhatsAppFlowsStatusDto> LinkSendMoneyAsync(int connectionId, string metaFlowId, CancellationToken ct)
    {
        metaFlowId = (metaFlowId ?? string.Empty).Trim();
        if (metaFlowId.Length is 0 or > 40 || !metaFlowId.All(char.IsAsciiDigit))
            throw new ArgumentException("Enter the Flow ID shown in WhatsApp Manager (digits only).");

        var (accessToken, wabaId) = await CredentialsAsync(connectionId, ct);

        var (owned, lookupError) = await _graph.WabaHasFlowAsync(accessToken, wabaId, metaFlowId, ct);
        if (lookupError is not null) throw new InvalidOperationException($"WhatsApp's Flow list could not be read: {lookupError}");
        if (!owned)
            throw new InvalidOperationException(
                $"Flow {metaFlowId} is not on this connection's WhatsApp Business Account ({wabaId}). Create it under that account in WhatsApp Manager, or link it on the connection it belongs to.");

        var flow = await _graph.GetFlowAsync(accessToken, metaFlowId, ct);
        if (!flow.Success) throw new InvalidOperationException($"WhatsApp did not return Flow {metaFlowId}: {flow.Error}");

        var registration = await SendMoneyRegistrationAsync(connectionId, ct);
        if (registration.MetaFlowId != metaFlowId)
        {
            registration.LastPingAt = null;
            registration.PublishedAt = null;
        }

        var status = flow.Body?["status"]?.GetValue<string>();
        registration.MetaFlowId = metaFlowId;
        registration.Name = Truncate(flow.Body?["name"]?.GetValue<string>() ?? registration.Name, 60);
        registration.Status = status;
        registration.JsonVersion = flow.Body?["json_version"]?.GetValue<string>();
        registration.ValidationErrorsJson = flow.ValidationErrors?.ToJsonString();
        registration.EndpointUri = flow.Body?["endpoint_uri"]?.GetValue<string>();
        registration.LastError = null;
        if (status == "PUBLISHED") registration.PublishedAt ??= DateTime.UtcNow;

        // A draft can still be pointed at us; a published Flow keeps whatever endpoint it was published with.
        var baseUrl = PublicBaseUrl();
        var endpointUri = EndpointUrl(baseUrl, registration.EndpointKey);
        if (status is not ("PUBLISHED" or "DEPRECATED") && registration.EndpointUri != endpointUri)
        {
            if (PublicEndpointProbe.Classify(baseUrl) is { } reason)
            {
                registration.LastError = Truncate($"Endpoint not set: App:PublicBaseUrl can't be used ({PublicEndpointCatalogDescribe(reason)}).", 500);
            }
            else
            {
                var fields = new Dictionary<string, string> { ["name"] = registration.Name, ["endpoint_uri"] = endpointUri };
                var update = await _graph.UpdateMetadataAsync(accessToken, metaFlowId, fields, ct);
                if (update.Success) registration.EndpointUri = endpointUri;
                else registration.LastError = Truncate($"Endpoint update failed: {update.Error}", 500);
            }
        }

        registration.LastSyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Connection {ConnectionId} now uses Flow {FlowId} ({Status}) for Send Money.", connectionId, metaFlowId, status);
        return await GetStatusAsync(connectionId, ct);
    }

    public string SendMoneyFlowJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WhatsAppFlows.send_money.flow.json")
            ?? throw new InvalidOperationException("The Send Money Flow definition is missing from the build.");
        var json = JsonNode.Parse(stream) as JsonObject ?? throw new InvalidOperationException("The Send Money Flow definition is not a JSON object.");
        json["version"] = _options.CurrentValue.Flows.JsonVersion;
        return json.ToJsonString();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private async Task RefreshAsync(WhatsAppFlowRegistration registration, string accessToken, CancellationToken ct)
    {
        if (registration.MetaFlowId is null) return;
        var flow = await _graph.GetFlowAsync(accessToken, registration.MetaFlowId, ct);
        if (!flow.Success) return;
        registration.Status = flow.Body?["status"]?.GetValue<string>() ?? registration.Status;
        registration.ValidationErrorsJson = flow.ValidationErrors?.ToJsonString();
    }

    /// <summary>The connection's Send Money registration, created with a fresh endpoint key the first time.</summary>
    private async Task<WhatsAppFlowRegistration> SendMoneyRegistrationAsync(int connectionId, CancellationToken ct)
    {
        var registration = await _db.WhatsAppFlowRegistrations
            .FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.Purpose == WhatsAppFlowRegistration.SendMoneyPurpose, ct);
        if (registration is not null) return registration;

        registration = new WhatsAppFlowRegistration
        {
            ConnectionId = connectionId,
            Purpose = WhatsAppFlowRegistration.SendMoneyPurpose,
            Name = "Send Money",
            EndpointKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
        };
        _db.WhatsAppFlowRegistrations.Add(registration);
        await _db.SaveChangesAsync(ct);
        return registration;
    }

    private async Task<(string AccessToken, string WabaId)> CredentialsAsync(int connectionId, CancellationToken ct)
    {
        await EnsureConnectionAsync(connectionId, ct);
        var config = await _db.WabaConfigurations.AsNoTracking()
            .Where(c => c.ConnectionId == connectionId)
            .OrderByDescending(c => c.Connected)
            .FirstOrDefaultAsync(ct);
        if (config is null || string.IsNullOrWhiteSpace(config.AccessToken) || string.IsNullOrWhiteSpace(config.WabaId))
        {
            throw new InvalidOperationException("Connect this WhatsApp account (access token and WABA id) before setting up Flows.");
        }
        return (config.AccessToken, config.WabaId);
    }

    private async Task EnsureConnectionAsync(int connectionId, CancellationToken ct)
    {
        if (!await _db.Connections.AsNoTracking().AnyAsync(c => c.Id == connectionId, ct))
            throw new KeyNotFoundException($"Connection {connectionId} was not found.");
    }

    private string PublicBaseUrl() => (_configuration["App:PublicBaseUrl"] ?? string.Empty).Trim().TrimEnd('/');

    public static string EndpointUrl(string baseUrl, string endpointKey) => $"{baseUrl}/api/webhook/whatsapp-flows/{endpointKey}";

    private static FlowRegistrationDto ToDto(WhatsAppFlowRegistration r, string baseUrl)
    {
        JsonNode? errors = null;
        try { errors = r.ValidationErrorsJson is null ? null : JsonNode.Parse(r.ValidationErrorsJson); } catch (System.Text.Json.JsonException) { }
        return new FlowRegistrationDto(r.Purpose, r.MetaFlowId, r.Name, r.Status, r.JsonVersion, errors,
            EndpointUrl(baseUrl, r.EndpointKey), r.EndpointUri, r.LastSyncedAt, r.PublishedAt, r.LastPingAt, r.LastError);
    }

    private static string PublicEndpointCatalogDescribe(string reason) => WhatsAppCampaignApi.Services.Catalogs.PublicEndpointCatalog.Describe(reason);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
