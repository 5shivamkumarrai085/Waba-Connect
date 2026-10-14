using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Services.Remittance;
using WhatsAppCampaignApi.Services.WhatsApp.Flows;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Endpoints called by machines, not people: Meta's WhatsApp Flows data exchange, and the payment
/// and payout providers' callbacks. Anonymous by necessity — none of the callers holds a token —
/// so each authenticates the request itself: Meta's requests are RSA/AES encrypted to our key and
/// signed with the app secret; providers' callbacks are signed with their webhook secret.
/// </summary>
/// <remarks>
/// They live under <c>/api/webhook/</c>, which the rate limiter exempts: Meta and payment providers
/// retry on 429, and throttling them only delays a customer's screen or payment confirmation.
/// [AllowAnonymous] is per action, as on WebhookController, so nothing added later inherits it.
/// </remarks>
[ApiController]
public class WhatsAppFlowsEndpointController : ControllerBase
{
    private readonly IWhatsAppFlowEndpoint _flows;
    private readonly IRemittanceCallbackIngestion _callbacks;

    public WhatsAppFlowsEndpointController(IWhatsAppFlowEndpoint flows, IRemittanceCallbackIngestion callbacks)
    {
        _flows = flows;
        _callbacks = callbacks;
    }

    /// <summary>WhatsApp Flows data exchange: INIT, BACK, data_exchange and Meta's health-check ping.</summary>
    [HttpPost("api/webhook/whatsapp-flows/{endpointKey}")]
    [AllowAnonymous]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Exchange(string endpointKey, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ct);
        var response = await _flows.HandleEncryptedAsync(endpointKey, body, Request.Headers["X-Hub-Signature-256"], ct);
        // Error codes (421, 427, 432…) go back with no body. StatusCode() would let [ApiController]
        // attach a plaintext problem-details body, which WhatsApp then tries to decrypt and reports
        // as "Response could not be decrypted" instead of acting on the code.
        return response.Body is null
            ? new ContentResult { StatusCode = response.StatusCode }
            : new ContentResult { StatusCode = response.StatusCode, Content = response.Body, ContentType = response.ContentType };
    }

    /// <summary>A payment provider's signed callback (payment succeeded, failed, expired, refunded).</summary>
    [HttpPost("api/webhook/payments/{provider}")]
    [AllowAnonymous]
    [RequestSizeLimit(256 * 1024)]
    public Task<IActionResult> PaymentCallback(string provider, CancellationToken ct) =>
        IngestAsync(RemittanceCallbackJob.PaymentKind, provider, ct);

    /// <summary>A payout provider's signed callback (transfer delivered or failed).</summary>
    [HttpPost("api/webhook/payouts/{provider}")]
    [AllowAnonymous]
    [RequestSizeLimit(256 * 1024)]
    public Task<IActionResult> PayoutCallback(string provider, CancellationToken ct) =>
        IngestAsync(RemittanceCallbackJob.PayoutKind, provider, ct);

    private async Task<IActionResult> IngestAsync(string kind, string provider, CancellationToken ct)
    {
        var body = System.Text.Encoding.UTF8.GetString(await ReadBodyAsync(ct));
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var refused = await _callbacks.IngestAsync(kind, provider, headers, body, ct);
        return refused is { } status ? StatusCode(status) : Ok(new { received = true });
    }

    /// <summary>The raw bytes: signatures are computed over them, so the body is never re-serialised.</summary>
    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
