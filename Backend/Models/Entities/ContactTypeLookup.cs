using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An editable contact type (Lead, Customer, Vendor…), replacing the hardcoded ContactType enum
/// that <c>ContactsController.GetTypes()</c> surfaced through <c>name.ToLower()</c> — which is
/// why the type dropdown literally read "lead".
///
/// <para>
/// Mirrors <see cref="ContactStatusLookup"/> exactly, including the two-column split:
/// <see cref="Value"/> is the wire format stored on <c>Contact.Type</c> and never changes, while
/// <see cref="Name"/> is the label an admin edits. Renaming a type is therefore a one-row update
/// that touches no contact rows and needs no data migration.
/// </para>
/// </summary>
public class ContactTypeLookup
{
    [Key]
    public int Id { get; set; }

    /// <summary>Immutable identifier stored on Contact.Type. Never rewritten after creation.</summary>
    [Required, MaxLength(50)]
    public string Value { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex colour for the type badge, e.g. "#6366F1".</summary>
    [MaxLength(9)]
    public string? Color { get; set; }

    /// <summary>
    /// Controls whether the type is offered in pickers. Display always resolves regardless, so
    /// contacts still carrying a retired type keep rendering their label.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Seeded from the original enum. Protected from deletion.</summary>
    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
