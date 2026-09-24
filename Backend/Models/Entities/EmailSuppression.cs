using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An address we must not send to, and why.
///
/// <para>
/// This list is authoritative for our own enqueue decisions — we deliberately do not lean on the
/// SES account-level suppression list, because by the time SES suppresses a send it has already
/// been counted against our reputation, and a campaign reported as "sent" with silently dropped
/// recipients is worse than one that reports them as skipped. Checked twice: when a campaign is
/// expanded into jobs, and again immediately before the provider call, since a complaint can land
/// in the minutes between the two.
/// </para>
/// </summary>
public class EmailSuppression
{
    public int Id { get; set; }

    /// <summary>
    /// Lower-cased and trimmed. Comparisons run on this column only, never on the address as
    /// typed, so casing can never let a suppressed address slip through.
    /// </summary>
    [Required, MaxLength(255)]
    public string EmailAddressNormalized { get; set; } = string.Empty;

    public SuppressionScope Scope { get; set; } = SuppressionScope.Global;

    /// <summary>
    /// Set only when <see cref="Scope"/> is Connection — a hard bounce against one sending
    /// identity does not always mean the address is dead everywhere.
    /// </summary>
    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }

    public SuppressionReason Reason { get; set; }

    /// <summary>
    /// Where it came from: an SES event type, "UnsubscribeLink", an operator's name, or an import
    /// file name.
    /// </summary>
    [MaxLength(200)]
    public string? Source { get; set; }

    [MaxLength(2000)]
    public string? Detail { get; set; }

    /// <summary>
    /// Set for soft suppressions that should lapse. Null means permanent, which is the right
    /// default for complaints and hard bounces.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime SuppressedAt { get; set; }

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
