using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Remittance;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace Remittance.Tests;

/// <summary>Fee and FX arithmetic, and quotes under provider success, fallback, staleness and failure.</summary>
public static class Phase3_PricingAndQuoteTests
{
    public static async Task RunAsync(RemittanceHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 3 — Pricing and quotes");

        try
        {
            run.Section("Arithmetic");
            run.Check("margin: 3640 less 150 bps = 3585.4", RemittancePricing.ApplyMargin(3640m, 150) == 3585.4m, RemittancePricing.ApplyMargin(3640m, 150).ToString());
            run.Check("IDR receive amount is truncated to whole rupiah", RemittancePricing.ReceiveAmount(500m, 3585.55m, 0) == 1_792_775m,
                RemittancePricing.ReceiveAmount(500m, 3585.55m, 0).ToString());
            run.Check("PHP receive amount keeps 2 decimals, truncated", RemittancePricing.ReceiveAmount(100m, 15.37829m, 2) == 1537.82m);

            var rule = new RemittanceFeeRule { FixedFee = 4m, PercentFee = 0.5m, MinFee = 5m, MaxFee = 50m, FirstTransferFee = 0m, PromoLabel = "PROMO" };
            run.Check("fee = fixed + percent (4 + 0.5% of 500 = 6.50)", RemittancePricing.Fee(rule, 500m, false).Fee == 6.50m);
            run.Check("fee is clamped to the minimum (100 → 5.00)", RemittancePricing.Fee(rule, 100m, false).Fee == 5.00m);
            run.Check("fee is clamped to the maximum (20,000 → 50.00)", RemittancePricing.Fee(rule, 20_000m, false).Fee == 50.00m);
            run.Check("first transfer uses the promotional fee", RemittancePricing.Fee(rule, 500m, true) is { Fee: 0m, Label: "PROMO" });
            run.Check("no fee rule means no fee", RemittancePricing.Fee(null, 500m, false).Fee == 0m);
            run.Check("amounts with 3 decimals are rejected", RemittancePricing.ParseAmount("10.123") is null);
            run.Check("negative and zero amounts are rejected", RemittancePricing.ParseAmount("-5") is null && RemittancePricing.ParseAmount("0") is null);
            run.Check("\"1,500.50\" parses", RemittancePricing.ParseAmount("1,500.50") == 1500.50m);

            run.Section("Quotes");
            var (_, sessionId, _) = await harness.NewSessionAsync();
            await using var scope = harness.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quotes = scope.ServiceProvider.GetRequiredService<IQuoteService>();
            var session = await db.RemittanceSessions.FirstAsync(s => s.Id == sessionId);

            FakeExchangeRateProvider.Set(FakeExchangeRateProvider.Name, "MYR", "IDR", () => FakeExchangeRateProvider.Ok(FakeExchangeRateProvider.Name, 3640m));
            var ok = await quotes.CreateAsync(session, harness.CorridorId, harness.BankMethodId, 500m, CancellationToken.None);
            run.Check("a quote is created from the provider's rate", ok.Ok, ok.Error);
            if (ok.Quote is { } q)
            {
                run.Check("applied rate carries the corridor margin", q.MidRate == 3640m && q.AppliedRate == 3585.4m, $"{q.MidRate} → {q.AppliedRate}");
                run.Check("receive amount = 500 × 3585.4 = IDR 1,792,700", q.ReceiveAmount == 1_792_700m, q.ReceiveAmount.ToString());
                run.Check("first-transfer fee applies to a new customer", q.FeeAmount == 0m && q.FeeLabel == "PROMO" && q.TotalPayable == 500m, $"{q.FeeAmount} {q.FeeLabel} {q.TotalPayable}");
                run.Check("the quote is locked for 15 minutes", Math.Abs((q.ExpiresAt - q.CreatedAt).TotalMinutes - 15) < 0.1);
                run.Check("the provider and its publication time are recorded", q.RateProvider == FakeExchangeRateProvider.Name && q.RateAsOf != default);
            }

            var below = await quotes.CreateAsync(session, harness.CorridorId, harness.BankMethodId, 10m, CancellationToken.None);
            run.Check("below the corridor minimum is refused", !below.Ok && below.Error!.Contains("between"), below.Error);
            var inactive = await quotes.CreateAsync(session, harness.CorridorId, harness.InactiveMethodId, 500m, CancellationToken.None);
            run.Check("an inactive payout method cannot be quoted", !inactive.Ok, inactive.Error);

            FakeExchangeRateProvider.Set(FakeExchangeRateProvider.Name, "MYR", "IDR", () => ExchangeRateResult.Unavailable(FakeExchangeRateProvider.Name, "timed out after 5s"));
            FakeExchangeRateProvider.Set(FakeExchangeRateProvider.FallbackName, "MYR", "IDR", () => FakeExchangeRateProvider.Ok(FakeExchangeRateProvider.FallbackName, 3600m));
            var fallback = await quotes.CreateAsync(session, harness.CorridorId, harness.BankMethodId, 500m, CancellationToken.None);
            run.Check("when the primary times out, the fallback prices it", fallback.Ok && fallback.Quote!.RateProvider == FakeExchangeRateProvider.FallbackName,
                fallback.Quote?.RateProvider ?? fallback.Error);

            FakeExchangeRateProvider.Set(FakeExchangeRateProvider.FallbackName, "MYR", "IDR", () => FakeExchangeRateProvider.Ok(FakeExchangeRateProvider.FallbackName, 3600m, DateTime.UtcNow.AddHours(-40)));
            var stale = await quotes.CreateAsync(session, harness.CorridorId, harness.BankMethodId, 500m, CancellationToken.None);
            run.Check("a rate older than the maximum age is refused, not used", !stale.Ok && stale.Error!.Contains("temporarily unavailable"), stale.Error);

            FakeExchangeRateProvider.Set(FakeExchangeRateProvider.Name, "MYR", "IDR", () => ExchangeRateResult.Unsupported(FakeExchangeRateProvider.Name, "no such pair"));
            FakeExchangeRateProvider.Set(FakeExchangeRateProvider.FallbackName, "MYR", "IDR", () => ExchangeRateResult.Unsupported(FakeExchangeRateProvider.FallbackName, "no such pair"));
            var unsupported = await quotes.CreateAsync(session, harness.CorridorId, harness.BankMethodId, 500m, CancellationToken.None);
            run.Check("an unsupported currency gives a clear message and no quote", !unsupported.Ok && unsupported.Error!.Contains("not supported"), unsupported.Error);

            var activeQuotes = await harness.ScalarAsync("SELECT count(*) FROM \"FxQuotes\" WHERE \"SessionId\" = @p0 AND \"Status\" = 'Active'", sessionId);
            run.Check("only the newest quote in a session stays active", Convert.ToInt32(activeQuotes) == 1, activeQuotes?.ToString());

            // Leave the default rate in place for the phases after this one.
            FakeExchangeRateProvider.Set(FakeExchangeRateProvider.Name, "MYR", "IDR", () => FakeExchangeRateProvider.Ok(FakeExchangeRateProvider.Name, 3640m));
        }
        catch (Exception ex)
        {
            run.Error("Phase 3 aborted", ex);
        }
    }
}
