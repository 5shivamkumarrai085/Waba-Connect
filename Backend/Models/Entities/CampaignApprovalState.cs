using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// Records an external authorization decision for a campaign.
///
/// <para>
/// This app does not implement maker-checker. The host application owns that, and this row is the
/// only footprint of it here: when the configured <c>ICampaignExecutionGate</c> answers
/// "AwaitingExternalApproval", the campaign parks in <c>CampaignStatus.AwaitingApproval</c> and
/// this row carries the host's reference id so a later decision can be matched back to it.
/// </para>
/// <para>
/// In the default deployment the gate auto-approves and no row is ever written. The table exists
/// so that connecting a host is a DI registration rather than a migration.
/// </para>
/// </summary>
public class CampaignApprovalState
{
    /// <summary>Also the primary key — a 1:1 extension of the campaign row.</summary>
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    /// <summary>
    /// The host system's own identifier for this approval request. Opaque to us; we only echo it
    /// back so the host can correlate its decision.
    /// </summary>
    [MaxLength(200)]
    public string? ExternalReferenceId { get; set; }

    /// <summary>"Pending", "Approved" or "Rejected" as reported by the gate. Deliberately a
    /// string rather than an enum: the vocabulary belongs to the host, not to us.</summary>
    [Required, MaxLength(40)]
    public string State { get; set; } = "Pending";

    public DateTime RequestedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    [MaxLength(200)]
    public string? DecidedBy { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    /// <summary>The user who submitted the campaign (the maker). The approver must be someone else.</summary>
    public int? RequestedByUserId { get; set; }

    /// <summary>The user who approved or rejected it (the checker).</summary>
    public int? DecidedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
