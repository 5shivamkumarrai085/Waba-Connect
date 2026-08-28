using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs.Activity;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Records administrative actions to the audit trail. Attribution is resolved from the current
/// request when there is one; background callers simply produce an entry with no user attached.
/// </summary>
public interface IAuditService
{
    /// <param name="eventName">Action performed, e.g. "User.Created".</param>
    /// <param name="category">Auth | Settings | User | Role | Data.</param>
    /// <param name="description">Human-readable detail shown in the activity log.</param>
    /// <param name="actorUserId">
    /// Overrides the actor resolved from the request. Needed for sign-in, which happens before
    /// there is a token to resolve — without it every successful login would be attributed to
    /// "System" instead of to the person who signed in.
    /// </param>
    /// <param name="metadata">
    /// Event-specific structured detail for the entry — the messages a chat delete removed, say.
    /// Serialised into <c>AuditLog.MetadataJson</c>. Null for the great majority of events, which
    /// are fully described by their description and field-level change set.
    /// </param>
    Task LogAsync(
        string eventName,
        string category,
        string? description = null,
        string? entityType = null,
        string? entityId = null,
        int? actorUserId = null,
        string? actorUserName = null,
        AuditMetadata? metadata = null);
}
