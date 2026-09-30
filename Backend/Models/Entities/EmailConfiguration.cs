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

    public EmailProviderType Provider { get; set; } = EmailProviderType.Smtp;

    public bool IsActive { get; set; } = true;

    // ── SMTP (MailKit) ──────────────────────────────────────────────────────────────────────

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

    /// <summary>
    /// Accept a certificate that fails validation (self-signed, or issued for the hosting
    /// provider's name rather than the mail host — common on cPanel). Off by default; an operator
    /// has to opt in per connection, and every poll logs a warning while it is on.
    /// </summary>
    public bool ImapAllowInvalidCertificate { get; set; }

    /// <summary>Highest IMAP UID already ingested from INBOX. Polling resumes after it, so a reply
    /// someone has already opened in webmail is still picked up.</summary>
    public long? ImapLastUid { get; set; }

    /// <summary>INBOX UIDVALIDITY that <see cref="ImapLastUid"/> belongs to. When the server
    /// reports a different value the stored UID is meaningless and polling falls back to a
    /// date window.</summary>
    public long? ImapUidValidity { get; set; }

    /// <summary>When the polling worker last finished a cycle for this mailbox, successful or not.</summary>
    public DateTime? ImapLastPolledAt { get; set; }

    /// <summary>Why the last poll failed, or null when it succeeded. Shown on the connection page
    /// so a broken inbox is visible instead of silently receiving nothing.</summary>
    [MaxLength(1000)]
    public string? ImapLastError { get; set; }

    // ── Shared sending settings ──────────────────────────────────────────────────────────────

    /// <summary>Messages per second this connection may emit, enforced across every running
    /// instance by the token bucket in <see cref="EmailSendQuota"/>. Null means fall back to the
    /// configured default rather than "unlimited" — an unbounded default would get the mail
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
}
