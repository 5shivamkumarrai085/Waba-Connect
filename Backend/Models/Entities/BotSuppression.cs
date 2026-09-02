namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A customer who has told the bots to stop, and until when.
///
/// <para>
/// Its own table rather than a flag on <see cref="Contact"/> because this is conversation state,
/// not contact data: it is scoped to a connection (a customer can be talking to two of your
/// numbers), it expires on its own, and it is written by the message pipeline rather than by
/// anyone editing a contact.
/// </para>
/// <para>
/// It could not reuse <see cref="ConversationState"/> or <see cref="AiSession"/> — those are keyed
/// to a specific flow and a specific message bot respectively, and a stop applies across every
/// automation, including ones the customer has never triggered.
/// </para>
/// <para>
/// Rows are kept rather than deleted when they expire: "this customer opted out twice last month"
/// is worth being able to see, and an expired row costs nothing to skip.
/// </para>
/// </summary>
public class BotSuppression
{
    public int Id { get; set; }

    /// <summary>Normalised, digits only — the same form the bot router matches on.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Which of your numbers they said stop to. Null means a stop recorded before any connection
    /// could be resolved, which is treated as applying everywhere.
    /// </summary>
    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }

    /// <summary>The word they actually sent, for the audit trail and for support questions.</summary>
    public string MatchedKeyword { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When bots may speak to them again. Null means indefinitely — which is what a restart
    /// window of zero or less means, and the right reading of an unqualified "stop".
    /// </summary>
    public DateTime? ResumeAt { get; set; }

    /// <summary>Whether this row is still silencing the bots at <paramref name="now"/>.</summary>
    public bool IsActiveAt(DateTime now) => ResumeAt is null || ResumeAt > now;
}
