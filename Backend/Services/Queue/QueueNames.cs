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

    /// <summary>
    /// Contract version of the inbound WhatsApp webhook queue, versioned on its own. Bump it
    /// whenever the handling of an inbound message changes meaning (new bot node types, new reply
    /// kinds, different routing), because whichever build claims the job decides what the customer
    /// gets back.
    /// </summary>
    /// <remarks>
    /// v4: WhatsApp Flow replies (nfm_reply), the WhatsApp Flow bot node, trigger interruption and
    /// per-customer ordering. A hosted v3 build on the same database was claiming about half of the
    /// inbound messages and silently dropping flows whose nodes it did not know. Only this queue
    /// moved: the other queues are still processed by the v3 builds that share the database, and
    /// moving them would leave jobs enqueued here with nobody to run them.
    /// </remarks>
    public const string InboundContractVersion = "v4";

    /// <summary>Expands an email campaign into one send job per recipient.</summary>
    public const string CampaignExpansion = "campaign-expansion." + ContractVersion;

    /// <summary>Delivers one email to one recipient.</summary>
    public const string EmailSend = "email-send." + ContractVersion;

    /// <summary>Expands a WhatsApp campaign into one send job per recipient.</summary>
    public const string WhatsAppCampaignExpansion = "wa-campaign-expansion." + ContractVersion;

    /// <summary>Delivers one WhatsApp template message to one recipient.</summary>
    public const string WhatsAppSend = "wa-send." + ContractVersion;

    /// <summary>
    /// One verified Meta webhook delivery, stored before it is acknowledged. The webhook writes to
    /// this queue and the inbound worker reads it, so a deployment with an inbound queue group
    /// (<see cref="UseInboundQueueGroup"/>) only ever answers the messages it received itself.
    /// </summary>
    public static string WhatsAppWebhook { get; private set; } = InboundQueueName(null);

    /// <summary>Longest group suffix kept in a queue name.</summary>
    private const int MaxInboundGroupLength = 32;

    /// <summary>
    /// The inbound queue for a group: the shared queue when there is none, otherwise its own queue
    /// (lower case letters, digits and hyphens). Several deployments sharing one database — a
    /// developer's PC, a teammate's, the hosted server — otherwise all claim each other's customer
    /// messages, and whichever wins decides the reply with its own code and settings.
    /// </summary>
    public static string InboundQueueName(string? group)
    {
        var shared = "wa-webhook." + InboundContractVersion;
        if (string.IsNullOrWhiteSpace(group)) return shared;

        var safe = new System.Text.StringBuilder();
        foreach (var c in group.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) safe.Append(c);
            else if (safe.Length > 0 && safe[^1] != '-') safe.Append('-');
        }
        var suffix = safe.ToString().Trim('-');
        if (suffix.Length > MaxInboundGroupLength) suffix = suffix[..MaxInboundGroupLength].TrimEnd('-');
        return suffix.Length == 0 ? shared : $"{shared}.{suffix}";
    }

    /// <summary>Selects this process's inbound queue group (WhatsApp:Inbound:QueueGroup). Called once at startup.</summary>
    public static void UseInboundQueueGroup(string? group) => WhatsAppWebhook = InboundQueueName(group);

    /// <summary>One signed event to one outbound webhook subscription.</summary>
    public const string WebhookOut = "webhook-out." + ContractVersion;

    /// <summary>One verified payment or payout callback from a Send Money provider.</summary>
    public const string RemittanceCallback = "remit-callback." + ContractVersion;

    /// <summary>Hands one paid transfer to its payout provider.</summary>
    public const string RemittancePayout = "remit-payout." + ContractVersion;

    /// <summary>Every queue, for the monitoring endpoint and the maintenance sweep.</summary>
    public static string[] All => [CampaignExpansion, EmailSend, WhatsAppCampaignExpansion, WhatsAppSend, WhatsAppWebhook, WebhookOut, RemittanceCallback, RemittancePayout];
}
