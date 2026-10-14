using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace Remittance.Tests;

/// <summary>
/// The real exchange-rate adapters against the real public APIs. Skipped (not failed) when the
/// machine is offline or the API is down — that is the provider's state, not a defect here.
/// </summary>
public static class Phase6_LiveRateTests
{
    public static async Task RunAsync(RemittanceHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 6 — Live exchange rates (real APIs)");

        try
        {
            await using var scope = harness.CreateScope();
            var exchangeRateApi = scope.ServiceProvider.GetRequiredService<ExchangeRateApiProvider>();
            var frankfurter = scope.ServiceProvider.GetRequiredService<FrankfurterProvider>();

            run.Section("ExchangeRate-API (primary)");
            (string To, decimal Low, decimal High)[] pairs =
            [
                ("IDR", 2_500m, 6_000m), ("PHP", 8m, 20m), ("INR", 10m, 30m), ("BDT", 15m, 40m), ("SGD", 0.2m, 0.5m), ("VND", 4_000m, 9_000m)
            ];
            foreach (var (to, low, high) in pairs)
            {
                var result = await exchangeRateApi.GetRateAsync("MYR", to, CancellationToken.None);
                if (result.Outcome == ExchangeRateOutcome.Unavailable)
                {
                    run.Skip($"MYR→{to} live rate", $"provider unavailable: {result.Error}");
                    continue;
                }
                run.Check($"MYR→{to} is priced, within a sane band ({low}–{high})",
                    result.Outcome == ExchangeRateOutcome.Ok && result.Rate >= low && result.Rate <= high, $"{result.Outcome} {result.Rate} {result.Error}");
                run.Check($"MYR→{to} carries the provider's publication time (< 2 days old)",
                    result.AsOfUtc > DateTime.UtcNow.AddDays(-2) && result.AsOfUtc <= DateTime.UtcNow.AddMinutes(5), result.AsOfUtc.ToString("u"));
            }

            var bogus = await exchangeRateApi.GetRateAsync("MYR", "XXX", CancellationToken.None);
            if (bogus.Outcome == ExchangeRateOutcome.Unavailable) run.Skip("an unknown currency is reported unsupported", bogus.Error ?? "provider unavailable");
            else run.Check("an unknown currency is reported unsupported, not priced", bogus.Outcome == ExchangeRateOutcome.Unsupported, $"{bogus.Outcome} {bogus.Error}");

            run.Section("Frankfurter / ECB (fallback)");
            var idr = await frankfurter.GetRateAsync("MYR", "IDR", CancellationToken.None);
            if (idr.Outcome == ExchangeRateOutcome.Unavailable) run.Skip("Frankfurter MYR→IDR", idr.Error ?? "provider unavailable");
            else run.Check("Frankfurter prices MYR→IDR", idr.Outcome == ExchangeRateOutcome.Ok && idr.Rate is > 2_500m and < 6_000m, $"{idr.Outcome} {idr.Rate} {idr.Error}");

            var bdt = await frankfurter.GetRateAsync("MYR", "BDT", CancellationToken.None);
            if (bdt.Outcome == ExchangeRateOutcome.Unavailable) run.Skip("Frankfurter MYR→BDT", bdt.Error ?? "provider unavailable");
            else run.Check("Frankfurter reports BDT (not published by the ECB) as unsupported", bdt.Outcome == ExchangeRateOutcome.Unsupported, $"{bdt.Outcome} {bdt.Rate}");
        }
        catch (Exception ex)
        {
            run.Error("Phase 6 aborted", ex);
        }
    }
}
