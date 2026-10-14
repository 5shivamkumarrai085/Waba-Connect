using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// The RSA key pair Meta uses to encrypt WhatsApp Flow data-exchange requests to one connection.
/// </summary>
/// <remarks>
/// Meta encrypts every request to a Flow endpoint with the public key uploaded for the sending
/// phone number; only the matching private key can read it. The private key is encrypted at rest
/// by the column converter and never leaves the server.
/// </remarks>
public class WhatsAppFlowEncryptionKey
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }
    public Connection Connection { get; set; } = null!;

    [Required]
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>PKCS#8 PEM. Encrypted by the column converter.</summary>
    [Required]
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>When the public key was last uploaded to Meta for this connection's numbers.</summary>
    public DateTime? UploadedAt { get; set; }

    /// <summary>Meta's verdict on the uploaded key (VALID / MISMATCH), when last read.</summary>
    [MaxLength(40)]
    public string? SignatureStatus { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A WhatsApp Flow this app created on a connection's WhatsApp Business Account, for one purpose.
/// </summary>
public class WhatsAppFlowRegistration
{
    public const string SendMoneyPurpose = "send_money";

    public int Id { get; set; }

    public int ConnectionId { get; set; }
    public Connection Connection { get; set; } = null!;

    /// <summary>What the Flow is for, e.g. send_money. One per connection and purpose.</summary>
    [Required, MaxLength(30)]
    public string Purpose { get; set; } = string.Empty;

    /// <summary>Meta's id for the Flow, once created.</summary>
    [MaxLength(40)]
    public string? MetaFlowId { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Meta's status, as last read: DRAFT, PUBLISHED, DEPRECATED, BLOCKED, THROTTLED.</summary>
    [MaxLength(20)]
    public string? Status { get; set; }

    /// <summary>The Flow JSON version uploaded.</summary>
    [MaxLength(10)]
    public string? JsonVersion { get; set; }

    /// <summary>Meta's validation errors from the last upload, as JSON; null when there were none.</summary>
    public string? ValidationErrorsJson { get; set; }

    /// <summary>
    /// Random key in the data-exchange URL. Identifies which connection's private key decrypts a
    /// request without exposing a guessable sequential id.
    /// </summary>
    [Required, MaxLength(64)]
    public string EndpointKey { get; set; } = string.Empty;

    /// <summary>The endpoint_uri last given to Meta.</summary>
    [MaxLength(500)]
    public string? EndpointUri { get; set; }

    public DateTime? LastSyncedAt { get; set; }
    public DateTime? PublishedAt { get; set; }

    /// <summary>Last time Meta's health check (ping) reached the endpoint.</summary>
    public DateTime? LastPingAt { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
