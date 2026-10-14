using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Remittance.Providers;

/// <summary>
/// Resolves payment, payout and OTP adapters by name. Every registered implementation is injected
/// and looked up by its ProviderName, the same way EmailProviderFactory picks a mail transport.
/// </summary>
public interface IRemittanceProviderRegistry
{
    IPaymentProvider Payment(string name);
    IPayoutProvider Payout(string name);

    /// <summary>The OTP provider chosen by Remittance:Otp:Provider.</summary>
    IOtpProvider Otp();

    IReadOnlyCollection<string> PaymentProviderNames { get; }
    IReadOnlyCollection<string> PayoutProviderNames { get; }
}

public sealed class RemittanceProviderRegistry : IRemittanceProviderRegistry
{
    private readonly Dictionary<string, IPaymentProvider> _payments;
    private readonly Dictionary<string, IPayoutProvider> _payouts;
    private readonly Dictionary<string, IOtpProvider> _otp;
    private readonly IOptionsMonitor<RemittanceOptions> _options;

    public RemittanceProviderRegistry(
        IEnumerable<IPaymentProvider> payments,
        IEnumerable<IPayoutProvider> payouts,
        IEnumerable<IOtpProvider> otp,
        IOptionsMonitor<RemittanceOptions> options)
    {
        _payments = payments.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        _payouts = payouts.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        _otp = otp.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        _options = options;
    }

    public IReadOnlyCollection<string> PaymentProviderNames => _payments.Keys;
    public IReadOnlyCollection<string> PayoutProviderNames => _payouts.Keys;

    public IPaymentProvider Payment(string name) =>
        _payments.TryGetValue(name, out var p) ? p
            : throw new ProviderNotConfiguredException($"No payment provider named '{name}' is registered.");

    public IPayoutProvider Payout(string name) =>
        _payouts.TryGetValue(name, out var p) ? p
            : throw new ProviderNotConfiguredException($"No payout provider named '{name}' is registered.");

    public IOtpProvider Otp()
    {
        var name = _options.CurrentValue.Otp.Provider;
        return _otp.TryGetValue(name, out var p) ? p
            : throw new ProviderNotConfiguredException($"No OTP provider named '{name}' is registered (Remittance:Otp:Provider).");
    }
}
