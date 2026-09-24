using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One selectable "from" address for a connection — what the campaign wizard's Sender Email
/// dropdown and its "Add New Sender" action operate on.
/// </summary>
public class EmailSenderIdentity
{
    public int Id { get; set; }

    public int EmailConfigurationId { get; set; }
    public EmailConfiguration EmailConfiguration { get; set; } = null!;

    /// <summary>The domain that authorises this address. Null while the address is verified
    /// individually rather than by domain, which SES also allows.</summary>
    public int? SendingDomainId { get; set; }
    public EmailSendingDomain? SendingDomain { get; set; }

    /// <summary>Shown to recipients as the sender name.</summary>
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string EmailAddress { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? ReplyTo { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// What the provider says about this exact address.
    ///
    /// <para>
    /// For SES this is the identity's <c>VerifiedForSendingStatus</c>. It is refreshed from SES
    /// rather than assumed: an address is commonly verified in the AWS console after being added
    /// here, and an operator has no way to know they would need to re-add it.
    /// </para>
    /// </summary>
    public EmailIdentityStatus VerificationStatus { get; set; } = EmailIdentityStatus.NotStarted;

    /// <summary>
    /// When the provider was last asked about this address, so the send gate can re-check a stale
    /// answer instead of trusting one from before the address was verified.
    /// </summary>
    public DateTime? LastCheckedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
