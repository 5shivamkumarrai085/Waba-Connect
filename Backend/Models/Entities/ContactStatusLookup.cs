using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An editable contact status (New, In Progress, Contacted…), replacing the hardcoded
/// ContactStatus enum and the hand-written literal array in ContactsController.
///
/// <para>
/// Two string columns on purpose. <see cref="Value"/> is the wire format — it is what
/// Contact.Status actually stores, and it never changes. <see cref="Name"/> is the label an
/// admin edits and the UI renders. Renaming "In Progress" to "Working" is therefore a one-row
/// update that touches no contact rows, no foreign keys and no data migration.
/// </para>
/// </summary>
public class ContactStatusLookup
{
    [Key]
    public int Id { get; set; }

    /// <summary>Immutable identifier stored on Contact.Status. Never rewritten after creation.</summary>
    [Required, MaxLength(50)]
    public string Value { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex colour for the status badge, e.g. "#22C55E".</summary>
    [MaxLength(9)]
    public string? Color { get; set; }

    /// <summary>
    /// Controls whether the status is offered in pickers. Display always resolves regardless,
    /// so historical rows using a retired status still render their label.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Seeded from the original enum. Protected from deletion.</summary>
    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
