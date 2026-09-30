using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Whether a sender address may be used right now, and why not when it may not.
///
/// <para>
/// Shared by the campaign service and the expansion worker so both apply exactly one rule:
/// checked when a campaign is saved, so a misconfigured campaign fails once and clearly while the
/// operator is looking, and again at expansion, because a sender or connection can be switched
/// off while a large campaign is in flight.
/// </para>
/// <para>
/// Sending is standard SMTP, where the mail server itself decides which From addresses an account
/// may use; there is no separate verification to consult. Domain authentication (SPF, DKIM,
/// DMARC) is checked by DNS in the pre-flight check instead, where it is a warning with advice.
/// </para>
/// </summary>
public interface IEmailSenderGate
{
    Task<(bool CanSend, string? Reason)> CanSenderSendAsync(int senderIdentityId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class EmailSenderGate(AppDbContext dbContext) : IEmailSenderGate
{
    public async Task<(bool CanSend, string? Reason)> CanSenderSendAsync(int senderIdentityId, CancellationToken ct = default)
    {
        var sender = await dbContext.EmailSenderIdentities
            .AsNoTracking()
            .Where(s => s.Id == senderIdentityId)
            .Select(s => new
            {
                s.EmailAddress,
                s.IsActive,
                ConfigurationActive = s.EmailConfiguration.IsActive,
                HasHost = s.EmailConfiguration.SmtpHost != null && s.EmailConfiguration.SmtpHost != ""
            })
            .FirstOrDefaultAsync(ct);

        if (sender is null) return (false, "The sender identity no longer exists.");
        if (!sender.IsActive) return (false, $"Sender {sender.EmailAddress} is deactivated.");
        if (!sender.ConfigurationActive) return (false, "The email connection for this sender is disconnected.");
        if (!sender.HasHost) return (false, "The email connection for this sender has no SMTP server configured.");
        return (true, null);
    }
}
