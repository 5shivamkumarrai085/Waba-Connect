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
    /// <summary>"AmazonSes" or "Smtp".</summary>
    [Required, MaxLength(40)]
    public string Provider { get; set; } = "AmazonSes";

    // ── Amazon SES ───────────────────────────────────────────────────────────────────────────
    [MaxLength(40)]
    public string? Region { get; set; }

    /// <summary>"IamRole" or "AccessKey". IamRole stores nothing and is preferred in production.</summary>
    [MaxLength(20)]
    public string? AuthMode { get; set; }

    [MaxLength(200)]
    public string? AccessKeyId { get; set; }

    /// <summary>Write-only. Blank means "keep the stored key".</summary>
    [MaxLength(500)]
    public string? SecretAccessKey { get; set; }

    [MaxLength(100)]
    public string? ConfigurationSet { get; set; }

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
    /// "unlimited", because an unbounded rate is how an SES account gets throttled.
    /// </summary>
    [Range(0.1, 1000)]
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

    public string? Region { get; set; }
    public string AuthMode { get; set; } = string.Empty;

    /// <summary>Not a secret — the key id is an identifier, and showing it lets an operator
    /// confirm which credential is in use.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>
    /// Whether a secret is stored. The value itself is never returned: the reference UI rendered
    /// live credentials into a readable input, which puts a working key in front of anyone who
    /// can open the page or screenshot it.
    /// </summary>
    public bool HasSecretAccessKey { get; set; }

    public string? ConfigurationSet { get; set; }

    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpSecurity { get; set; }
    public string? SmtpUsername { get; set; }
    public bool HasSmtpPassword { get; set; }

    // ── IMAP (inbound reply polling) ─────────────────────────────────────────────────────────
    public string? ImapHost { get; set; }
    public int? ImapPort { get; set; }
    public string? ImapSecurity { get; set; }
    public string? ImapUsername { get; set; }
    public bool HasImapPassword { get; set; }

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
    public List<EmailSendingDomainResponse> Domains { get; set; } = [];

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
    public string VerificationStatus { get; set; } = string.Empty;
    public int? SendingDomainId { get; set; }
    public string? DomainName { get; set; }

    /// <summary>
    /// Whether this sender may actually be used. Campaigns check the same rule server-side; this
    /// exists so the wizard can explain why a sender is unselectable instead of failing later.
    /// </summary>
    public bool CanSend { get; set; }
}

public class EmailSendingDomainResponse
{
    public int Id { get; set; }
    public string DomainName { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = string.Empty;
    public string DkimStatus { get; set; } = string.Empty;
    public string? MailFromDomain { get; set; }
    public string MailFromStatus { get; set; } = string.Empty;
    public DateTime? LastCheckedAt { get; set; }
    public string? LastCheckMessage { get; set; }

    /// <summary>The DNS records that still need publishing, for the setup screen.</summary>
    public List<DnsRecordResponse> RequiredDnsRecords { get; set; } = [];
}

/// <param name="Type">"CNAME", "TXT" or "MX".</param>
/// <param name="Name">Host name to create.</param>
/// <param name="Value">Value to publish.</param>
/// <param name="Purpose">"DKIM", "SPF", "DMARC" or "MAIL FROM" — what breaks without it.</param>
/// <param name="Required">
/// False for records that improve deliverability but are not needed to send, so the UI can
/// separate "do this now" from "do this soon".
/// </param>
public record DnsRecordResponse(
    string Type,
    string Name,
    string Value,
    string Purpose,
    bool Required = true);

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
}
