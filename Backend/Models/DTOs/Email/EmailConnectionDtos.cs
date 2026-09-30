using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs.Email;

/// <summary>
/// Step 1 of the connect-email wizard: what this connection is and who it sends as.
///
/// <para>
/// Creating the connection and its first sender identity together is deliberate. A connection
/// with no sender cannot send anything, and leaving it in that state would let an operator finish
/// the wizard and only discover the gap when a campaign fails.
/// </para>
/// </summary>
public class CreateEmailConnectionRequest
{
    /// <summary>Internal label, shown in the connections list.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Sender name recipients see.</summary>
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(255), EmailAddress]
    public string EmailAddress { get; set; } = string.Empty;

    [MaxLength(255), EmailAddress]
    public string? ReplyToEmail { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Short label reused across the app's connection pickers. Optional here because it is a
    /// WhatsApp-era convention, and an email connection does not need one to work.
    /// </summary>
    [MaxLength(4)]
    public string? Nickname { get; set; }
}

/// <summary>
/// Step 2: provider credentials and sending settings.
///
/// <para>
/// Both secret fields are write-only by contract. Leaving one blank keeps whatever is already
/// stored, so an operator can edit the region or the configuration set without re-typing a
/// credential — and, more importantly, so the API never has a reason to return one.
/// </para>
/// </summary>
public class SaveEmailProviderRequest
{
    /// <summary>The transport. Standard SMTP is the only one; the field stays for API stability.</summary>
    [MaxLength(40)]
    public string Provider { get; set; } = nameof(Enums.EmailProviderType.Smtp);

    // ── SMTP ─────────────────────────────────────────────────────────────────────────────────
    [MaxLength(255)]
    public string? SmtpHost { get; set; }

    [Range(1, 65535)]
    public int? SmtpPort { get; set; }

    /// <summary>"None", "StartTls" or "SslOnConnect".</summary>
    [MaxLength(20)]
    public string? SmtpSecurity { get; set; }

    [MaxLength(255)]
    public string? SmtpUsername { get; set; }

    /// <summary>Write-only. Blank means "keep the stored password".</summary>
    [MaxLength(500)]
    public string? SmtpPassword { get; set; }

    // ── Sending settings ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Messages per second. Left empty, the configured default applies — deliberately not
    /// "unlimited", because an unbounded rate is how a mail account gets throttled.
    /// </summary>
    [Range(Services.Catalogs.EmailConnectionCatalog.MinSendRatePerSecond, Services.Catalogs.EmailConnectionCatalog.MaxSendRatePerSecond)]
    public decimal? MaxSendRatePerSecond { get; set; }

    [MaxLength(200)]
    public string? DefaultFromName { get; set; }

    [MaxLength(255), EmailAddress]
    public string? DefaultFromEmail { get; set; }

