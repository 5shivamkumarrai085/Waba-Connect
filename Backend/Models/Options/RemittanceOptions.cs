using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Options;

/// <summary>
/// Send Money settings, bound from the <c>Remittance</c> section and validated at startup.
/// </summary>
/// <remarks>
/// Business data — countries, payout methods, fees, payment methods — is NOT here; it lives in the
/// database and is edited under Setup › Send Money. This section only chooses adapters and sets
/// safety limits. The few secrets (an exchange-rate API key, the mock providers' webhook secrets)
/// are left empty in appsettings.json and supplied through user-secrets,
/// appsettings.secrets.local.json or environment variables, never committed.
/// </remarks>
public class RemittanceOptions
{
    public const string SectionName = "Remittance";

    /// <summary>Master switch. Off: the Flow endpoint answers "unavailable" and no workers run.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long a customer has to finish the Flow after it is sent.</summary>
    [Range(5, 240)]
    public int SessionTtlMinutes { get; set; } = 30;

    /// <summary>How many times one contact may open the Send Money form in ten minutes.</summary>
    [Range(1, 100)]
    public int MaxLaunchesPerTenMinutes { get; set; } = 5;

    /// <summary>How long a quoted rate is guaranteed.</summary>
    [Range(1, 60)]
    public int QuoteTtlMinutes { get; set; } = 15;

    [Required]
    public ExchangeRateOptions ExchangeRates { get; set; } = new();

    [Required]
    public OtpOptions Otp { get; set; } = new();

    [Required]
    public ProviderSelection Payments { get; set; } = new();

    [Required]
    public ProviderSelection Payouts { get; set; } = new();

    [Required]
    public FlowOptions Flows { get; set; } = new();

    [Required]
    public DevToolOptions DevTools { get; set; } = new();
}

public class ExchangeRateOptions
{
    /// <summary>Primary IExchangeRateProvider.ProviderName.</summary>
    [Required]
    public string Provider { get; set; } = "ExchangeRateApi";

    /// <summary>Tried in order when the primary cannot price a pair.</summary>
    public List<string> Fallbacks { get; set; } = new();

    /// <summary>How long a fetched rate is reused before asking the provider again.</summary>
    [Range(0, 3600)]
    public int CacheSeconds { get; set; } = 60;

    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Total time one customer request may spend fetching a rate, across the provider and its
    /// fallbacks. WhatsApp gives each screen about ten seconds in all.
    /// </summary>
    [Range(1, 9)]
    public int RequestBudgetSeconds { get; set; } = 3;

    /// <summary>How long the last good rate may still be used while every provider is failing.</summary>
    [Range(0, 3600)]
    public int ServeStaleSeconds { get; set; } = 300;

    /// <summary>A rate published longer ago than this is refused as stale.</summary>
    [Range(1, 168)]
    public int MaxRateAgeHours { get; set; } = 26;

    [Required]
    public ExchangeRateApiOptions ExchangeRateApi { get; set; } = new();

    [Required]
    public FrankfurterOptions Frankfurter { get; set; } = new();
}

public class ExchangeRateApiOptions
{
    /// <summary>Secret. With a key the paid v6 endpoint is used; without one, the open-access endpoint.</summary>
    public string? ApiKey { get; set; }

    [Required]
    public string BaseUrl { get; set; } = "https://v6.exchangerate-api.com/v6";

    [Required]
    public string OpenAccessBaseUrl { get; set; } = "https://open.er-api.com/v6";

    /// <summary>Overrides ExchangeRates:MaxRateAgeHours for this provider (it publishes daily).</summary>
    public int? MaxRateAgeHours { get; set; }
}

public class FrankfurterOptions
{
    [Required]
    public string BaseUrl { get; set; } = "https://api.frankfurter.dev/v1";

    /// <summary>
    /// ECB reference rates are published on working days only, so Monday's rate is Friday's.
    /// Defaults to four days to cover a weekend plus a holiday.
    /// </summary>
    public int? MaxRateAgeHours { get; set; } = 96;
}

public class OtpOptions
{
    /// <summary>IOtpProvider.ProviderName. "Mock" is development-only and refused in Production.</summary>
    [Required]
    public string Provider { get; set; } = "Mock";

    [Range(4, 8)]
    public int Length { get; set; } = 6;

    [Range(60, 1800)]
    public int TtlSeconds { get; set; } = 300;

    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 5;

    [Range(0, 600)]
    public int ResendCooldownSeconds { get; set; } = 30;

    [Range(0, 10)]
    public int MaxResends { get; set; } = 3;

    /// <summary>
    /// Codes one phone number may receive per hour and per day, across all its sessions. A new
    /// session used to reset every limit, which made the OTP an open SMS relay (SMS pumping).
    /// </summary>
    [Range(1, 1000)]
    public int MaxPerDestinationPerHour { get; set; } = 5;

    [Range(1, 10000)]
    public int MaxPerDestinationPerDay { get; set; } = 10;

    [Required]
    public MockOtpOptions Mock { get; set; } = new();
}

public class MockOtpOptions
{
    /// <summary>
    /// Development only: show the generated code on the Flow's OTP screen, because the mock
    /// provider delivers nothing. Ignored outside Development.
    /// </summary>
    public bool ExposeCodeInFlow { get; set; }
}

public class ProviderSelection
{
    /// <summary>Adapter name. "Mock" is development-only and refused in Production.</summary>
    [Required]
    public string Provider { get; set; } = "Mock";

    [Required]
    public MockProviderOptions Mock { get; set; } = new();
}

public class MockProviderOptions
{
    /// <summary>Secret used to sign the mock provider's callbacks to our webhook.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Base URL the mock checkout posts its signed callback to. Empty means this server's own
    /// address (App:PublicBaseUrl, else the request's origin).
    /// </summary>
    public string? CallbackBaseUrl { get; set; }
}

public class FlowOptions
{
    /// <summary>Flow JSON version uploaded to Meta.</summary>
    [Required]
    public string JsonVersion { get; set; } = "7.3";

    /// <summary>Send the draft version of an unpublished Flow (mode "draft"). For testing only.</summary>
    public bool AllowDraftSend { get; set; }
}

public class DevToolOptions
{
    /// <summary>The /api/dev/... helpers. Also require the Development environment and a Debug build.</summary>
    public bool Enabled { get; set; }
}
