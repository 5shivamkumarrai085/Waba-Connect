using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// Something a person (or the system, as "System") did that is worth keeping a record of — a
/// contact or campaign created, a template approved, a chat reply sent, permissions changed. Backs
/// the Audit Log page (/audit-log) and the dashboard's Recent Activity. Append-only.
///
/// What was sent and whether it was delivered lives on the campaign recipients and email events;
/// this answers "who did what, and when".
///
/// The user is denormalized (<see cref="UserName"/>) as well as referenced, so the trail stays
/// readable after an account is deleted.
/// </summary>
public class AuditLog
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The number shown to users and quoted back in support ("event 12458").
    ///
    /// Deliberately separate from <see cref="Id"/>: a surrogate primary key leaks the table's row
    /// count and invites enumeration of the API by id. Backed by its own database sequence, so it
    /// is stable for the life of the row and unaffected by filtering, paging or deletions.
    /// </summary>
    public long EventNumber { get; set; }

    /// <summary>Action performed, e.g. "User.Created", "SystemLog.Cleared".</summary>
    [Required, MaxLength(150)]
    public string Event { get; set; } = string.Empty;

    /// <summary>Broad grouping used by the UI filter: Auth, Settings, User, Role, Data.</summary>
    [Required, MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Derived from <see cref="Event"/>'s prefix ("Contact.Updated" → "Contact"). Stored as its
    /// own column so the activity log can filter on it in SQL with an index, rather than doing a
    /// LIKE over the event string.
    /// </summary>
    [MaxLength(100)]
    public string? Module { get; set; }

    /// <summary>Derived from <see cref="Event"/>'s suffix ("Contact.Updated" → "Updated").</summary>
    [MaxLength(100)]
    public string? Action { get; set; }

    /// <summary>"Success" or "Failed". Derived from the action verb.</summary>
    [MaxLength(20)]
    public string? Status { get; set; }

    /// <summary>
    /// Human-readable label for the entity at the time of the action, so the trail stays
    /// meaningful after the row it points at has been renamed or deleted.
    /// </summary>
    [MaxLength(200)]
    public string? EntityName { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Field-level before/after values as a JSON array of {field, oldValue, newValue}, captured
    /// automatically from EF's change tracker. Null when the operation changed no tracked fields
    /// (a pure read, or a create where "before" is meaningless).
    ///
    /// Sensitive fields are redacted by name before serialisation — see AuditChangeCapture.
    /// </summary>
    public string? ChangesJson { get; set; }

    /// <summary>
    /// Event-specific structured payload, as JSON. Null for the great majority of events.
    ///
    /// Separate from <see cref="ChangesJson"/> deliberately: that column is the field-level
    /// before/after table and the details panel renders it as such. This one carries whatever a
    /// particular event needs recorded that a field diff cannot express — the list of chat
    /// messages a delete removed, for instance, where the interesting content is the messages
    /// themselves rather than the IsDeleted flag flipping on each of them.
    ///
    /// Uncapped, like ChangesJson: writers are responsible for bounding what they put here.
    /// </summary>
    public string? MetadataJson { get; set; }

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
