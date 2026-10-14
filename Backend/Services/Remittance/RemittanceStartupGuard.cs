using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace WhatsAppCampaignApi.Services.Remittance;

/// <summary>
/// Refuses to start anywhere but Development with Send Money switched on and a development-only
/// adapter in play. The mock OTP delivers nothing and the mock gateway moves no money; either
/// reaching a real customer would be a security incident, so it fails the boot instead of a transfer.
/// </summary>
/// <remarks>
/// Two halves: <see cref="Validate"/> checks configuration before the host is built, and
/// <see cref="RemittanceProviderCheck"/> checks the database once it can — the payment and payout
/// provider of each method is chosen per row by an admin, not in configuration. The mock adapters
/// are only registered in Development (Program.cs), so outside it "Mock" cannot even be saved.
/// </remarks>
public static class RemittanceStartupGuard
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment()) return;
        if (!configuration.GetValue("Remittance:Enabled", false)) return;

        var problems = new List<string>();
        if (IsMock(configuration["Remittance:Otp:Provider"] ?? "Mock")) problems.Add("Remittance:Otp:Provider is the development mock (it sends no code)");
        if (configuration.GetValue("Remittance:Flows:AllowDraftSend", false)) problems.Add("Remittance:Flows:AllowDraftSend sends unpublished Flows to customers");
        if (configuration.GetValue("Remittance:DevTools:Enabled", false)) problems.Add("Remittance:DevTools:Enabled exposes the development endpoints");

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to start in {environment.EnvironmentName} with Send Money enabled: " + string.Join("; ", problems)
              + ". Configure real providers, or set Remittance:Enabled to false.");
        }
    }

    internal static bool IsMock(string? name) => string.Equals(name?.Trim(), "Mock", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The database half of the guard: outside Development, with Send Money on, every active payment
/// and payout method must name a provider that is actually registered (never the mock). Runs once
/// at startup and stops the host on failure.
/// </summary>
public sealed class RemittanceProviderCheck : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public RemittanceProviderCheck(IServiceScopeFactory scopeFactory, IHostEnvironment environment, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_environment.IsDevelopment() || !_configuration.GetValue("Remittance:Enabled", false)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payments = scope.ServiceProvider.GetServices<IPaymentProvider>().Select(p => p.ProviderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var payouts = scope.ServiceProvider.GetServices<IPayoutProvider>().Select(p => p.ProviderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var otp = scope.ServiceProvider.GetServices<IOtpProvider>().Select(p => p.ProviderName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var problems = new List<string>();
        var otpName = _configuration["Remittance:Otp:Provider"];
        if (!otp.Contains(otpName ?? string.Empty)) problems.Add($"OTP provider '{otpName}' is not available in this build");

        var paymentMethods = await db.RemittancePaymentMethods.AsNoTracking().Where(m => m.IsActive)
            .Select(m => new { m.Code, m.Provider }).ToListAsync(cancellationToken);
        problems.AddRange(paymentMethods
            .Where(m => RemittanceStartupGuard.IsMock(m.Provider) || !payments.Contains(m.Provider))
            .Select(m => $"payment method {m.Code} uses provider '{m.Provider}'"));

        var payoutMethods = await db.RemittancePayoutMethods.AsNoTracking().Where(m => m.IsActive)
            .Select(m => new { m.Code, m.PayoutProvider }).ToListAsync(cancellationToken);
        problems.AddRange(payoutMethods
            .Where(m => RemittanceStartupGuard.IsMock(m.PayoutProvider) || !payouts.Contains(m.PayoutProvider))
            .Select(m => $"payout method {m.Code} uses provider '{m.PayoutProvider}'"));

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to start in {_environment.EnvironmentName} with Send Money enabled: " + string.Join("; ", problems)
              + ". Switch those methods to a real provider (or off), or set Remittance:Enabled to false.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
