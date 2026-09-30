using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A long-lived, single-use credential that buys a new short-lived access token.
/// </summary>
/// <remarks>
/// Only the SHA-256 of the token is stored, so a database read does not yield usable tokens.
/// Every refresh rotates: the presented token is revoked and replaced by a new one in the same
/// family. Presenting a token that was already rotated means it was stolen and replayed, and the
/// whole family is revoked (RFC 6819 §5.2.2.3).
/// </remarks>
public class RefreshToken
{
    public long Id { get; set; }

    public int UserId { get; set; }

    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>All tokens descended from one sign-in share this id.</summary>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    [MaxLength(200)]
    public string? RevokedReason { get; set; }

    [MaxLength(64)]
    public string? CreatedByIp { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public virtual AppUser? User { get; set; }
}
