namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// The logical queues. Constants rather than inline strings, because a typo in a queue name does
/// not fail — it silently enqueues work that no worker is listening for.
/// </summary>
public static class QueueNames
{
    /// <summary>
    /// The job contract version, part of every queue name. Bump it whenever a change means an
    /// older build of the app can no longer process the jobs (payload shape, credential format…).
    /// </summary>
    /// <remarks>
    /// Several builds routinely share one database: a teammate's checkout, a staging box, a
    /// rolling deploy. They all lease from the same table, so without a version in the name an
    /// older build takes jobs it cannot handle. That is exactly what happened with v1: builds that
    /// predate the AES-GCM (<c>enc:v2:</c>) credential format leased "email-send" jobs, could not
    /// read the SMTP password, and failed every recipient with "could not be decrypted". With the
    /// version in the name an older build simply never sees these jobs.
    ///
    /// v3 (round 5): a send now builds tracking and unsubscribe links on the verified public
    /// address and records a refused mailbox as a bounce; a v2 build would still send with a dead
    /// tunnel address and count bounces as failures, so it must not take these jobs.
    /// </remarks>
    public const string ContractVersion = "v3";

    /// <summary>Expands an email campaign into one send job per recipient.</summary>
    public const string CampaignExpansion = "campaign-expansion." + ContractVersion;

    /// <summary>Delivers one email to one recipient.</summary>
    public const string EmailSend = "email-send." + ContractVersion;

    /// <summary>Expands a WhatsApp campaign into one send job per recipient.</summary>
    public const string WhatsAppCampaignExpansion = "wa-campaign-expansion." + ContractVersion;

    /// <summary>Delivers one WhatsApp template message to one recipient.</summary>
    public const string WhatsAppSend = "wa-send." + ContractVersion;

    /// <summary>One verified Meta webhook delivery, stored before it is acknowledged.</summary>
    public const string WhatsAppWebhook = "wa-webhook." + ContractVersion;

    /// <summary>One signed event to one outbound webhook subscription.</summary>
    public const string WebhookOut = "webhook-out." + ContractVersion;

    /// <summary>Every queue, for the monitoring endpoint and the maintenance sweep.</summary>
    public static readonly string[] All = [CampaignExpansion, EmailSend, WhatsAppCampaignExpansion, WhatsAppSend, WhatsAppWebhook, WebhookOut];
}
