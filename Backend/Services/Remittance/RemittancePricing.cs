using System.Globalization;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Remittance;

/// <summary>
/// The arithmetic of a quote, kept free of I/O so it can be checked line by line.
/// </summary>
/// <remarks>
/// Rounding rules, chosen so the customer is never promised more than is delivered:
/// <list type="bullet">
/// <item>Fees and totals in the send currency: 2 decimals, half away from zero.</item>
/// <item>The amount received: truncated to the receive currency's minor units (0 for IDR/VND).</item>
/// <item>The applied rate: 8 decimals, truncated.</item>
/// </list>
/// Send currencies are assumed to have 2 minor units (MYR, INR, SGD…).
/// </remarks>
public static class RemittancePricing
{
    public const int SendDecimals = 2;

    /// <summary>Mid-market rate less the corridor's margin.</summary>
    public static decimal ApplyMargin(decimal midRate, int marginBps)
    {
        if (midRate <= 0) throw new ArgumentOutOfRangeException(nameof(midRate));
        if (marginBps is < 0 or >= 10_000) throw new ArgumentOutOfRangeException(nameof(marginBps));
        return Math.Round(midRate * (1m - marginBps / 10_000m), 8, MidpointRounding.ToZero);
    }

    public static decimal ReceiveAmount(decimal sendAmount, decimal appliedRate, int receiveDecimals) =>
        Math.Round(sendAmount * appliedRate, Math.Clamp(receiveDecimals, 0, 4), MidpointRounding.ToZero);

    /// <summary>The fee for <paramref name="sendAmount"/>, and the badge to show beside it (PROMO…), if any.</summary>
    public static (decimal Fee, string? Label) Fee(RemittanceFeeRule? rule, decimal sendAmount, bool isFirstTransfer)
    {
        if (rule is null) return (0m, null);

        if (isFirstTransfer && rule.FirstTransferFee is { } firstFee)
        {
            return (Money(Math.Max(0m, firstFee)), string.IsNullOrWhiteSpace(rule.PromoLabel) ? "FIRST TRANSFER" : rule.PromoLabel);
        }

        var fee = rule.FixedFee + sendAmount * rule.PercentFee / 100m;
        if (rule.MinFee is { } min) fee = Math.Max(fee, min);
        if (rule.MaxFee is { } max) fee = Math.Min(fee, max);
        return (Money(Math.Max(0m, fee)), string.IsNullOrWhiteSpace(rule.PromoLabel) ? null : rule.PromoLabel);
    }

    public static decimal Money(decimal value) => Math.Round(value, SendDecimals, MidpointRounding.AwayFromZero);

    /// <summary>Parses an amount typed into the Flow ("500", "500.5"); null when it is not a usable positive amount.</summary>
    public static decimal? ParseAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Trim().Replace(",", string.Empty);
        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        if (value <= 0 || decimal.Round(value, SendDecimals) != value) return null;
        return value;
    }

    /// <summary>"MYR 1,234.50" / "IDR 1,792,750".</summary>
    public static string Format(string currency, decimal amount, int decimals = SendDecimals) =>
        $"{currency} {amount.ToString("N" + Math.Clamp(decimals, 0, 4), CultureInfo.InvariantCulture)}";

    /// <summary>A rate without trailing noise: 3585.5, 0.31415.</summary>
    public static string FormatRate(decimal rate)
    {
        var digits = rate >= 100 ? 2 : rate >= 1 ? 4 : 6;
        return Math.Round(rate, digits, MidpointRounding.ToZero).ToString("#,0.######", CultureInfo.InvariantCulture);
    }
}
