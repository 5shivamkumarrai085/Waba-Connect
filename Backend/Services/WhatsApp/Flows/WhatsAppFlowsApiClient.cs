using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhatsAppCampaignApi.Services.WhatsApp.Flows;

/// <summary>A Graph API answer: success, the parsed body, and Meta's error message when it failed.</summary>
public sealed record GraphResult(bool Success, JsonObject? Body, string? Error)
{
    /// <summary>Meta's validation_errors array, when present and non-empty.</summary>
    public JsonArray? ValidationErrors => Body?["validation_errors"] is JsonArray { Count: > 0 } errors ? errors : null;
}

public interface IWhatsAppFlowsApiClient
{
    Task<GraphResult> CreateFlowAsync(string accessToken, string wabaId, string name, string flowJson, string? endpointUri, CancellationToken ct);
    Task<GraphResult> UploadFlowJsonAsync(string accessToken, string flowId, string flowJson, CancellationToken ct);
    Task<GraphResult> UpdateMetadataAsync(string accessToken, string flowId, IReadOnlyDictionary<string, string> fields, CancellationToken ct);
    Task<GraphResult> PublishAsync(string accessToken, string flowId, CancellationToken ct);
    Task<GraphResult> GetFlowAsync(string accessToken, string flowId, CancellationToken ct);
    Task<GraphResult> UploadPublicKeyAsync(string accessToken, string phoneNumberId, string publicKeyPem, CancellationToken ct);
    Task<GraphResult> GetPublicKeyAsync(string accessToken, string phoneNumberId, CancellationToken ct);

    /// <summary>Whether the WhatsApp Business Account owns the Flow; Error is set when Meta could not be asked.</summary>
    Task<(bool Found, string? Error)> WabaHasFlowAsync(string accessToken, string wabaId, string flowId, CancellationToken ct);
}

/// <summary>
/// The Flows endpoints of the Graph API, on the same version as the rest of the WhatsApp
/// integration (<c>WhatsApp:ApiVersion</c>). Never throws for an API failure; the result says why.
/// </summary>
public sealed class WhatsAppFlowsApiClient : IWhatsAppFlowsApiClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppFlowsApiClient> _logger;

    public WhatsAppFlowsApiClient(HttpClient http, IConfiguration configuration, ILogger<WhatsAppFlowsApiClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    private string BaseUrl => $"https://graph.facebook.com/{_configuration["WhatsApp:ApiVersion"] ?? "v21.0"}";

    public Task<GraphResult> CreateFlowAsync(string accessToken, string wabaId, string name, string flowJson, string? endpointUri, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["name"] = name,
            ["categories"] = "[\"OTHER\"]",
            ["flow_json"] = flowJson
        };
        if (!string.IsNullOrWhiteSpace(endpointUri)) fields["endpoint_uri"] = endpointUri;
        return SendAsync(HttpMethod.Post, $"{BaseUrl}/{wabaId}/flows", accessToken, new FormUrlEncodedContent(fields), ct);
    }

    public Task<GraphResult> UploadFlowJsonAsync(string accessToken, string flowId, string flowJson, CancellationToken ct)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("flow.json"), "name" },
            { new StringContent("FLOW_JSON"), "asset_type" }
        };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(flowJson));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", "flow.json");
        return SendAsync(HttpMethod.Post, $"{BaseUrl}/{flowId}/assets", accessToken, form, ct);
    }

    public Task<GraphResult> UpdateMetadataAsync(string accessToken, string flowId, IReadOnlyDictionary<string, string> fields, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"{BaseUrl}/{flowId}", accessToken, new FormUrlEncodedContent(fields), ct);

    public Task<GraphResult> PublishAsync(string accessToken, string flowId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"{BaseUrl}/{flowId}/publish", accessToken, null, ct);

    public Task<GraphResult> GetFlowAsync(string accessToken, string flowId, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"{BaseUrl}/{flowId}?fields=id,name,status,validation_errors,endpoint_uri,json_version", accessToken, null, ct);

    public Task<GraphResult> UploadPublicKeyAsync(string accessToken, string phoneNumberId, string publicKeyPem, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"{BaseUrl}/{phoneNumberId}/whatsapp_business_encryption", accessToken,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["business_public_key"] = publicKeyPem }), ct);

    public Task<GraphResult> GetPublicKeyAsync(string accessToken, string phoneNumberId, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"{BaseUrl}/{phoneNumberId}/whatsapp_business_encryption", accessToken, null, ct);

    public async Task<(bool Found, string? Error)> WabaHasFlowAsync(string accessToken, string wabaId, string flowId, CancellationToken ct)
    {
        string? url = $"{BaseUrl}/{wabaId}/flows?fields=id&limit=100";
        for (var page = 0; page < 20 && url is not null; page++)
        {
            var result = await SendAsync(HttpMethod.Get, url, accessToken, null, ct);
            if (!result.Success) return (false, result.Error);
            if (result.Body?["data"] is JsonArray rows && rows.Any(r => r?["id"]?.GetValue<string>() == flowId)) return (true, null);
            url = result.Body?["paging"]?["next"]?.GetValue<string>();
        }
        return (false, null);
    }

    private async Task<GraphResult> SendAsync(HttpMethod method, string url, string accessToken, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await _http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);

            JsonObject? body = null;
            try { body = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }

            if (response.IsSuccessStatusCode) return new GraphResult(true, body, null);

            var error = body?["error"]?["error_user_msg"]?.GetValue<string>()
                        ?? body?["error"]?["message"]?.GetValue<string>()
                        ?? $"HTTP {(int)response.StatusCode}";
            _logger.LogWarning("Graph {Method} {Path} failed: {Error}", method, new Uri(url).AbsolutePath, error);
            return new GraphResult(false, body, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            return new GraphResult(false, null, $"Could not reach Meta: {ex.Message}");
        }
    }
}
