namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// The logical queues. Constants rather than inline strings, because a typo in a queue name does
/// not fail — it silently enqueues work that no worker is listening for.
/// </summary>
public static class QueueNames
{
    /// <summary>Expands a campaign into one send job per recipient.</summary>
    public const string CampaignExpansion = "campaign-expansion";

    /// <summary>Delivers one email to one recipient.</summary>
    public const string EmailSend = "email-send";

    /// <summary>Parses and threads one inbound email.</summary>
    public const string EmailInbound = "email-inbound";

    /// <summary>Every queue, for the monitoring endpoint.</summary>
    public static readonly string[] All = [CampaignExpansion, EmailSend, EmailInbound];
}
