using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A saved report — the column selection and filter set someone built, kept so they do not have to
/// rebuild it next month.
///
/// <para>
/// Private by default and shared only by an explicit toggle. A report is a saved question about
/// customer data, and its filters can encode things the author would not choose to publish (a
/// single campaign's failures, one agent's conversations), so visibility opens deliberately rather
/// than by omission.
/// </para>
/// </summary>
public class ReportDefinition
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Column keys as a JSON array, in the author's chosen order.
    ///
    /// Stored as JSON rather than as a join table because the set is a presentation preference,
    /// not a relationship — nothing else in the schema needs to query "which reports show the
    /// Delivered column", and a table would invite the pretence that something might.
    /// </summary>
    public string ColumnsJson { get; set; } = "[]";

    /// <summary>The saved filter set, serialised as a ReportQueryRequest.</summary>
    public string FiltersJson { get; set; } = "{}";

    /// <summary>
    /// Who saved it. Nullable so the report survives the author's account being deleted rather
    /// than disappearing with it — an orphaned shared report is still useful to the team.
    /// </summary>
    public int? OwnerUserId { get; set; }
    public AppUser? OwnerUser { get; set; }

    /// <summary>Denormalised, so the list stays readable after the owner's account is removed.</summary>
    [MaxLength(200)]
    public string? OwnerName { get; set; }

    /// <summary>When false, only the owner can see it.</summary>
    public bool IsShared { get; set; }

    /// <summary>Last time someone ran it. Null until first run.</summary>
    public DateTime? LastRunAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
