namespace WhatsAppCampaignApi.Models.DTOs.Activity;

/// <summary>
/// Structured payload stored in <c>AuditLog.MetadataJson</c>.
///
/// <para>
/// One envelope with optional sections rather than a bare array, so a later event can record
/// something else here without changing the shape the client already parses. The client renders
/// whichever section is present and ignores the rest.
/// </para>
/// </summary>
public class AuditMetadata
{
    /// <summary>Chat messages removed by this event. Null when the event removed none.</summary>
    public List<AuditDeletedMessage>? DeletedMessages { get; set; }

    /// <summary>
    /// How many messages the operation actually removed. Kept separate from
    /// <see cref="DeletedMessages"/>'s length because that list is capped — a delete of 5,000
    /// messages must not write 5,000 records into a single audit row.
    /// </summary>
    public int? DeletedMessageCount { get; set; }

    /// <summary>True when <see cref="DeletedMessages"/> holds fewer entries than were deleted.</summary>
    public bool? DeletedMessagesTruncated { get; set; }
}

/// <summary>
/// One deleted chat message, as it existed at the moment of deletion.
///
/// A snapshot, not a reference: for a conversation delete the row is gone afterwards, so this is
/// the only remaining record of what it said.
/// </summary>
public class AuditDeletedMessage
{
    public int Id { get; set; }

    /// <summary>"Incoming" or "Outgoing", from the message's own Direction.</summary>
    public string? Direction { get; set; }

    /// <summary>The message text. Null for a media-only message.</summary>
    public string? Text { get; set; }

    /// <summary>Media type as WhatsApp reported it (image, document, …). Null for a text message.</summary>
    public string? MediaType { get; set; }

    /// <summary>Filename of the attachment, so a media message reads as something other than blank.</summary>
    public string? MediaFileName { get; set; }

    /// <summary>When the message itself was sent or received — not when it was deleted.</summary>
    public DateTime SentAt { get; set; }

    /// <summary>Delivery status at the time of deletion (Sent, Delivered, Read, Failed).</summary>
    public string? Status { get; set; }
}
