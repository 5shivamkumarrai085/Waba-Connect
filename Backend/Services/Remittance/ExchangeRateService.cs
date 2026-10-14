using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace WhatsAppCampaignApi.Services.Remittance;

/// <summary>A usable rate, or the reason there is none.</summary>
public sealed record RateLookup(bool Ok, decimal Rate, DateTime AsOfUtc, string Provider, string? Error, bool Unsupported)
{
    public static RateLookup Failed(string error, bool unsupported) => new(false, 0, default, string.Empty, error, unsupported);
}

public interface IExchangeRateService
{
    /// <summary>
    /// Prices <paramref name="from"/> → <paramref name="to"/> with the configured provider, then its
    /// fallbacks. Never invents a rate: if no provider has a fresh one, the result says so.
    /// </summary>
    Task<RateLookup> GetRateAsync(string from, string to, CancellationToken ct);

    /// <summary>Fetches the pair from the providers now and caches it (the background refresher).</summary>
    Task<RateLookup> RefreshAsync(string from, string to, CancellationToken ct);
}

/// <summary>
/// Exchange rates for the Send Money screens, which WhatsApp gives about ten seconds to answer.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Pairs customers use are kept warm by <see cref="ExchangeRateRefresher"/>, so a screen
/// normally reads a cached rate and never waits on a provider.</item>
/// <item>On a miss only one fetch per pair runs at a time; concurrent requests wait for it instead
/// of all calling the provider (a burst used to multiply the provider's rate-limit errors).</item>
/// <item>A fetch has a total budget (<c>RequestBudgetSeconds</c>, default 3) across the provider
/// and its fallbacks. If it runs out, or every provider fails, the last good rate is used while it
/// is younger than <c>ServeStaleSeconds</c> (default 300); after that the customer is told rates
/// are unavailable — never given an invented or very old rate.</item>
/// <item>Failures are remembered for a few seconds so an outage does not make every request wait
/// out the timeouts again.</item>
/// </list>
/// </remarks>
public sealed class ExchangeRateService : IExchangeRateService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Fetching = new();

    /// <summary>Pairs asked for recently, with when; the refresher keeps these warm.</summary>
    internal static readonly ConcurrentDictionary<(string From, string To), DateTime> HotPairs = new();

    private static readonly TimeSpan FailureMemory = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, IExchangeRateProvider> _providers;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExchangeRateService> _logger;

    public ExchangeRateService(
        IEnumerable<IExchangeRateProvider> providers,
        IOptionsMonitor<RemittanceOptions> options,
        IMemoryCache cache,
        ILogger<ExchangeRateService> logger)
    {
        _providers = providers.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    private static string FreshKey(string from, string to) => $"fx:{from}:{to}";
    private static string LastGoodKey(string from, string to) => $"fx-last:{from}:{to}";
    private static string FailedKey(string from, string to) => $"fx-failed:{from}:{to}";

    public async Task<RateLookup> GetRateAsync(string from, string to, CancellationToken ct)
    {
        from = from.Trim().ToUpperInvariant();
        to = to.Trim().ToUpperInvariant();
        if (from == to) return new RateLookup(true, 1m, DateTime.UtcNow, "identity", null, false);

        var settings = _options.CurrentValue.ExchangeRates;
        if (settings.CacheSeconds > 0) HotPairs[(from, to)] = DateTime.UtcNow;

        if (settings.CacheSeconds > 0 && _cache.TryGetValue(FreshKey(from, to), out RateLookup? cached) && cached is not null)
        {
            return cached;
        }
        if (_cache.TryGetValue(FailedKey(from, to), out RateLookup? recentFailure) && recentFailure is not null)
        {
            return LastGoodOr(from, to, recentFailure);
        }

        // One fetch per pair at a time; whoever waited reads what it cached.
        var gate = Fetching.GetOrAdd(FreshKey(from, to), _ => new SemaphoreSlim(1, 1));
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.RequestBudgetSeconds)));
        try
        {
            await gate.WaitAsync(budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return LastGoodOr(from, to, RateLookup.Failed("Exchange rates are temporarily unavailable. Please try again in a few minutes.", false));
        }

        try
        {
            if (settings.CacheSeconds > 0 && _cache.TryGetValue(FreshKey(from, to), out cached) && cached is not null)
            {
                return cached;
            }
            var result = await FetchAsync(from, to, budget.Token, ct);
            return result.Ok ? result : LastGoodOr(from, to, result);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RateLookup> RefreshAsync(string from, string to, CancellationToken ct)
    {
        from = from.Trim().ToUpperInvariant();
        to = to.Trim().ToUpperInvariant();
        var gate = Fetching.GetOrAdd(FreshKey(from, to), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await FetchAsync(from, to, ct, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The last good rate while it is recent enough, else the failure.</summary>
    private RateLookup LastGoodOr(string from, string to, RateLookup failure)
    {
        if (failure.Unsupported) return failure;
        if (_cache.TryGetValue(LastGoodKey(from, to), out RateLookup? lastGood) && lastGood is not null)
        {
            _logger.LogInformation("Serving the last good {From}->{To} rate from {AsOf:u} while providers are unavailable.", from, to, lastGood.AsOfUtc);
            return lastGood;
        }
        return failure;
    }

    private async Task<RateLookup> FetchAsync(string from, string to, CancellationToken budget, CancellationToken caller)
    {
        var settings = _options.CurrentValue.ExchangeRates;
        var order = new[] { settings.Provider }.Concat(settings.Fallbacks)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var errors = new List<string>();
        var allUnsupported = true;

        foreach (var name in order)
        {
            if (!_providers.TryGetValue(name, out var provider))
            {
                errors.Add($"{name}: not a registered exchange-rate provider");
                allUnsupported = false;
                continue;
            }

            ExchangeRateResult result;
            try
            {
                result = await provider.GetRateAsync(from, to, budget);
            }
            catch (OperationCanceledException) when (!caller.IsCancellationRequested)
            {
                errors.Add($"{name}: no answer within the time budget");
                allUnsupported = false;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !caller.IsCancellationRequested)
            {
                // Adapters should not throw; one that does is treated as unavailable, not fatal.
                _logger.LogWarning(ex, "Exchange-rate provider {Provider} threw for {From}->{To}.", name, from, to);
                result = ExchangeRateResult.Unavailable(name, ex.Message);
            }

            if (result.Outcome == ExchangeRateOutcome.Ok)
            {
                var maxAge = result.MaxAge ?? TimeSpan.FromHours(settings.MaxRateAgeHours);
                var age = DateTime.UtcNow - result.AsOfUtc;
                if (age > maxAge)
                {
                    errors.Add($"{name}: rate is stale (published {age.TotalHours:F0}h ago)");
                    allUnsupported = false;
                    continue;
                }

                var lookup = new RateLookup(true, result.Rate, result.AsOfUtc, result.Provider, null, false);
                if (settings.CacheSeconds > 0)
                {
                    _cache.Set(FreshKey(from, to), lookup, TimeSpan.FromSeconds(settings.CacheSeconds));
                    _cache.Set(LastGoodKey(from, to), lookup, TimeSpan.FromSeconds(Math.Max(settings.CacheSeconds, settings.ServeStaleSeconds)));
                }
                _cache.Remove(FailedKey(from, to));
                return lookup;
            }

            if (result.Outcome != ExchangeRateOutcome.Unsupported) allUnsupported = false;
            errors.Add($"{name}: {result.Error}");
        }

        _logger.LogWarning("No exchange rate for {From}->{To}: {Errors}", from, to, string.Join("; ", errors));
        var failed = RateLookup.Failed(
            allUnsupported
                ? $"{from} to {to} is not supported by the configured rate providers."
                : "Exchange rates are temporarily unavailable. Please try again in a few minutes.",
            allUnsupported && errors.Count > 0);
        if (settings.CacheSeconds > 0) _cache.Set(FailedKey(from, to), failed, FailureMemory);
        return failed;
    }
}

/// <summary>
/// Keeps the exchange rates customers are using fresh in the background, so the Send Money screens
/// read a cached rate instead of waiting on a provider inside WhatsApp's time limit.
/// </summary>
public sealed class ExchangeRateRefresher : BackgroundService
{
    /// <summary>A pair nobody asked for in this long stops being refreshed.</summary>
    private static readonly TimeSpan HotFor = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly ILogger<ExchangeRateRefresher> _logger;

    public ExchangeRateRefresher(IServiceScopeFactory scopeFactory, IOptionsMonitor<RemittanceOptions> options, ILogger<ExchangeRateRefresher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = _options.CurrentValue;
                // Refresh a little before the cached rate lapses.
                var interval = TimeSpan.FromSeconds(Math.Max(15, settings.ExchangeRates.CacheSeconds * 0.8));

                if (settings.Enabled && settings.ExchangeRates.CacheSeconds > 0)
                {
                    var cutoff = DateTime.UtcNow - HotFor;
                    foreach (var (pair, lastAsked) in ExchangeRateService.HotPairs)
                    {
                        if (lastAsked < cutoff)
                        {
                            ExchangeRateService.HotPairs.TryRemove(pair, out _);
                            continue;
                        }
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            await scope.ServiceProvider.GetRequiredService<IExchangeRateService>().RefreshAsync(pair.From, pair.To, stoppingToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Refreshing the {From}->{To} rate failed.", pair.From, pair.To);
                        }
                    }
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
