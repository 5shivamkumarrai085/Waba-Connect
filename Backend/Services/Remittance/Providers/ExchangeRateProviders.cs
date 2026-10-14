using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Remittance.Providers;

/// <summary>
/// ExchangeRate-API (exchangerate-api.com). With <c>Remittance:ExchangeRates:ExchangeRateApi:ApiKey</c>
/// set it uses the keyed v6 pair endpoint; without one, the open-access endpoint, which covers 160+
/// currencies (MYR, IDR, PHP, INR, BDT, SGD, VND among them) and updates once a day.
/// </summary>
/// <remarks>The open-access endpoint asks for attribution and is rate limited; production should use a key.</remarks>
public sealed class ExchangeRateApiProvider : IExchangeRateProvider
{
    public const string Name = "ExchangeRateApi";

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly ILogger<ExchangeRateApiProvider> _logger;

    public ExchangeRateApiProvider(HttpClient http, IOptionsMonitor<RemittanceOptions> options, ILogger<ExchangeRateApiProvider> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public string ProviderName => Name;

    public async Task<ExchangeRateResult> GetRateAsync(string from, string to, CancellationToken ct)
    {
        var settings = _options.CurrentValue.ExchangeRates;
        var api = settings.ExchangeRateApi;
        var keyed = !string.IsNullOrWhiteSpace(api.ApiKey);
        var url = keyed
            ? $"{api.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(api.ApiKey!)}/pair/{from}/{to}"
            : $"{api.OpenAccessBaseUrl.TrimEnd('/')}/latest/{from}";

        var fetched = await ExchangeRateHttp.GetJsonAsync(_http, url, settings.TimeoutSeconds, ct);
        if (fetched.Error is not null)
        {
            // Never log the URL: in keyed mode it carries the API key.
            _logger.LogWarning("ExchangeRate-API could not price {From}->{To}: {Error}", from, to, fetched.Error);
            return ExchangeRateResult.Unavailable(Name, fetched.Error);
        }

        using var doc = fetched.Document!;
        var root = doc.RootElement;
        if (root.TryGetProperty("result", out var result) && result.GetString() != "success")
        {
            var errorType = root.TryGetProperty("error-type", out var et) ? et.GetString() : "unknown-error";
            return errorType is "unsupported-code" or "malformed-request"
                ? ExchangeRateResult.Unsupported(Name, $"{from} or {to} is not supported ({errorType}).")
                : ExchangeRateResult.Unavailable(Name, $"Provider error: {errorType}.");
        }

        decimal? rate = null;
        if (keyed && root.TryGetProperty("conversion_rate", out var cr) && cr.TryGetDecimal(out var keyedRate))
        {
            rate = keyedRate;
        }
        else if (!keyed && root.TryGetProperty("rates", out var rates) && rates.TryGetProperty(to, out var r) && r.TryGetDecimal(out var openRate))
        {
            rate = openRate;
        }

        if (rate is null or <= 0) return ExchangeRateResult.Unsupported(Name, $"No {from}->{to} rate is published.");

        var asOf = root.TryGetProperty("time_last_update_unix", out var t) && t.TryGetInt64(out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
            : DateTime.UtcNow;

        var maxAge = api.MaxRateAgeHours is { } hours ? TimeSpan.FromHours(hours) : (TimeSpan?)null;
        return new ExchangeRateResult(ExchangeRateOutcome.Ok, Name, rate.Value, asOf, maxAge);
    }
}

/// <summary>
/// Frankfurter (frankfurter.dev): European Central Bank reference rates, free and keyless. Covers
/// about 30 currencies — not BDT or VND, which it reports as unsupported so the next provider is tried.
/// </summary>
public sealed class FrankfurterProvider : IExchangeRateProvider
{
    public const string Name = "Frankfurter";

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly ILogger<FrankfurterProvider> _logger;

    public FrankfurterProvider(HttpClient http, IOptionsMonitor<RemittanceOptions> options, ILogger<FrankfurterProvider> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public string ProviderName => Name;

    public async Task<ExchangeRateResult> GetRateAsync(string from, string to, CancellationToken ct)
    {
        var settings = _options.CurrentValue.ExchangeRates;
        var url = $"{settings.Frankfurter.BaseUrl.TrimEnd('/')}/latest?base={from}&symbols={to}";

        var fetched = await ExchangeRateHttp.GetJsonAsync(_http, url, settings.TimeoutSeconds, ct);
        if (fetched.StatusCode == HttpStatusCode.NotFound)
        {
            return ExchangeRateResult.Unsupported(Name, $"ECB does not publish a {from}->{to} rate.");
        }
        if (fetched.Error is not null)
        {
            _logger.LogWarning("Frankfurter could not price {From}->{To}: {Error}", from, to, fetched.Error);
            return ExchangeRateResult.Unavailable(Name, fetched.Error);
        }

        using var doc = fetched.Document!;
        var root = doc.RootElement;
        if (!root.TryGetProperty("rates", out var rates) || !rates.TryGetProperty(to, out var r) || !r.TryGetDecimal(out var rate) || rate <= 0)
        {
            return ExchangeRateResult.Unsupported(Name, $"ECB does not publish a {from}->{to} rate.");
        }

        // ECB publishes around 16:00 CET; the date alone is what the API returns.
        var asOf = root.TryGetProperty("date", out var d)
                   && DateTime.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
            ? date.AddHours(15)
            : DateTime.UtcNow;

        var maxAge = settings.Frankfurter.MaxRateAgeHours is { } hours ? TimeSpan.FromHours(hours) : (TimeSpan?)null;
        return new ExchangeRateResult(ExchangeRateOutcome.Ok, Name, rate, asOf, maxAge);
    }
}

/// <summary>GET with a timeout and one retry on a transient failure; no exception escapes.</summary>
internal static class ExchangeRateHttp
{
    public sealed record Fetched(JsonDocument? Document, HttpStatusCode? StatusCode, string? Error);

    public static async Task<Fetched> GetJsonAsync(HttpClient http, string url, int timeoutSeconds, CancellationToken ct)
    {
        const int attempts = 2;
        string error = "no response";
        HttpStatusCode? status = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                using var response = await http.GetAsync(url, timeout.Token);
                status = response.StatusCode;
                if (response.StatusCode == HttpStatusCode.NotFound) return new(null, status, "not found");

                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    error = $"HTTP {(int)response.StatusCode}";
                }
                else
                {
                    // 4xx bodies still carry the provider's error JSON, which the caller reads.
                    var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    return new(await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token), status, null);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                error = $"timed out after {timeoutSeconds}s";
            }
            catch (HttpRequestException ex)
            {
                error = ex.Message;
            }
            catch (JsonException)
            {
                return new(null, status, "the response was not valid JSON");
            }

            if (attempt < attempts) await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        }

        return new(null, status, error);
    }
}
