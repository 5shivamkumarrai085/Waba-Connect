using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Integrations;

namespace WhatsAppCampaignApi.Services.Remittance.Providers;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// DEVELOPMENT-ONLY ADAPTERS. None of these moves money or delivers a message. They exist so the
// whole Send Money journey can be exercised end to end before real providers are contracted, and
// startup refuses to run them in Production (see RemittanceStartupGuard). Each must be replaced by
// a real adapter implementing the same interface before go-live.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MOCK OTP — delivers nothing. The code is generated, hashed and checked by OtpService exactly as
/// it would be with a real provider; this class only pretends to send it. The code itself is never
/// logged. In Development, Remittance:Otp:Mock:ExposeCodeInFlow shows it on the OTP screen instead.
/// Replace with an SMS gateway or a WhatsApp authentication-template provider before production.
/// </summary>
public sealed class MockOtpProvider : IOtpProvider
{
    public const string Name = "Mock";

    private readonly ILogger<MockOtpProvider> _logger;

    public MockOtpProvider(ILogger<MockOtpProvider> logger) => _logger = logger;

    public string ProviderName => Name;
    public bool DeliversCode => false;

    public Task<OtpDeliveryResult> SendAsync(OtpDeliveryRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "[MOCK OTP] A {Purpose} code was issued (expires {ExpiresAt:u}). Nothing was delivered — this provider is for development only.",
            request.Purpose, request.ExpiresAt);
        return Task.FromResult(new OtpDeliveryResult(true));
    }
}

/// <summary>
/// Signatures for the mock providers' callbacks, in the same Stripe style the app already uses for
/// its own outbound webhooks: <c>t=&lt;unix&gt;,v1=&lt;hex HMAC-SHA256(secret, "t.body")&gt;</c>.
/// </summary>
public static class MockCallbackSigning
{
    public const string SignatureHeader = "X-Mock-Signature";
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, string body, DateTimeOffset now) => WebhookSigner.Sign(secret, now.ToUnixTimeSeconds(), body);

    public static bool Verify(string? secret, IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(secret)
        && headers.TryGetValue(SignatureHeader, out var header)
        && !string.IsNullOrWhiteSpace(header)
        && WebhookSigner.Verify(secret, header, body, Tolerance, now);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>The body the mock providers post to our webhooks.</summary>
public sealed record MockCallback(string Id, string Type, string ObjectId, decimal? Amount, string? Currency, DateTimeOffset Created, string? Reason = null);

/// <summary>
/// MOCK PAYMENT GATEWAY (sandbox). "Pays" through a test checkout page this server hosts in
/// Development (/api/dev/mock-payments/{id}/checkout). Its buttons send a signed callback through
/// the same verification and processing path a real gateway's would take, so an order becomes
/// Paid only via a verified callback — never because a button in WhatsApp was tapped.
/// </summary>
public sealed class MockPaymentProvider : IPaymentProvider
{
    public const string Name = "Mock";

    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly IConfiguration _configuration;

    public MockPaymentProvider(IOptionsMonitor<RemittanceOptions> options, IConfiguration configuration)
    {
        _options = options;
        _configuration = configuration;
    }

    public string ProviderName => Name;

    public Task<PaymentSessionResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct)
    {
        var paymentId = "mockpay_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var baseUrl = (_configuration["App:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');

        // Counter methods pay with a slip reference; online methods open a hosted page.
        var isCounter = request.MethodCode.Equals("COUNTER", StringComparison.OrdinalIgnoreCase);
        var reference = isCounter ? $"MOCK-{RandomNumberGenerator.GetInt32(10_000_000, 99_999_999)}" : null;

        return Task.FromResult(new PaymentSessionResult(
            Success: true,
            ProviderPaymentId: paymentId,
            PaymentUrl: string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl}/api/dev/mock-payments/{paymentId}/checkout",
            PaymentReference: reference));
    }

    public PaymentWebhookParseResult ParseWebhook(IReadOnlyDictionary<string, string> headers, string rawBody, DateTimeOffset now)
    {
        if (!MockCallbackSigning.Verify(_options.CurrentValue.Payments.Mock.WebhookSecret, headers, rawBody, now))
        {
            return new(false, null, "Missing or invalid signature.");
        }

        MockCallback? callback;
        try
        {
            callback = JsonSerializer.Deserialize<MockCallback>(rawBody, MockCallbackSigning.Json);
        }
        catch (JsonException)
        {
            return new(true, null, "Malformed callback body.");
        }

        PaymentEventKind? kind = callback?.Type switch
        {
            "payment.succeeded" => PaymentEventKind.Succeeded,
            "payment.failed" => PaymentEventKind.Failed,
            "payment.expired" => PaymentEventKind.Expired,
            "payment.refunded" => PaymentEventKind.Refunded,
            _ => null
        };

        if (callback is null || kind is null || string.IsNullOrWhiteSpace(callback.Id) || string.IsNullOrWhiteSpace(callback.ObjectId))
        {
            return new(true, null, $"Unknown or incomplete event '{callback?.Type}'.");
        }

        return new(true, new PaymentWebhookEvent(callback.Id, callback.ObjectId, kind.Value, callback.Amount, callback.Currency,
            callback.Created.UtcDateTime, callback.Reason));
    }
}

/// <summary>
/// MOCK PAYOUT PARTNER (sandbox). Accepts every payout and returns a reference; completion or
/// failure arrives later as a signed callback, sent in Development from
/// /api/dev/mock-payouts/{reference}/simulate.
/// </summary>
public sealed class MockPayoutProvider : IPayoutProvider
{
    public const string Name = "Mock";

    private readonly IOptionsMonitor<RemittanceOptions> _options;

    public MockPayoutProvider(IOptionsMonitor<RemittanceOptions> options) => _options = options;

    public string ProviderName => Name;

    /// <summary>Idempotent on the order reference, as the contract requires: a resubmission gets the same payout back.</summary>
    public Task<PayoutSubmitResult> SubmitAsync(PayoutRequest request, CancellationToken ct) =>
        Task.FromResult(new PayoutSubmitResult(true, "mockpo_" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.OrderReference)))[..16].ToLowerInvariant()));

    public PayoutWebhookParseResult ParseWebhook(IReadOnlyDictionary<string, string> headers, string rawBody, DateTimeOffset now)
    {
        if (!MockCallbackSigning.Verify(_options.CurrentValue.Payouts.Mock.WebhookSecret, headers, rawBody, now))
        {
            return new(false, null, "Missing or invalid signature.");
        }

        MockCallback? callback;
        try
        {
            callback = JsonSerializer.Deserialize<MockCallback>(rawBody, MockCallbackSigning.Json);
        }
        catch (JsonException)
        {
            return new(true, null, "Malformed callback body.");
        }

        PayoutEventKind? kind = callback?.Type switch
        {
            "payout.completed" => PayoutEventKind.Completed,
            "payout.failed" => PayoutEventKind.Failed,
            _ => null
        };

        if (callback is null || kind is null || string.IsNullOrWhiteSpace(callback.Id) || string.IsNullOrWhiteSpace(callback.ObjectId))
        {
            return new(true, null, $"Unknown or incomplete event '{callback?.Type}'.");
        }

        return new(true, new PayoutWebhookEvent(callback.Id, callback.ObjectId, kind.Value, callback.Created.UtcDateTime, callback.Reason));
    }
}
