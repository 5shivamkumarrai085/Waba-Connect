using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An administrative action worth keeping a record of — user created, role permissions changed,
/// logs cleared. Backs the "Audit Events" tab of /activity-logs.
///
/// Distinct from <see cref="MessageActivityLog"/>, which records outbound WhatsApp sends. This
/// one answers "who changed the configuration"; that one answers "what did we send and did Meta
/// accept it".
///
/// The user is denormalized (<see cref="UserName"/>) as well as referenced, so the trail stays
/// readable after an account is deleted.
/// </summary>
public class AuditLog
{
    [Key]
    public int Id { get; set; }

    /// <summary>Action performed, e.g. "User.Created", "SystemLog.Cleared".</summary>
    [Required, MaxLength(150)]
    public string Event { get; set; } = string.Empty;

    /// <summary>Broad grouping used by the UI filter: Auth, Settings, User, Role, Data.</summary>
    [Required, MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    public int? UserId { get; set; }

    [MaxLength(200)]
    public string? UserName { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Entity touched, for cross-referencing. e.g. "AppUser" / 42.</summary>
    [MaxLength(100)]
    public string? EntityType { get; set; }

    [MaxLength(100)]
    public string? EntityId { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
