using System.Text;
using System.Text.Json;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Re-sends inbound WhatsApp events to a customer-supplied endpoint.
///
/// <para>
/// Strictly secondary to the main pipeline. Meta retries a webhook it does not get a prompt 200
/// from, so anything that delays or fails our response costs us duplicate deliveries of every
/// message. This therefore runs after the event has already been processed and persisted, and it
/// swallows every failure: a customer's endpoint being down, slow, or misconfigured must have no
/// effect on whether their messages arrive.
/// </para>
/// <para>
/// Forwarding is per change rather than per payload, so an operator who subscribed to
/// <c>messages</c> alone does not also receive the template-status events that happened to arrive
/// in the same batch.
/// </para>
/// </summary>
public interface IWebhookForwarder
{
    /// <summary>
    /// Forwards whichever parts of this payload the operator has subscribed to. Never throws.
    /// </summary>
    Task ForwardAsync(WhatsAppWebhookPayload payload, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class WebhookForwarder : IWebhookForwarder
{
    /// <summary>Named client so the timeout and handler lifetime belong to this feature alone.</summary>
    public const string HttpClientName = "webhook-forwarder";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOmniSettingsService _settings;
    private readonly ILogger<WebhookForwarder> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public WebhookForwarder(
        IHttpClientFactory httpClientFactory,
        IOmniSettingsService settings,
        ILogger<WebhookForwarder> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ForwardAsync(WhatsAppWebhookPayload payload, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _settings.GetFlagAsync("webhook.resendEnabled")) return;

            var url = await _settings.GetValueAsync("webhook.resendUrl");
            if (!TryParseDestination(url, out var destination)) return;

            var selected = await _settings.GetListAsync("webhook.events");
            if (selected.Count == 0)
            {
                _logger.LogDebug("Webhook re-send is on but no event types are selected; nothing to forward.");
                return;
            }

            var method = await ResolveMethodAsync();
            var subscribed = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in payload.Entry ?? [])
            {
                foreach (var change in entry.Changes ?? [])
                {
                    if (!subscribed.Contains(change.Field)) continue;

                    // One change per request, wrapped back into the shape Meta sent, so the
                    // receiving endpoint can use the same parser it would use for Meta itself.
                    var slice = new WhatsAppWebhookPayload
                    {
                        Object = payload.Object,
                        Entry = [new Entry { Id = entry.Id, Changes = [change] }]
                    };

                    await SendAsync(destination, method, slice, change.Field, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            // The outermost guard. Nothing about forwarding is allowed to reach the caller, which
            // is the pipeline that owes Meta a 200.
            _logger.LogError(ex, "Webhook forwarding failed; inbound processing is unaffected.");
        }
    }

    /// <summary>
    /// Posts one event, retrying transient failures.
    ///
    /// Three attempts with exponential backoff, matching <see cref="GroqProvider"/> — the retry
    /// shape already used for outbound calls in this codebase. A 4xx is not retried: the endpoint
    /// understood the request and rejected it, so repeating it only doubles the noise.
    /// </summary>
    private async Task SendAsync(
        Uri destination,
        HttpMethod method,
        WhatsAppWebhookPayload slice,
        string eventField,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var json = JsonSerializer.Serialize(slice, JsonOptions);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);

                using var request = new HttpRequestMessage(method, destination)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                // Lets the receiver tell a forwarded event from one Meta sent directly.
                request.Headers.TryAddWithoutValidation("X-Forwarded-By", "OmniConnect");
                request.Headers.TryAddWithoutValidation("X-Webhook-Event", eventField);

                using var response = await client.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Forwarded \"{Event}\" to {Host} ({Status}).",
                        eventField, destination.Host, (int)response.StatusCode);
                    return;
                }

                var isClientError = (int)response.StatusCode is >= 400 and < 500;

                _logger.LogWarning(
                    "Forwarding \"{Event}\" to {Host} returned {Status} (attempt {Attempt}/{Max}).",
                    eventField, destination.Host, (int)response.StatusCode, attempt, maxAttempts);

                if (isClientError) return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The application is shutting down. Abandoning a forward is the right call.
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Forwarding \"{Event}\" to {Host} failed (attempt {Attempt}/{Max}).",
                    eventField, destination.Host, attempt, maxAttempts);
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }

        _logger.LogError(
            "Gave up forwarding \"{Event}\" to {Host} after {Max} attempts.",
            eventField, destination.Host, maxAttempts);
    }

    private async Task<HttpMethod> ResolveMethodAsync()
    {
        var configured = await _settings.GetValueAsync("webhook.resendMethod");
        return string.Equals(configured, "PUT", StringComparison.OrdinalIgnoreCase)
            ? HttpMethod.Put
            : HttpMethod.Post;
    }

    /// <summary>
    /// Accepts only an absolute http/https URL.
    ///
    /// The value is operator-supplied and this process will connect to whatever it names, so the
    /// scheme is restricted rather than assumed: file:// and friends have no business here, and a
    /// relative string would resolve against nothing useful.
    /// </summary>
    private bool TryParseDestination(string? url, out Uri destination)
    {
        destination = null!;

        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("Webhook re-send is enabled but no destination URL is configured.");
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Webhook destination \"{Url}\" is not an absolute http(s) URL; skipping.", url);
            return false;
        }

        destination = parsed;
        return true;
    }
}
