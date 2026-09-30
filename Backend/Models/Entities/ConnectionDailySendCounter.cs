namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// Business-initiated messages sent from one connection on one UTC day, for the daily limit.
/// </summary>
/// <remarks>
/// A counter reserved with one atomic upsert per send. The limit used to be checked with a COUNT
/// over the day's chat messages before every recipient — a scan that grew with each message sent,
/// so a large campaign did quadratic work just deciding whether it was allowed to continue.
/// </remarks>
public class ConnectionDailySendCounter
{
    public int ConnectionId { get; set; }
    public DateOnly Day { get; set; }
    public int Count { get; set; }
}
