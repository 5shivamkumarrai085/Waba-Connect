#if DEBUG
// Development harness only: compiled out of Release builds, and every action refuses outside the
// Development environment unless Remittance:DevTools:Enabled is also on. Nothing here is reachable
// on a production binary.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.BotFlow;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Remittance;
using WhatsAppCampaignApi.Services.Remittance.Providers;
using WhatsAppCampaignApi.Services.WhatsApp.Flows;

namespace WhatsAppCampaignApi.Controllers;

/// <remarks>
/// Lets the whole Send Money journey be driven from Postman without Meta or a real payment
/// provider: seed demo configuration, start a session, walk the Flow screens in plaintext (or
/// through the real encrypted endpoint with Meta-style encryption done here), and act as the mock
/// payment gateway's checkout page. Anonymous, like LocalTestController, so it needs no token
/// locally — which is exactly why it refuses everywhere but a developer machine.
/// </remarks>
[ApiController]
[Route("api/dev")]
[AllowAnonymous]
public class RemittanceDevController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly IConfiguration _configuration;

    public RemittanceDevController(AppDbContext db, IWebHostEnvironment environment, IOptionsMonitor<RemittanceOptions> options, IConfiguration configuration)
    {
        _db = db;
        _environment = environment;
        _options = options;
        _configuration = configuration;
    }

    private bool Allowed => _environment.IsDevelopment() && _options.CurrentValue.DevTools.Enabled;

    // ── set-up ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Seeds the Malaysian demo configuration and the "Send Money" bot flow. Idempotent.</summary>
    [HttpPost("remittance/seed-demo")]
    public async Task<IActionResult> SeedDemo([FromServices] IBotFlowService botFlows, CancellationToken ct)
    {
        if (!Allowed) return NotFound();

        var summary = await RemittanceDemoSeeder.SeedAsync(_db, _options.CurrentValue.Payments.Provider, _options.CurrentValue.Payouts.Provider, ct);

        // Created switched off. The database may be shared with builds that predate Send Money; an
        // active keyword flow would be picked up by their bot router, which has no executor for the
        // WhatsApp Flow node. Switch it on in Bot Flow once every build reading this database has it.
        var botFlowCreated = false;
        if (!await _db.BotFlows.AnyAsync(f => f.Name == "Send Money", ct))
        {
            await botFlows.CreateAsync(new CreateBotFlowRequest
            {
                Name = "Send Money",
                Description = "Keyword trigger that opens the Send Money WhatsApp Flow.",
                IsActive = false,
                FlowData = RemittanceDemoSeeder.SendMoneyBotFlowData()
            });
            botFlowCreated = true;
        }

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new { summary, botFlowCreated, botFlowActive = false },
            Message = "Demo Send Money configuration is in place. The \"Send Money\" bot flow was created switched off; turn it on in Bot Flow when every backend on this database includes Send Money."
        });
    }

    public sealed record StartSessionRequest(string Phone, int? ConnectionId, string? Name);

    /// <summary>
    /// Starts a Send Money session as if the bot had sent the Flow, without sending anything.
    /// Returns the flow_token and the endpoint key to use in the exchange calls.
    /// </summary>
    [HttpPost("remittance/sessions")]
    public async Task<IActionResult> StartSession([FromBody] StartSessionRequest request, [FromServices] IRemittanceSessionService sessions, CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        if (!PhoneNumberHelper.TryNormalize(request.Phone, out var phone, out var problem)) return BadRequest(new ApiResponse { Success = false, Message = problem });

        var connectionId = request.ConnectionId
            ?? await _db.Connections.AsNoTracking().OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        if (connectionId is null) return BadRequest(new ApiResponse { Success = false, Message = "Create a connection first." });

        var bare = PhoneNumberHelper.FormatForWhatsApp(phone);
        var contact = await _db.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Phone == phone || c.Phone == bare, ct);
        if (contact is { IsDeleted: true })
        {
            // Sessions of deleted contacts are filtered out, so the token would only ever get 427.
            return Conflict(new ApiResponse { Success = false, Message = "That number belongs to a deleted contact. Restore the contact or use another number." });
        }
        if (contact is null)
        {
            contact = new Contact { Phone = phone, Name = string.IsNullOrWhiteSpace(request.Name) ? "Send Money Tester" : request.Name!, IsActive = true };
            _db.Contacts.Add(contact);
            await _db.SaveChangesAsync(ct);
        }

        var registration = await EnsureRegistrationAsync(connectionId.Value, ct);
        await EnsureLocalKeyAsync(connectionId.Value, ct);
        var (session, token) = await sessions.StartAsync(contact.Id, bare, connectionId, ct, enforceLaunchLimit: false);

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new { sessionId = session.Id, flowToken = token, endpointKey = registration.EndpointKey, connectionId, contactId = contact.Id, expiresAt = session.ExpiresAt }
        });
    }

    // ── Flow exchange ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plaintext data exchange: the same handler the encrypted endpoint calls, minus encryption.
    /// Body: {"action":"INIT"|"data_exchange"|"BACK"|"ping","screen":"...","data":{...},"flow_token":"..."}.
    /// </summary>
    [HttpPost("whatsapp-flows/{endpointKey}/exchange")]
    public async Task<IActionResult> Exchange(string endpointKey, [FromBody] JsonObject request, [FromServices] IWhatsAppFlowEndpoint endpoint, CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        var (status, body) = await endpoint.HandlePlaintextAsync(endpointKey, request, ct);
        return body is null ? StatusCode(status, new { status, meaning = Meaning(status) }) : StatusCode(status, body);
    }

    /// <summary>
    /// Encrypts a plaintext request exactly as WhatsApp would, with this connection's public key, and
    /// signs it with the app secret. Post <c>body</c> to /api/webhook/whatsapp-flows/{endpointKey}
    /// with header X-Hub-Signature-256 = <c>signature</c>, then decrypt the answer with decrypt-response.
    /// </summary>
    [HttpPost("whatsapp-flows/{endpointKey}/encrypt-request")]
    public async Task<IActionResult> EncryptRequest(string endpointKey, [FromBody] JsonObject request, CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        var registration = await _db.WhatsAppFlowRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.EndpointKey == endpointKey, ct);
        if (registration is null) return NotFound();
        var key = await EnsureLocalKeyAsync(registration.ConnectionId, ct);

        using var rsa = FlowCrypto.LoadKey(key.PublicKeyPem);
        var encrypted = FlowCrypto.EncryptRequestLikeMeta(request.ToJsonString(), rsa);
        var body = JsonSerializer.Serialize(new
        {
            encrypted_flow_data = encrypted.EncryptedFlowData,
            encrypted_aes_key = encrypted.EncryptedAesKey,
            initial_vector = encrypted.InitialVector
        });

        var secret = await _db.WabaConfigurations.AsNoTracking()
            .Where(c => c.ConnectionId == registration.ConnectionId && c.FacebookAppSecret != null && c.FacebookAppSecret != "")
            .Select(c => c.FacebookAppSecret).FirstOrDefaultAsync(ct);
        var signature = string.IsNullOrEmpty(secret)
            ? null
            : "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        return Ok(new
        {
            url = $"/api/webhook/whatsapp-flows/{endpointKey}",
            body,
            signature,
            aesKey = Convert.ToBase64String(encrypted.AesKey),
            iv = Convert.ToBase64String(encrypted.Iv)
        });
    }

    public sealed record DecryptRequest([property: SkipSanitization] string Response, [property: SkipSanitization] string AesKey, [property: SkipSanitization] string Iv);

    /// <summary>Decrypts the encrypted endpoint's Base64 answer with the key and IV from encrypt-request.</summary>
    [HttpPost("whatsapp-flows/decrypt-response")]
    public IActionResult DecryptResponse([FromBody] DecryptRequest request)
    {
        if (!Allowed) return NotFound();
        var json = FlowCrypto.DecryptResponseLikeMeta(request.Response, Convert.FromBase64String(request.AesKey), Convert.FromBase64String(request.Iv));
        return Content(json, "application/json");
    }

    // ── mock payment gateway ─────────────────────────────────────────────────────────────────

    /// <summary>The mock gateway's hosted checkout page (what the "Pay" button in WhatsApp opens).</summary>
    [HttpGet("mock-payments/{paymentId}/checkout")]
    public async Task<IActionResult> Checkout(string paymentId, CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        var order = await _db.TransferOrders.AsNoTracking().FirstOrDefaultAsync(o => o.PaymentProvider == MockPaymentProvider.Name && o.ProviderPaymentId == paymentId, ct);
        if (order is null) return NotFound();

        var e = (string s) => WebUtility.HtmlEncode(s);
        var total = RemittancePricing.Format(order.SendCurrency, order.TotalPayable);
        var button = (string outcome, string label, string style) =>
            $"<form method=\"post\" action=\"/api/dev/mock-payments/{e(paymentId)}/simulate?outcome={outcome}\"><button style=\"{style}\">{label}</button></form>";

        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Mock payment gateway</title>
            <style>body{font-family:system-ui,sans-serif;max-width:420px;margin:24px auto;padding:0 16px;color:#1b2430}
            .card{border:1px solid #d9dee3;border-radius:10px;padding:18px;margin:14px 0}.warn{background:#fff4d6;border-color:#f0d58a}
            button{width:100%;padding:13px;border:0;border-radius:8px;font-size:15px;font-weight:600;margin-top:10px;cursor:pointer}
            dt{color:#5b6570;font-size:13px}dd{margin:0 0 8px;font-weight:600}</style></head><body>
            <div class="card warn"><b>SANDBOX — no real money moves.</b> This page stands in for a payment gateway's checkout in development.
            The buttons send the gateway's signed callback to this server, which verifies it exactly as it would a real one.</div>
            <div class="card"><dl><dt>Order</dt><dd>{{e(order.Reference)}}</dd><dt>Recipient</dt><dd>{{e(order.BeneficiaryName)}}</dd>
            <dt>Method</dt><dd>{{e(order.PaymentMethodCode)}}</dd><dt>Amount</dt><dd>{{e(total)}}</dd>
            <dt>Status</dt><dd>{{e(order.OrderStatus.ToString())}} / payment {{e(order.PaymentStatus.ToString())}}</dd></dl></div>
            {{button("succeeded", "Simulate successful payment", "background:#1f6f54;color:#fff")}}
            {{button("failed", "Simulate failed payment", "background:#f6e1db;color:#a23a26")}}
            {{button("expired", "Simulate expiry", "background:#e9ece9;color:#1b2430")}}
            </body></html>
            """;
        return Content(html, "text/html");
    }

    /// <summary>
    /// What the mock gateway does when the customer finishes: builds its callback, signs it with
    /// Remittance:Payments:Mock:WebhookSecret, and hands it to the same verify-and-queue path the
    /// public webhook uses. The order changes only once the worker applies the verified event.
    /// </summary>
    [HttpPost("mock-payments/{paymentId}/simulate")]
    public async Task<IActionResult> SimulatePayment(string paymentId, [FromQuery] string outcome, [FromServices] IRemittanceCallbackIngestion ingestion, CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        var order = await _db.TransferOrders.AsNoTracking().FirstOrDefaultAsync(o => o.PaymentProvider == MockPaymentProvider.Name && o.ProviderPaymentId == paymentId, ct);
        if (order is null) return NotFound();

        var type = outcome?.ToLowerInvariant() switch
        {
            "succeeded" => "payment.succeeded",
            "failed" => "payment.failed",
            "expired" => "payment.expired",
            "refunded" => "payment.refunded",
            _ => null
        };
        if (type is null) return BadRequest(new ApiResponse { Success = false, Message = "outcome must be succeeded, failed, expired or refunded." });

        var secret = _options.CurrentValue.Payments.Mock.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
            return BadRequest(new ApiResponse { Success = false, Message = "Set Remittance:Payments:Mock:WebhookSecret (secrets file) to sign mock callbacks." });

        var body = JsonSerializer.Serialize(new MockCallback("evt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), type, paymentId,
            order.TotalPayable, order.SendCurrency, DateTimeOffset.UtcNow, type == "payment.failed" ? "Declined by the customer's bank (mock)" : null), MockCallbackSigning.Json);
        var headers = new Dictionary<string, string> { [MockCallbackSigning.SignatureHeader] = MockCallbackSigning.Sign(secret, body, DateTimeOffset.UtcNow) };

        var refused = await ingestion.IngestAsync(RemittanceCallbackJob.PaymentKind, MockPaymentProvider.Name, headers, body, ct);
        var message = refused is null ? $"Signed {type} callback accepted; the order updates within a few seconds." : $"Callback refused with HTTP {refused}.";

        if (Request.HasFormContentType)
        {
            return Content($"<!doctype html><meta name=viewport content=\"width=device-width,initial-scale=1\"><body style=\"font-family:system-ui;max-width:420px;margin:24px auto;padding:0 16px\">"
                + $"<h3>{WebUtility.HtmlEncode(message)}</h3><p>You can return to WhatsApp.</p></body>", "text/html");
        }
        return Ok(new ApiResponse<object> { Success = refused is null, Data = new { callback = JsonNode.Parse(body), signatureHeader = headers[MockCallbackSigning.SignatureHeader] }, Message = message });
    }

    /// <summary>The mock payout partner reporting delivery (completed) or failure for a payout reference.</summary>
    [HttpPost("mock-payouts/{payoutReference}/simulate")]
    public async Task<IActionResult> SimulatePayout(string payoutReference, [FromQuery] string outcome, [FromServices] IRemittanceCallbackIngestion ingestion, CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        var type = outcome?.ToLowerInvariant() switch { "completed" => "payout.completed", "failed" => "payout.failed", _ => null };
        if (type is null) return BadRequest(new ApiResponse { Success = false, Message = "outcome must be completed or failed." });

        var secret = _options.CurrentValue.Payouts.Mock.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
            return BadRequest(new ApiResponse { Success = false, Message = "Set Remittance:Payouts:Mock:WebhookSecret (secrets file) to sign mock callbacks." });

        var body = JsonSerializer.Serialize(new MockCallback("evt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), type, payoutReference,
            null, null, DateTimeOffset.UtcNow, type == "payout.failed" ? "Beneficiary account closed (mock)" : null), MockCallbackSigning.Json);
        var headers = new Dictionary<string, string> { [MockCallbackSigning.SignatureHeader] = MockCallbackSigning.Sign(secret, body, DateTimeOffset.UtcNow) };
        var refused = await ingestion.IngestAsync(RemittanceCallbackJob.PayoutKind, MockPayoutProvider.Name, headers, body, ct);

        return Ok(new ApiResponse<object>
        {
            Success = refused is null,
            Data = new { callback = JsonNode.Parse(body), signatureHeader = headers[MockCallbackSigning.SignatureHeader] },
            Message = refused is null ? "Signed payout callback accepted." : $"Callback refused with HTTP {refused}."
        });
    }

    public sealed record SignRequest(string Kind, [property: SkipSanitization] string Body);

    /// <summary>
    /// Signs an arbitrary callback body with the mock secret, so Postman can call the public
    /// /api/webhook/payments/Mock or /payouts/Mock endpoint directly (and show a tampered body failing).
    /// </summary>
    [HttpPost("remittance/sign-callback")]
    public IActionResult Sign([FromBody] SignRequest request)
    {
        if (!Allowed) return NotFound();
        var secret = request.Kind == RemittanceCallbackJob.PayoutKind ? _options.CurrentValue.Payouts.Mock.WebhookSecret : _options.CurrentValue.Payments.Mock.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret)) return BadRequest(new ApiResponse { Success = false, Message = "The mock webhook secret is not configured." });
        return Ok(new { header = MockCallbackSigning.SignatureHeader, value = MockCallbackSigning.Sign(secret, request.Body, DateTimeOffset.UtcNow) });
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task<WhatsAppFlowRegistration> EnsureRegistrationAsync(int connectionId, CancellationToken ct)
    {
        var registration = await _db.WhatsAppFlowRegistrations
            .FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.Purpose == WhatsAppFlowRegistration.SendMoneyPurpose, ct);
        if (registration is not null) return registration;

        // A local registration with no Meta Flow yet: enough for the endpoint key the harness needs.
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

    /// <summary>The connection's key pair, generated locally (not uploaded) when there is none yet.</summary>
    private async Task<WhatsAppFlowEncryptionKey> EnsureLocalKeyAsync(int connectionId, CancellationToken ct)
    {
        var key = await _db.WhatsAppFlowEncryptionKeys.FirstOrDefaultAsync(k => k.ConnectionId == connectionId, ct);
        if (key is not null) return key;

        var pair = FlowCrypto.GenerateKeyPair();
        key = new WhatsAppFlowEncryptionKey { ConnectionId = connectionId, PublicKeyPem = pair.PublicKeyPem, PrivateKeyPem = pair.PrivateKeyPem };
        _db.WhatsAppFlowEncryptionKeys.Add(key);
        await _db.SaveChangesAsync(ct);
        return key;
    }

    private static string Meaning(int status) => status switch
    {
        427 => "Flow token unknown, expired or no longer valid (Meta closes the Flow).",
        421 => "Could not decrypt (Meta re-downloads the public key).",
        432 => "Signature check failed.",
        404 => "Unknown endpoint key.",
        _ => "See the server log."
    };
}
#endif
