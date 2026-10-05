using System.Collections.Concurrent;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Catalogs;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Realtime;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Polls IMAP mailboxes for inbound replies and threads them into conversations.
///
/// <para>
/// For each active SMTP connection that can receive (an IMAP host saved, or one derived from the
/// SMTP host), this worker:
/// 1. Connects via IMAP and opens INBOX
/// 2. Fetches every message after the last UID it processed — not just unread ones, so a reply
///    someone already opened in webmail is still ingested
/// 3. Routes delivery reports to the bounce pipeline and skips auto-generated mail
/// 4. Threads everything else via <see cref="IInboundEmailThreader"/> and, when the reply answers a
///    campaign message, records a Replied event for that recipient
/// 5. Stores the new high-water UID and the poll outcome on the connection
/// </para>
///
/// <para>
/// Runs only when <c>Email:Enabled</c> and <c>Email:Inbound:Enabled</c> are both true.
/// </para>
/// </summary>
public class ImapPollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<ImapPollingWorker> _logger;

    /// <summary>Consecutive failures per (configuration, UIDVALIDITY, UID), so one message that
    /// always throws is eventually stepped over instead of blocking its mailbox forever.</summary>
    private readonly ConcurrentDictionary<string, int> _messageFailures = new();

    public ImapPollingWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmailOptions> options,
        ILogger<ImapPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IMAP polling worker started.");

        try
        {
            // Let the application finish bootstrapping (migrations, seeding) before the first poll.
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollAllMailboxesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IMAP polling cycle failed; retrying next interval.");
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.Inbound.PollIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("IMAP polling worker stopped.");
    }

    /// <summary>One poll cycle over every connection that can receive replies.</summary>
    private async Task PollAllMailboxesAsync(CancellationToken ct)
    {
        var inbound = _options.CurrentValue.Inbound;

        List<EmailConfiguration> configurations;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            configurations = await dbContext.EmailConfigurations
                .AsNoTracking()
                .Where(c => c.IsActive
                         && c.ConnectionId != null
                         && c.Provider == EmailProviderType.Smtp
                         && ((c.ImapHost != null && c.ImapHost != "")
                             || (inbound.DeriveImapFromSmtp && c.SmtpHost != null && c.SmtpHost != "")))
                .ToListAsync(ct);
        }

        if (configurations.Count == 0) return;

        _logger.LogDebug("Polling {Count} IMAP mailbox(es).", configurations.Count);

        foreach (var config in configurations)
        {
            if (ct.IsCancellationRequested) break;
            await PollConfigurationAsync(config, inbound, ct);
        }
    }

    private async Task PollConfigurationAsync(EmailConfiguration config, InboundOptions inbound, CancellationToken ct)
    {
        string? error = null;
        var lastUid = config.ImapLastUid;
        var uidValidity = config.ImapUidValidity;

        try
        {
            ImapEndpoint? endpoint;
            string? reason;
            using (var scope = _scopeFactory.CreateScope())
            {
                var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
                endpoint = ImapEndpointResolver.Resolve(config, inbound, cipher => Decrypt(encryption, cipher, config.Id), out reason);
            }

            if (endpoint is null)
            {
                error = reason;
            }
            else
            {
                (lastUid, uidValidity) = await PollMailboxAsync(config, endpoint, inbound, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogWarning(ex,
                "IMAP poll failed for email configuration {ConfigId}; retrying next cycle.", config.Id);
        }

        await RecordPollOutcomeAsync(config.Id, lastUid, uidValidity, error);
    }

    /// <summary>
    /// Ingests every message after the stored high-water UID and returns the new high-water mark.
    /// </summary>
    private async Task<(long? LastUid, long? UidValidity)> PollMailboxAsync(
        EmailConfiguration config,
        ImapEndpoint endpoint,
        InboundOptions inbound,
        CancellationToken ct)
    {
        var connectionId = config.ConnectionId!.Value;

        using var client = await ImapEndpointResolver.ConnectAsync(endpoint, inbound.TimeoutSeconds, _logger, ct);

        var inbox = client.Inbox;
        await inbox.OpenAsync(inbound.MarkAsSeen ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, ct);

        var uidValidity = (long)inbox.UidValidity;
        var resumeFrom = config.ImapUidValidity == uidValidity ? config.ImapLastUid ?? 0 : 0;

        IList<UniqueId> candidates;
        if (resumeFrom > 0)
        {
            // IMAP returns the highest existing message for "n:*" even when its UID is below n,
            // so the result is filtered again client-side.
            var range = new UniqueIdRange(new UniqueId(inbox.UidValidity, (uint)resumeFrom + 1), UniqueId.MaxValue);
            candidates = (await inbox.SearchAsync(SearchQuery.Uids(range), ct))
                .Where(u => u.Id > resumeFrom)
                .ToList();
        }
        else
        {
            // First poll of this mailbox, or the server reset UIDVALIDITY: look back a bounded
            // window. Re-reading a message already stored is harmless — the threader skips any
            // Message-ID on record.
            candidates = await inbox.SearchAsync(
                SearchQuery.DeliveredAfter(DateTime.UtcNow.Date.AddDays(-inbound.InitialLookbackDays)), ct);
        }

        var batch = candidates.OrderBy(u => u.Id).Take(inbound.MaxMessagesPerPoll).ToList();
        long highWater = resumeFrom;

        if (batch.Count == 0)
        {
            await client.DisconnectAsync(quit: true, ct);
            return (highWater > 0 ? highWater : config.ImapLastUid, uidValidity);
        }

        _logger.LogInformation(
            "Found {Count} new message(s) in the IMAP inbox for connection {ConnectionId}.",
            batch.Count, connectionId);

        // Sizes first, so an oversized message is skipped without being downloaded.
        var sizes = (await inbox.FetchAsync(batch, MessageSummaryItems.UniqueId | MessageSummaryItems.Size, ct))
            .ToDictionary(s => s.UniqueId.Id, s => (long)(s.Size ?? 0));

        foreach (var uid in batch)
        {
            if (ct.IsCancellationRequested) break;

            var failureKey = $"{config.Id}:{uidValidity}:{uid.Id}";

            try
            {
                if (sizes.TryGetValue(uid.Id, out var size) && size > inbound.MaxSizeBytes)
                {
                    _logger.LogWarning(
                        "Skipping oversized inbound email (UID {Uid}, {Size} bytes) for connection {ConnectionId}.",
                        uid, size, connectionId);
                }
                else
                {
                    var mime = await inbox.GetMessageAsync(uid, ct);
                    await IngestAsync(mime, connectionId, inbound, uid, ct);
                }

                if (inbound.MarkAsSeen)
                {
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, ct);
                }

                _messageFailures.TryRemove(failureKey, out _);
                highWater = uid.Id;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failures = _messageFailures.AddOrUpdate(failureKey, 1, (_, n) => n + 1);

                if (failures >= inbound.MaxFailuresPerMessage)
                {
                    _logger.LogError(ex,
                        "IMAP message UID {Uid} for connection {ConnectionId} failed {Failures} times; skipping it.",
                        uid, connectionId, failures);
                    _messageFailures.TryRemove(failureKey, out _);
                    highWater = uid.Id;
                    continue;
                }

                // Stop here so the high-water mark never passes a message that has not been
                // stored; the next poll resumes at this one.
                _logger.LogWarning(ex,
                    "Failed to process IMAP message UID {Uid} for connection {ConnectionId} (attempt {Attempt}); will retry.",
                    uid, connectionId, failures);
                break;
            }
        }

        await client.DisconnectAsync(quit: true, ct);
        return (highWater > 0 ? highWater : config.ImapLastUid, uidValidity);
    }

    /// <summary>Routes one message: bounce report, auto-generated mail, or a reply to thread.</summary>
    private async Task IngestAsync(
        MimeKit.MimeMessage mime,
        int connectionId,
        InboundOptions inbound,
        UniqueId uid,
        CancellationToken ct)
    {
        // Delivery reports arrive as ordinary mail. They update the campaign rather than
        // becoming a conversation.
        var bounceInfo = ImapBounceDetector.TryParse(mime);
        if (bounceInfo is not null)
        {
            await ProcessBounceAsync(bounceInfo, connectionId, ct);
            return;
        }

        if (IsAutoGenerated(mime, inbound.LoopProtectionHeaders))
        {
            _logger.LogDebug("Skipping auto-generated message (UID {Uid}) for connection {ConnectionId}.", uid, connectionId);
            return;
        }

        // Own scope per message, so a failure cannot leave half-tracked entities behind for the next.
        using var scope = _scopeFactory.CreateScope();
        var threader = scope.ServiceProvider.GetRequiredService<IInboundEmailThreader>();

        var result = await threader.ThreadInboundMessageAsync(mime, connectionId, ct);
        if (result is null) return;

        var inboxNotifier = scope.ServiceProvider.GetService<IInboxNotifier>();
        if (inboxNotifier is not null)
        {
            await inboxNotifier.MessageReceivedAsync(result.Message.ConversationId, connectionId, ct);
        }

        // Reopen / SLA clock / routing, as for any inbound message.
        try
        {
            await scope.ServiceProvider.GetRequiredService<WhatsAppCampaignApi.Services.Chat.IConversationOperations>()
                .OnInboundAsync(result.Message.ConversationId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Conversation {ConversationId}: inbound bookkeeping (SLA/routing) failed.", result.Message.ConversationId);
        }

        if (result.CampaignId is null && result.CampaignContactId is null) return;

        var eventProcessor = scope.ServiceProvider.GetRequiredService<ICampaignEmailEventProcessor>();
        var messageId = result.Message.EmailDetail?.MessageIdHeader;

        await eventProcessor.ProcessAsync(
            kind: EmailEventKind.Replied,
            idempotencyKey: $"imap:reply:{messageId ?? $"{connectionId}:{uid}"}",
            source: "Imap",
            campaignId: result.CampaignId,
            campaignContactId: result.CampaignContactId,
            messageId: messageId,
            recipientAddress: mime.From.Mailboxes.FirstOrDefault()?.Address,
            occurredAt: mime.Date != default ? mime.Date.UtcDateTime : DateTime.UtcNow,
            ct: ct);
    }

    /// <summary>
    /// Routes a delivery report into the normalized event pipeline. Only a permanent failure is a
    /// bounce; a "delayed" report means the remote server is still retrying, so it is logged and
    /// otherwise ignored — suppressing on it would block a perfectly good address.
    /// </summary>
    /// <summary>
    /// Which campaign recipient a bounce belongs to. The original Message-ID is exact; when the
    /// report does not carry it (some servers strip the returned copy), the latest send to that
    /// address from this same connection within <see cref="BounceCatalog.MatchWindowDays"/> is
    /// used — never a send from another connection, and never an older one.
    /// </summary>
    public static async Task<(int? CampaignId, int? CampaignContactId)> ResolveBounceRecipientAsync(
        AppDbContext db, BounceInfo bounce, int connectionId, DateTime now, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(bounce.OriginalMessageId))
        {
            var byMessageId = await db.EmailMessageDetails
                .AsNoTracking()
                .Where(d => d.MessageIdHeader == bounce.OriginalMessageId)
                .Join(
                    db.ChatMessages.AsNoTracking(),
                    d => d.ChatMessageId,
                    m => m.Id,
                    (d, m) => new { m.CampaignId, m.CampaignContactId })
                .FirstOrDefaultAsync(ct);
            if (byMessageId?.CampaignContactId is not null) return (byMessageId.CampaignId, byMessageId.CampaignContactId);
        }

        if (string.IsNullOrEmpty(bounce.FinalRecipient)) return (null, null);

        var address = bounce.FinalRecipient.ToLowerInvariant();
        var since = now.AddDays(-BounceCatalog.MatchWindowDays);
        var byAddress = await db.CampaignContacts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(cc => cc.Campaign.ConnectionId == connectionId
                      && cc.Campaign.Channel == MessageChannel.Email
                      && cc.SentAt != null && cc.SentAt >= since
                      && cc.Contact.Email != null && cc.Contact.Email.ToLower() == address)
            .OrderByDescending(cc => cc.SentAt)
            .Select(cc => new { cc.CampaignId, cc.Id })
            .FirstOrDefaultAsync(ct);
        return byAddress is null ? (null, null) : (byAddress.CampaignId, byAddress.Id);
    }

    private async Task ProcessBounceAsync(BounceInfo bounce, int connectionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(bounce.FinalRecipient)) return;

        if (bounce.BounceType == EmailBounceType.Transient)
        {
            _logger.LogInformation("Transient delivery report ({Status}); not treated as a bounce.", bounce.StatusCode);
            return;
        }

        _logger.LogInformation("IMAP delivery report: permanent failure ({Status}).", bounce.StatusCode);

        using var scope = _scopeFactory.CreateScope();
        var eventProcessor = scope.ServiceProvider.GetRequiredService<ICampaignEmailEventProcessor>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (campaignId, campaignContactId) = await ResolveBounceRecipientAsync(db, bounce, connectionId, DateTime.UtcNow, ct);

        var idempotencyKey = campaignContactId is { } ccId
            ? $"imap:bounce:recipient:{ccId}"
            : $"imap:bounce:{bounce.OriginalMessageId ?? bounce.FinalRecipient}";

        await eventProcessor.ProcessAsync(
            kind: EmailEventKind.Bounced,
            idempotencyKey: idempotencyKey,
            source: "Imap",
            campaignId: campaignId,
            campaignContactId: campaignContactId,
            messageId: bounce.OriginalMessageId,
            recipientAddress: bounce.FinalRecipient,
            occurredAt: bounce.ReceivedAt,
            bounceType: bounce.BounceType,
            bounceSubType: bounce.StatusCode,
            diagnosticCode: bounce.DiagnosticText,
            ct: ct);
    }

    /// <summary>
    /// RFC 3834 auto-response detection, interpreted by header value rather than mere presence.
    /// Presence-only matching treated Outlook's X-Auto-Response-Suppress — a header a human reply
    /// routinely carries — as proof of a robot, so real replies were discarded.
    /// </summary>
    internal static bool IsAutoGenerated(MimeKit.MimeMessage mime, string[] loopHeaders)
    {
        foreach (var headerName in loopHeaders)
        {
            var value = mime.Headers[headerName];
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (headerName.Equals("Auto-Submitted", StringComparison.OrdinalIgnoreCase))
            {
                if (!value.Trim().StartsWith("no", StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }

            if (headerName.Equals("Precedence", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Trim().ToLowerInvariant() is "bulk" or "list" or "junk") return true;
                continue;
            }

            // A request to suppress auto-responses says nothing about the sender.
            if (headerName.Equals("X-Auto-Response-Suppress", StringComparison.OrdinalIgnoreCase)) continue;

            return true;
        }

        return false;
    }

    private async Task RecordPollOutcomeAsync(int configId, long? lastUid, long? uidValidity, string? error)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trimmedError = error is { Length: > 1000 } ? error[..1000] : error;

            // CancellationToken.None: losing the high-water mark on shutdown would re-read the
            // same messages next start (harmless but noisy), so the write is allowed to finish.
            await dbContext.EmailConfigurations
                .Where(c => c.Id == configId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(c => c.ImapLastUid, lastUid)
                    .SetProperty(c => c.ImapUidValidity, uidValidity)
                    .SetProperty(c => c.ImapLastPolledAt, DateTime.UtcNow)
                    .SetProperty(c => c.ImapLastError, trimmedError), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the IMAP poll outcome for configuration {ConfigId}.", configId);
        }
    }

    private string? Decrypt(IEncryptionService encryption, string? cipherText, int configId)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return null;

        try
        {
            return encryption.Decrypt(cipherText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not decrypt a mailbox password for email configuration {ConfigId}.", configId);
            return null;
        }
    }
}
