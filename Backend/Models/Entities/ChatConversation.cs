using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

public class ChatConversation
{
    public int Id { get; set; }

    /// <summary>
    /// Which channel this thread belongs to. Defaults to WhatsApp, so existing rows keep their
    /// meaning. This is also part of the uniqueness key: the old unique index on
    /// {ContactId, ConnectionId} would have collided for a contact reachable on both WhatsApp
    /// and email through the same connection, silently merging two unrelated threads.
    /// </summary>
    public MessageChannel Channel { get; set; } = MessageChannel.WhatsApp;

    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    public int? WabaPhoneNumberId { get; set; }
    public WabaPhoneNumber? WabaPhoneNumber { get; set; }

    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }

    [MaxLength(1024)]
    public string? LastMessageText { get; set; }

    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public bool IsArchived { get; set; }

    // ── Operations: owner, status and SLA ─────────────────────────────────────────────────────

    /// <summary>The agent responsible for this conversation (routing or a manual assignment).</summary>
    public int? AssignedUserId { get; set; }
    public AppUser? AssignedUser { get; set; }
    public DateTime? AssignedAt { get; set; }

    public ConversationStatus Status { get; set; } = ConversationStatus.Open;

    /// <summary>When the customer last wrote. Drives auto-close and the reopen rule.</summary>
    public DateTime? LastInboundAt { get; set; }

    /// <summary>First-response SLA for the current open period: due, answered, breached.</summary>
    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? FirstRespondedAt { get; set; }
    public DateTime? SlaBreachedAt { get; set; }

    /// <summary>Resolution SLA for the current open period.</summary>
    public DateTime? ResolveDueAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = [];
}
