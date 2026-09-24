using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// Provider credentials and sending settings for one email-channel <see cref="Connection"/>.
///
/// <para>
/// Mirrors how <see cref="WabaConfiguration"/> hangs off a Connection: the Connection is the
/// user-facing label, this row is the credential. Unlike WabaConfiguration, every secret here is
/// AES-encrypted at rest via IEncryptionService, and never leaves the process — the API returns
/// only the <c>Has*</c> booleans so the UI can show "configured" without ever holding the value.
/// </para>
/// </summary>
public class EmailConfiguration
{
    public int Id { get; set; }

    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }

    public EmailProviderType Provider { get; set; } = EmailProviderType.AmazonSes;

    public bool IsActive { get; set; } = true;

    // ── Amazon SES ───────────────────────────────────────────────────────────────────────────

    /// <summary>AWS region id, e.g. "ap-southeast-1". Never defaulted in code — an unset region
    /// is a configuration error the UI must surface, not something to guess.</summary>
    [MaxLength(40)]
    public string? Region { get; set; }

    /// <summary>IamRole stores nothing and is preferred in production; AccessKey uses the
    /// encrypted pair below.</summary>
    public EmailAuthMode AuthMode { get; set; } = EmailAuthMode.IamRole;

    [MaxLength(200)]
    public string? AccessKeyId { get; set; }

    /// <summary>AES ciphertext, never the raw key. Wider than the plaintext to allow for the
    /// Base64 + prepended IV that EncryptionService produces.</summary>
    [MaxLength(1000)]
    public string? SecretAccessKeyEncrypted { get; set; }

    /// <summary>SES configuration set that publishes delivery events to our SNS topic. Without
    /// one, SES sends fine but no delivery/bounce/complaint events ever arrive.</summary>
    [MaxLength(100)]
    public string? ConfigurationSet { get; set; }

    // ── SMTP (MailKit fallback) ──────────────────────────────────────────────────────────────

    [MaxLength(255)]
    public string? SmtpHost { get; set; }

    public int? SmtpPort { get; set; }

    public SmtpSecurityMode SmtpSecurity { get; set; } = SmtpSecurityMode.StartTls;

    [MaxLength(255)]
    public string? SmtpUsername { get; set; }

    [MaxLength(1000)]
    public string? SmtpPasswordEncrypted { get; set; }

    // ── IMAP (inbound reply polling) ─────────────────────────────────────────────────────────
    //
    // When set, the IMAP polling worker connects here to fetch replies. Credentials default to
    // the SMTP values at poll time when left null, because most providers use the same account
    // for both — so a connection that already sends via SMTP needs only a host to start receiving.

    [MaxLength(255)]
    public string? ImapHost { get; set; }

    public int? ImapPort { get; set; }

    public SmtpSecurityMode ImapSecurity { get; set; } = SmtpSecurityMode.SslOnConnect;

    /// <summary>Falls back to <see cref="SmtpUsername"/> when null.</summary>
    [MaxLength(255)]
    public string? ImapUsername { get; set; }

    /// <summary>Falls back to the SMTP password when null.</summary>
    [MaxLength(1000)]
    public string? ImapPasswordEncrypted { get; set; }

    // ── Shared sending settings ──────────────────────────────────────────────────────────────

    /// <summary>Messages per second this connection may emit, enforced across every running
    /// instance by the token bucket in <see cref="EmailSendQuota"/>. Null means fall back to the
    /// configured default rather than "unlimited" — an unbounded default would get the SES
    /// account throttled.</summary>
    public decimal? MaxSendRatePerSecond { get; set; }

    [MaxLength(200)]
    public string? DefaultFromName { get; set; }

    [MaxLength(255)]
    public string? DefaultFromEmail { get; set; }

    [MaxLength(255)]
    public string? DefaultReplyTo { get; set; }

    /// <summary>
    /// When a provider was first configured on this connection, or null if step 2 of the wizard
    /// was never completed.
    ///
    /// <para>
    /// Stored rather than inferred because "never set up" and "deliberately disconnected" look
    /// identical otherwise — both are inactive with no usable credential, since disconnecting
    /// clears the secrets. Without this the connections list would report a brand-new connection
    /// as Disconnected, and the dashboard's Connected/Disconnected counts would be wrong.
    /// </para>
    /// </summary>
    public DateTime? ConfiguredAt { get; set; }

    // ── Last test result (shown on the connection wizard) ────────────────────────────────────

    public DateTime? LastTestedAt { get; set; }

    public bool? LastTestSucceeded { get; set; }

    [MaxLength(1000)]
    public string? LastTestMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<EmailSenderIdentity> SenderIdentities { get; set; } = [];
    public ICollection<EmailSendingDomain> SendingDomains { get; set; } = [];
}
