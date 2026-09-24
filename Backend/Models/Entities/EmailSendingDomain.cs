using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A domain this connection is allowed to send from, plus the state of its SPF/DKIM/DMARC setup.
///
/// <para>
/// Sends are gated on <see cref="VerificationStatus"/>: an unverified domain fails the campaign
/// at expansion time with a clear message, rather than letting SES reject every recipient one by
/// one. The DKIM tokens are whatever SES returned for this identity; the DNS records shown to the
/// operator are derived from them plus configured SPF/DMARC templates, never hardcoded strings.
/// </para>
/// </summary>
public class EmailSendingDomain
{
    public int Id { get; set; }

    public int EmailConfigurationId { get; set; }
    public EmailConfiguration EmailConfiguration { get; set; } = null!;

    [Required, MaxLength(255)]
    public string DomainName { get; set; } = string.Empty;

    public EmailIdentityStatus VerificationStatus { get; set; } = EmailIdentityStatus.NotStarted;

    public EmailIdentityStatus DkimStatus { get; set; } = EmailIdentityStatus.NotStarted;

    /// <summary>The CNAME tokens SES issued for Easy DKIM, as a JSON array. Stored rather than
    /// re-fetched so the DNS instructions stay stable and readable while propagation happens.</summary>
    public string? DkimTokensJson { get; set; }

    /// <summary>Custom MAIL FROM subdomain, when configured. Aligning MAIL FROM is what makes
    /// SPF pass under DMARC alignment rather than only DKIM.</summary>
    [MaxLength(255)]
    public string? MailFromDomain { get; set; }

    public EmailIdentityStatus MailFromStatus { get; set; } = EmailIdentityStatus.NotStarted;

    public DateTime? LastCheckedAt { get; set; }

    [MaxLength(1000)]
    public string? LastCheckMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<EmailSenderIdentity> SenderIdentities { get; set; } = [];
}
