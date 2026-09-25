using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Polls IMAP mailboxes for inbound replies and threads them into conversations.
///
/// <para>
/// For each active SMTP EmailConfiguration that has IMAP settings, this worker:
/// 1. Connects via IMAP
/// 2. Fetches unseen messages
/// 3. Passes each to <see cref="IInboundEmailThreader"/> for threading and persistence
/// 4. Marks processed messages as Seen on the server
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

        // Brief initial delay so the application finishes bootstrapping before we start making
        // IMAP connections — especially relevant during development where the DB migration may
        // still be running.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pollInterval = _options.CurrentValue.Inbound.PollIntervalSeconds;

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
                _logger.LogError(ex, "IMAP polling cycle failed. Will retry in {Seconds}s.", pollInterval);
            }

            await Task.Delay(TimeSpan.FromSeconds(pollInterval), stoppingToken);
        }

        _logger.LogInformation("IMAP polling worker stopped.");
    }

    /// <summary>
    /// One poll cycle: enumerate every SMTP connection with IMAP settings and check each mailbox.
    /// </summary>
    private async Task PollAllMailboxesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        // Find all active SMTP configurations that have IMAP settings
        var configurations = await dbContext.EmailConfigurations
            .AsNoTracking()
            .Where(c => c.IsActive
                      && c.Provider == EmailProviderType.Smtp
                      && c.ImapHost != null
                      && c.ImapHost != "")
            .Select(c => new
            {
                c.Id,
                c.ConnectionId,
                c.ImapHost,
                c.ImapPort,
                c.ImapSecurity,
                c.ImapUsername,
                c.SmtpUsername,
                c.ImapPasswordEncrypted,
                c.SmtpPasswordEncrypted,
                c.DefaultFromEmail
            })
            .ToListAsync(ct);

        if (configurations.Count == 0) return;

        _logger.LogDebug("Polling {Count} IMAP mailbox(es).", configurations.Count);

        foreach (var config in configurations)
        {
            if (ct.IsCancellationRequested) break;
            if (!config.ConnectionId.HasValue) continue;

            try
            {
                // Decrypt credentials — fall back to SMTP credentials when IMAP-specific ones are not set
                var username = !string.IsNullOrWhiteSpace(config.ImapUsername)
                    ? config.ImapUsername
                    : config.SmtpUsername;

                var password = DecryptOrFallback(
                    encryptionService,
                    config.ImapPasswordEncrypted,
                    config.SmtpPasswordEncrypted,
                    config.Id);

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    _logger.LogDebug(
                        "Skipping IMAP poll for config {ConfigId}: no credentials available.", config.Id);
                    continue;
                }

                await PollOneMailboxAsync(
                    config.ImapHost!,
                    config.ImapPort ?? 993,
                    config.ImapSecurity,
                    username,
                    password,
                    config.ConnectionId.Value,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "IMAP poll failed for config {ConfigId} ({Host}). Will retry next cycle.",
                    config.Id, config.ImapHost);
            }
        }
    }

    /// <summary>
    /// Connect to one IMAP mailbox, fetch unseen messages, thread each one, and mark them read.
    /// </summary>
    private async Task PollOneMailboxAsync(
        string host,
        int port,
        SmtpSecurityMode security,
        string username,
        string password,
        int connectionId,
        CancellationToken ct)
    {
        using var client = new ImapClient();
        client.Timeout = 30_000; // 30s timeout
        // cPanel/shared-hosting servers often present a self-signed certificate. Bypass validation
        // so polling does not silently fail on every cycle. The channel is still TLS-encrypted.
        client.ServerCertificateValidationCallback = (s, c, h, e) => true;

        var socketOptions = security switch
        {
            SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
            SmtpSecurityMode.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.SslOnConnect
        };

        await client.ConnectAsync(host, port, socketOptions, ct);
        await client.AuthenticateAsync(username, password, ct);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

        // Search for unseen (unread) messages
        var unseenUids = await inbox.SearchAsync(SearchQuery.NotSeen, ct);

        if (unseenUids.Count == 0)
        {
            await client.DisconnectAsync(quit: true, ct);
            return;
        }

        _logger.LogInformation(
            "Found {Count} unseen message(s) in IMAP mailbox {Host} for connection {ConnectionId}.",
            unseenUids.Count, host, connectionId);

        var maxSize = _options.CurrentValue.Inbound.MaxSizeBytes;
        var loopHeaders = _options.CurrentValue.Inbound.LoopProtectionHeaders;

        foreach (var uid in unseenUids)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var mime = await inbox.GetMessageAsync(uid, ct);

                // Skip messages that exceed the size limit
                if (mime.ToString().Length > maxSize)
                {
                    _logger.LogWarning("Skipping oversized inbound email (UID {Uid}). Subject: {Subject}",
                        uid, mime.Subject);
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, ct);
                    continue;
                }

                // ── Bounce/DSN detection ──────────────────────────────────────────────────────
                // DSN (Delivery Status Notification) reports arrive as regular emails.
                // We process them before regular threading so they don't create a conversation.
                var bounceInfo = ImapBounceDetector.TryParse(mime);
                if (bounceInfo is not null)
                {
                    await ProcessBounceAsync(bounceInfo, connectionId, ct);
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, ct);
                    continue;
                }

                // Skip auto-generated messages (vacation replies, mailing list traffic, etc.)
                if (IsAutoGenerated(mime, loopHeaders))
                {
                    _logger.LogDebug("Skipping auto-generated message (UID {Uid}). Subject: {Subject}",
                        uid, mime.Subject);
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, ct);
                    continue;
                }

                // ── Thread the message into a conversation ────────────────────────────────────
                // Each message gets its own scope so a failure on one does not roll back the others
                using var messageScope = _scopeFactory.CreateScope();
                var threader = messageScope.ServiceProvider.GetRequiredService<IInboundEmailThreader>();

                var result = await threader.ThreadInboundMessageAsync(mime, connectionId, ct);

                if (result is not null)
                {
                    _logger.LogDebug(
                        "Threaded UID {Uid} into conversation {ConversationId} as message {MessageId}.",
                        uid, result.ConversationId, result.Id);

                    // ── Emit REPLIED event to the normalized pipeline ─────────────────────────
                    // Try to match to a campaign recipient via the X-Omni-Recipient-Id header
                    // (set by the dispatch worker in every outbound email's headers).
                    int? campaignContactId = null;
                    int? campaignId = null;

                    var recipientIdHeader = mime.Headers["X-Omni-Recipient-Id"];
                    var campaignIdHeader  = mime.Headers["X-Omni-Campaign-Id"];

                    if (int.TryParse(recipientIdHeader, out var rid)) campaignContactId = rid;
                    if (int.TryParse(campaignIdHeader,  out var cid)) campaignId = cid;

                    // Fallback: match by In-Reply-To against EmailMessageDetail.MessageIdHeader
                    // This is handled by the threader itself; we extract CampaignContactId
                    // from the matched ChatMessage's CampaignContactId foreign key.
                    if (campaignContactId is null && result.CampaignContactId.HasValue)
                    {
                        campaignContactId = result.CampaignContactId.Value;
                    }

                    // Only emit if we can tie it to a campaign
                    if (campaignContactId.HasValue || campaignId.HasValue)
                    {
                        var eventProcessor = messageScope.ServiceProvider
                            .GetRequiredService<ICampaignEmailEventProcessor>();

                        var senderAddress = mime.From.Mailboxes.FirstOrDefault()?.Address;
                        var msgId         = mime.MessageId is not null ? $"<{mime.MessageId}>" : null;
                        var idempotencyKey = $"imap:reply:{msgId ?? uid.ToString()}:{connectionId}";

                        await eventProcessor.ProcessAsync(
                            kind:              EmailEventKind.Replied,
                            idempotencyKey:    idempotencyKey,
                            source:            "Imap",
                            campaignId:        campaignId,
                            campaignContactId: campaignContactId,
                            messageId:         msgId,
                            recipientAddress:  senderAddress,
                            occurredAt:        mime.Date != default ? mime.Date.UtcDateTime : DateTime.UtcNow,
                            ct:                ct);
                    }
                }

                // Mark as read so we don't process it again
                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to process IMAP message UID {Uid} from {Host}. Leaving it as unseen for retry.",
                    uid, host);
                // Don't mark as seen — it will be retried on the next poll
            }
        }

        await client.DisconnectAsync(quit: true, ct);
    }

    /// <summary>
    /// Routes a detected DSN bounce into the normalized email event pipeline.
    /// </summary>
    private async Task ProcessBounceAsync(BounceInfo bounce, int connectionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(bounce.FinalRecipient)) return;

        _logger.LogInformation(
            "IMAP DSN bounce for {Recipient}: {Status} ({BounceType})",
            bounce.FinalRecipient, bounce.StatusCode, bounce.BounceType);

        using var scope = _scopeFactory.CreateScope();
        var eventProcessor = scope.ServiceProvider.GetRequiredService<ICampaignEmailEventProcessor>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Try to find the campaign/recipient this bounce is for via Original-Message-ID
        int? campaignId = null;
        int? campaignContactId = null;

        if (!string.IsNullOrEmpty(bounce.OriginalMessageId))
        {
            var contactRow = await db.EmailMessageDetails
                .AsNoTracking()
                .Where(d => d.MessageIdHeader == bounce.OriginalMessageId)
                .Join(
                    db.ChatMessages.AsNoTracking(),
                    d => d.ChatMessageId,
                    m => m.Id,
                    (d, m) => new { m.CampaignId, m.CampaignContactId })
                .FirstOrDefaultAsync(ct);

            campaignId        = contactRow?.CampaignId;
            campaignContactId = contactRow?.CampaignContactId;
        }

        var idempotencyKey = $"imap:bounce:{bounce.OriginalMessageId ?? bounce.FinalRecipient}:{bounce.ReceivedAt:yyyyMMddHHmm}";

        await eventProcessor.ProcessAsync(
            kind:              EmailEventKind.Bounced,
            idempotencyKey:    idempotencyKey,
            source:            "Imap",
            campaignId:        campaignId,
            campaignContactId: campaignContactId,
            messageId:         bounce.OriginalMessageId,
            recipientAddress:  bounce.FinalRecipient,
            occurredAt:        bounce.ReceivedAt,
            bounceType:        bounce.BounceType,
            bounceSubType:     bounce.StatusCode,
            diagnosticCode:    bounce.DiagnosticText,
            ct: ct);
    }

    /// <summary>
    /// Detects auto-generated messages (vacation replies, delivery status notifications, etc.)
    /// to prevent reply loops.
    /// </summary>
    private static bool IsAutoGenerated(MimeKit.MimeMessage mime, string[] loopHeaders)
    {
        foreach (var headerName in loopHeaders)
        {
            var value = mime.Headers[headerName];
            if (!string.IsNullOrWhiteSpace(value))
            {
                // Auto-Submitted: auto-replied, auto-generated, etc. — anything other than "no"
                if (headerName == "Auto-Submitted" && value.Equals("no", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Precedence: bulk, list, junk
                if (headerName == "Precedence")
                {
                    var lower = value.ToLowerInvariant();
                    if (lower is "bulk" or "list" or "junk") return true;
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Decrypts the IMAP password, falling back to the SMTP password when no separate IMAP
    /// password was configured.
    /// </summary>
    private string? DecryptOrFallback(
        IEncryptionService encryption,
        string? imapPasswordEncrypted,
        string? smtpPasswordEncrypted,
        int configId)
    {
        var cipherText = !string.IsNullOrWhiteSpace(imapPasswordEncrypted)
            ? imapPasswordEncrypted
            : smtpPasswordEncrypted;

        if (string.IsNullOrWhiteSpace(cipherText)) return null;

        try
        {
            return encryption.Decrypt(cipherText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Could not decrypt IMAP/SMTP password for email configuration {ConfigId}.", configId);
            return null;
        }
    }
}