    [MaxLength(255), EmailAddress]
    public string? DefaultReplyTo { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Tests credentials that have not been saved yet, so the wizard's Test Connection button works
/// before Save — otherwise an operator would have to store a possibly-wrong secret to find out
/// whether it is wrong.
/// </summary>
public class TestEmailProviderRequest : SaveEmailProviderRequest
{
    /// <summary>
    /// When set, secrets left blank in this request are taken from this stored configuration.
    /// Lets an operator re-test after changing only the region, without re-entering the key.
    /// </summary>
    public int? EmailConfigurationId { get; set; }
}

public class SendTestEmailRequest
{
    [Required, MaxLength(255), EmailAddress]
    public string ToAddress { get; set; } = string.Empty;

    /// <summary>Which sender identity to send as. Defaults to the connection's default sender.</summary>
    public int? SenderIdentityId { get; set; }
}

public class SaveEmailSenderIdentityRequest
{
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(255), EmailAddress]
    public string EmailAddress { get; set; } = string.Empty;

    [MaxLength(255), EmailAddress]
    public string? ReplyTo { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}

// ── Responses ────────────────────────────────────────────────────────────────────────────────

public class EmailConnectionResponse
{
    public int Id { get; set; }
    public int? ConnectionId { get; set; }
    public string ConnectionName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Description { get; set; }

    public string Provider { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpSecurity { get; set; }
    public string? SmtpUsername { get; set; }
    public bool HasSmtpPassword { get; set; }

    /// <summary>
    /// False when a stored password (SMTP or IMAP) cannot be decrypted with this server's key, so
    /// the page can ask for it to be re-entered before a send fails. The password itself is never
    /// returned.
    /// </summary>
    public bool CredentialsReadable { get; set; } = true;

    // ── IMAP (inbound reply polling) ─────────────────────────────────────────────────────────
    public string? ImapHost { get; set; }
    public int? ImapPort { get; set; }
    public string? ImapSecurity { get; set; }
    public string? ImapUsername { get; set; }
    public bool HasImapPassword { get; set; }
    public bool ImapAllowInvalidCertificate { get; set; }

    /// <summary>
    /// The mailbox replies are actually read from: the saved IMAP host, or the one derived from the
    /// SMTP host when none is saved. Null when this connection cannot receive replies.
    /// </summary>
    public string? EffectiveImapHost { get; set; }

    /// <summary>True when <see cref="EffectiveImapHost"/> was derived rather than saved.</summary>
    public bool ImapHostIsDerived { get; set; }

    public DateTime? ImapLastPolledAt { get; set; }
    public string? ImapLastError { get; set; }

    public decimal? MaxSendRatePerSecond { get; set; }
    public string? DefaultFromName { get; set; }
    public string? DefaultFromEmail { get; set; }
    public string? DefaultReplyTo { get; set; }

    /// <summary>Null until step 2 of the wizard has been completed at least once.</summary>
    public DateTime? ConfiguredAt { get; set; }

    public DateTime? LastTestedAt { get; set; }
    public bool? LastTestSucceeded { get; set; }
    public string? LastTestMessage { get; set; }

    /// <summary>
    /// Derived, so the UI does not have to re-implement the rule: a connection is only usable
    /// once it is active, has credentials and has a verified sender.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>What this provider can actually do, so the UI hides tracking it cannot deliver.</summary>
    public EmailProviderCapabilitiesResponse? Capabilities { get; set; }

    public List<EmailSenderIdentityResponse> Senders { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class EmailProviderCapabilitiesResponse
{
    public bool SupportsEventWebhooks { get; set; }
    public bool SupportsDkimProvisioning { get; set; }
    public bool SupportsSuppressionApi { get; set; }
    public bool SupportsInboundReceiving { get; set; }
    public int MaxMessageBytes { get; set; }
}

public class EmailSenderIdentityResponse
{
    public int Id { get; set; }
    public int EmailConfigurationId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string? ReplyTo { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether this sender may actually be used. Campaigns check the same rule server-side; this
    /// exists so the wizard can explain why a sender is unselectable instead of failing later.
    /// </summary>
    public bool CanSend { get; set; }
}

public class EmailProviderTestResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string>? Details { get; set; }
}

/// <summary>
/// IMAP settings for inbound reply polling. Username/password default to SMTP when omitted.
/// </summary>
public class SaveImapSettingsRequest
{
    [Required, MaxLength(255)]
    public string ImapHost { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int? ImapPort { get; set; }

    /// <summary>"None", "StartTls" or "SslOnConnect".</summary>
    [MaxLength(20)]
    public string? ImapSecurity { get; set; }

    /// <summary>Defaults to SmtpUsername when blank.</summary>
    [MaxLength(255)]
    public string? ImapUsername { get; set; }

    /// <summary>Write-only. Defaults to SMTP password when blank.</summary>
    [MaxLength(500)]
    public string? ImapPassword { get; set; }

    /// <summary>
    /// Accept a certificate that fails validation. Only for mail hosts that present a certificate
    /// for another name (common on shared hosting); leave off otherwise. Null keeps the saved value.
    /// </summary>
    public bool? ImapAllowInvalidCertificate { get; set; }
}
