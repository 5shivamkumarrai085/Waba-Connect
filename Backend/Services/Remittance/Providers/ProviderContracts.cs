namespace WhatsAppCampaignApi.Services.Remittance.Providers;

// The seams Send Money talks to the outside world through. Each is an interface with a
// ProviderName; Program.cs registers every implementation and the factories pick one by the name
// in configuration (Remittance:*:Provider) or on the configuration row (a payout method's
// PayoutProvider, a payment method's Provider). Adding a real provider means writing one class and
// registering it — no screen, service or Flow changes.
//
// Like IEmailProvider, an adapter reports ordinary failures (declined, timed out, unsupported) in
// its result instead of throwing, so callers handle them as business outcomes.

// ── Exchange rates ───────────────────────────────────────────────────────────────────────────

public interface IExchangeRateProvider
{
    string ProviderName { get; }

    /// <summary>The mid-market rate: 1 <paramref name="from"/> = Rate <paramref name="to"/>.</summary>
    Task<ExchangeRateResult> GetRateAsync(string from, string to, CancellationToken ct);
}

public enum ExchangeRateOutcome { Ok, Unsupported, Unavailable }

/// <param name="AsOfUtc">When the provider published the rate (not when it was fetched).</param>
/// <param name="MaxAge">How old this provider's rates may be before they are refused; null = the configured default.</param>
public sealed record ExchangeRateResult(
    ExchangeRateOutcome Outcome,
    string Provider,
    decimal Rate = 0,
    DateTime AsOfUtc = default,
    TimeSpan? MaxAge = null,
    string? Error = null)
{
    public static ExchangeRateResult Unsupported(string provider, string error) => new(ExchangeRateOutcome.Unsupported, provider, Error: error);
    public static ExchangeRateResult Unavailable(string provider, string error) => new(ExchangeRateOutcome.Unavailable, provider, Error: error);
}

// ── One-time passwords ───────────────────────────────────────────────────────────────────────

/// <summary>
/// Delivers a one-time code. Generating, hashing, expiring and checking codes is OtpService's job,
/// so a provider only ever moves a code to the customer — swapping the mock for an SMS gateway or a
/// WhatsApp authentication template changes delivery and nothing else.
/// </summary>
public interface IOtpProvider
{
    string ProviderName { get; }

    /// <summary>False for the development mock, which delivers nothing.</summary>
    bool DeliversCode { get; }

    Task<OtpDeliveryResult> SendAsync(OtpDeliveryRequest request, CancellationToken ct);
}

public sealed record OtpDeliveryRequest(string Destination, string Code, string Purpose, DateTime ExpiresAt, int? ConnectionId);

public sealed record OtpDeliveryResult(bool Sent, string? Error = null);

// ── Payments (the customer paying us) ────────────────────────────────────────────────────────

public interface IPaymentProvider
{
    string ProviderName { get; }

    /// <summary>Creates a hosted payment for an order; the customer pays on the provider's page, never in WhatsApp.</summary>
    Task<PaymentSessionResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct);

    /// <summary>
    /// Verifies a callback's signature against this provider's secret and reads the event. A
    /// callback whose signature does not verify must yield <c>SignatureValid = false</c> and no event.
    /// </summary>
    PaymentWebhookParseResult ParseWebhook(IReadOnlyDictionary<string, string> headers, string rawBody, DateTimeOffset now);
}

public sealed record PaymentRequest(
    string OrderReference,
    string MethodCode,
    decimal Amount,
    string Currency,
    DateTime ExpiresAt,
    string CustomerPhone);

public sealed record PaymentSessionResult(
    bool Success,
    string? ProviderPaymentId = null,
    string? PaymentUrl = null,
    string? PaymentReference = null,
    string? Error = null);

public enum PaymentEventKind { Succeeded, Failed, Expired, Refunded }

public sealed record PaymentWebhookEvent(
    string ProviderEventId,
    string ProviderPaymentId,
    PaymentEventKind Kind,
    decimal? Amount,
    string? Currency,
    DateTime OccurredAt,
    string? Reason = null);

public sealed record PaymentWebhookParseResult(bool SignatureValid, PaymentWebhookEvent? Event, string? Error = null);

// ── Payouts (the money reaching the recipient) ───────────────────────────────────────────────

public interface IPayoutProvider
{
    string ProviderName { get; }

    /// <summary>
    /// Sends one transfer. Must be idempotent on <see cref="PayoutRequest.OrderReference"/>: after a
    /// timeout or transient failure the same order is submitted again, and the provider has to
    /// return the existing payout rather than create a second one.
    /// </summary>
    Task<PayoutSubmitResult> SubmitAsync(PayoutRequest request, CancellationToken ct);

    PayoutWebhookParseResult ParseWebhook(IReadOnlyDictionary<string, string> headers, string rawBody, DateTimeOffset now);
}

public sealed record PayoutRequest(
    string OrderReference,
    decimal Amount,
    string Currency,
    string DestinationCountryCode,
    string PayoutMethodCode,
    string BeneficiaryName,
    string? InstitutionCode,
    IReadOnlyDictionary<string, string> Details);

public sealed record PayoutSubmitResult(bool Accepted, string? PayoutReference = null, string? Error = null, bool IsTransient = false);

public enum PayoutEventKind { Completed, Failed }

public sealed record PayoutWebhookEvent(string ProviderEventId, string PayoutReference, PayoutEventKind Kind, DateTime OccurredAt, string? Reason = null);

public sealed record PayoutWebhookParseResult(bool SignatureValid, PayoutWebhookEvent? Event, string? Error = null);

/// <summary>Thrown when configuration names an adapter that is not registered.</summary>
public sealed class ProviderNotConfiguredException(string message) : InvalidOperationException(message);
