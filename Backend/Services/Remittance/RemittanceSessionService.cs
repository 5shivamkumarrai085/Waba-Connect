using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Remittance;

public interface IRemittanceSessionService
{
    /// <summary>
    /// Starts a Send Money session for a contact and returns the flow_token to send with the Flow.
    /// The contact's older open sessions are cancelled (one live form per customer, so two open
    /// Flows can never confirm two orders), and launches are rate-limited per contact.
    /// </summary>
    /// <exception cref="RemittanceLaunchLimitException">Too many launches for this contact just now.</exception>
    Task<(RemittanceSession Session, string FlowToken)> StartAsync(int contactId, string phoneNumber, int? connectionId, CancellationToken ct,
        bool enforceLaunchLimit = true);

    /// <summary>The session behind a flow_token, tracked, or null.</summary>
    Task<RemittanceSession?> FindByTokenAsync(string flowToken, CancellationToken ct);
}

/// <summary>A contact asked for the Send Money form too many times in a short period.</summary>
public sealed class RemittanceLaunchLimitException(string message) : Exception(message);

public sealed class RemittanceSessionService : IRemittanceSessionService
{
    private static readonly TimeSpan LaunchWindow = TimeSpan.FromMinutes(10);

    /// <summary>Prefix of Send Money flow tokens; routes a data-exchange request to its handler.</summary>
    public const string TokenPrefix = "sm";

    private readonly AppDbContext _db;
    private readonly IOptionsMonitor<RemittanceOptions> _options;

    public RemittanceSessionService(AppDbContext db, IOptionsMonitor<RemittanceOptions> options)
    {
        _db = db;
        _options = options;
    }

    public async Task<(RemittanceSession Session, string FlowToken)> StartAsync(int contactId, string phoneNumber, int? connectionId, CancellationToken ct,
        bool enforceLaunchLimit = true)
    {
        var now = DateTime.UtcNow;

        // Every launch sends a WhatsApp message and (later) an OTP, both of which cost money; a
        // customer or script repeating "send money" must not turn into unlimited sends.
        if (enforceLaunchLimit)
        {
            var since = now - LaunchWindow;
            var recent = await _db.RemittanceSessions.CountAsync(s => s.ContactId == contactId && s.CreatedAt > since, ct);
            if (recent >= _options.CurrentValue.MaxLaunchesPerTenMinutes)
            {
                throw new RemittanceLaunchLimitException("Too many Send Money requests from this contact in the last 10 minutes.");
            }
        }

        // One open form per customer: the older ones stop working, so they cannot each confirm an order.
        await _db.RemittanceSessions
            .Where(s => s.ContactId == contactId && (s.Status == RemittanceSessionStatus.Started || s.Status == RemittanceSessionStatus.Verified))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, RemittanceSessionStatus.Cancelled), ct);

        // Unpredictable, as Meta requires: 32 random bytes.
        var token = $"{TokenPrefix}_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

        var session = new RemittanceSession
        {
            Id = Guid.NewGuid(),
            FlowTokenHash = HashToken(token),
            ContactId = contactId,
            PhoneNumber = phoneNumber.Trim(),
            ConnectionId = connectionId,
            Status = RemittanceSessionStatus.Started,
            ExpiresAt = now.AddMinutes(_options.CurrentValue.SessionTtlMinutes)
        };

        _db.RemittanceSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return (session, token);
    }

    public Task<RemittanceSession?> FindByTokenAsync(string flowToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(flowToken)) return Task.FromResult<RemittanceSession?>(null);
        var hash = HashToken(flowToken);
        return _db.RemittanceSessions.FirstOrDefaultAsync(s => s.FlowTokenHash == hash, ct);
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
