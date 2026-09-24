using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Sending-domain authentication: SPF, DKIM and DMARC.
///
/// <para>
/// This is not optional polish. Mail from an unauthenticated domain is junked or rejected outright
/// by every major mailbox provider, and — worse for the customer — it lets anyone forge mail as
/// their domain. So sends are gated on verification rather than merely warned about: a campaign
/// from an unverified domain fails at expansion, with an explanation, instead of quietly
/// destroying the customer's sending reputation one recipient at a time.
/// </para>
/// </summary>
public interface IEmailDomainService
{
    Task<List<EmailSendingDomainResponse>> GetAllAsync(int emailConfigurationId, CancellationToken ct = default);

    Task<EmailSendingDomainResponse> GetByIdAsync(int domainId, CancellationToken ct = default);

    /// <summary>
    /// Registers the domain with the provider and stores the DKIM tokens it returns, so the DNS
    /// records can be shown to whoever administers the zone.
    /// </summary>
    Task<EmailSendingDomainResponse> ProvisionAsync(
        int emailConfigurationId,
        string domainName,
        CancellationToken ct = default);

    /// <summary>
    /// Re-reads verification state from the provider. DNS propagation is not instant, so this is
    /// what the setup screen polls rather than assuming the records took effect.
    /// </summary>
    Task<EmailSendingDomainResponse> RefreshStatusAsync(int domainId, CancellationToken ct = default);

    Task DeleteAsync(int domainId, CancellationToken ct = default);

    /// <summary>
    /// Whether a sender may be used, and why not when it may not.
    ///
    /// <para>
    /// Shared by the campaign service and the dispatch worker so both apply exactly one rule.
    /// Checked at expansion so a misconfigured campaign fails once and clearly, and again at
    /// dispatch because verification can lapse while a large campaign is in flight.
    /// </para>
    /// </summary>
    Task<(bool CanSend, string? Reason)> CanSenderSendAsync(int senderIdentityId, CancellationToken ct = default);

    /// <summary>
    /// Re-reads one sender address's verification state from the provider.
    ///
    /// <para>
    /// SES verifies two kinds of identity: a whole domain, and a single email address. Only the
    /// first was ever refreshed here, so an address verified in SES — the normal way to send from
    /// a mailbox on a domain you do not control, and the only way while in the sandbox — stayed
    /// <c>NotStarted</c> in this database and was refused by the send gate forever.
    /// </para>
    /// <para>
    /// Returns the status it settled on, and the reason when that is not Verified.
    /// </para>
    /// </summary>
    Task<(EmailIdentityStatus Status, string? Reason)> RefreshSenderStatusAsync(
        int senderIdentityId,
        CancellationToken ct = default);
}
