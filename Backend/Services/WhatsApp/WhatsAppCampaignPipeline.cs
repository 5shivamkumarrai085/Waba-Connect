using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;

namespace WhatsAppCampaignApi.Services.WhatsApp;

/// <summary>WhatsApp campaign throughput and limits (<c>WhatsApp:Dispatch</c>).</summary>
public sealed class WhatsAppDispatchOptions
{
    public const string SectionName = "WhatsApp:Dispatch";

    /// <summary>Concurrent send loops per instance.</summary>
    public int WorkerCount { get; set; } = 4;

    /// <summary>Jobs one loop claims at a time.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Messages per second per connection, shared by every instance through the database token
    /// bucket. Meta's Cloud API default throughput is 80 messages/second; start below it.
    /// </summary>
    public double SendRatePerSecond { get; set; } = 20;

    /// <summary>Fallback daily limit when the phone number has none recorded.</summary>
    public int DefaultDailyLimit { get; set; } = 1000;

    /// <summary>Recipients read per expansion page.</summary>
    public int RecipientPageSize { get; set; } = 1000;
}

/// <summary>Queue payloads for the WhatsApp campaign pipeline.</summary>
public sealed record WhatsAppExpansionJob(int CampaignId, string RunId);
public sealed record WhatsAppSendJob(int CampaignId, int CampaignContactId, string RunId);

/// <summary>
/// Starts (or restarts) delivery of a WhatsApp campaign through the durable job queue.
/// </summary>
public interface IWhatsAppCampaignDispatcher
{
    /// <summary>
    /// Queues an expansion run. <paramref name="notBefore"/> schedules it; null runs immediately.
    /// Scheduled runs use a deterministic key, so the safety-net scheduler and the create path
    /// cannot queue the same scheduled send twice.
    /// </summary>
    Task StartAsync(int campaignId, DateTime? notBefore, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class WhatsAppCampaignDispatcher : IWhatsAppCampaignDispatcher
{
    private readonly IJobQueue _queue;
    private readonly ILogger<WhatsAppCampaignDispatcher> _logger;

    public WhatsAppCampaignDispatcher(IJobQueue queue, ILogger<WhatsAppCampaignDispatcher> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task StartAsync(int campaignId, DateTime? notBefore, CancellationToken ct = default)
    {
        var runId = notBefore is { } at
            ? $"sched-{DateTime.SpecifyKind(at, DateTimeKind.Utc).Ticks}"
            : Guid.NewGuid().ToString("N");

        var result = await _queue.EnqueueAsync(
        [
            new QueueMessage
            {
                QueueName = QueueNames.WhatsAppCampaignExpansion,
                Payload = JsonSerializer.Serialize(new WhatsAppExpansionJob(campaignId, runId)),
                IdempotencyKey = $"wa:expand:{campaignId}:{runId}",
                PartitionKey = campaignId.ToString(),
                AvailableAt = notBefore is { } time ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : null
            }
        ], ct);

        _logger.LogInformation(
            "WhatsApp campaign {CampaignId} queued for delivery (run {RunId}, {Enqueued} new).",
            campaignId, runId, result.Enqueued);
    }
}

/// <summary>
/// Turns a WhatsApp campaign run into one send job per pending recipient.
/// </summary>
public sealed class WhatsAppCampaignExpansionWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _queueOptions;
    private readonly IOptionsMonitor<WhatsAppDispatchOptions> _options;
    private readonly ILogger<WhatsAppCampaignExpansionWorker> _logger;

    public WhatsAppCampaignExpansionWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmailOptions> queueOptions,
        IOptionsMonitor<WhatsAppDispatchOptions> options,
        ILogger<WhatsAppCampaignExpansionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _queueOptions = queueOptions;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{Environment.MachineName}-wa-expand-{Guid.NewGuid().ToString("N")[..4]}";

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var claimed = 0;
                try
                {
                    var leases = await _queue.ClaimAsync(new QueueClaimRequest
                    {
                        QueueName = QueueNames.WhatsAppCampaignExpansion,
                        WorkerId = workerId,
                        BatchSize = 1
                    }, stoppingToken);

                    claimed = leases.Count;
                    foreach (var lease in leases) await ExpandAsync(lease, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WhatsApp expansion worker cycle failed.");
                }

                await Task.Delay(claimed > 0 ? 0 : _queueOptions.CurrentValue.Queue.IdleBackoffMs, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExpandAsync(QueueLease lease, CancellationToken ct)
    {
        var job = JsonSerializer.Deserialize<WhatsAppExpansionJob>(lease.Payload);
        if (job is null)
        {
            await _queue.FailAsync(lease, new QueueFailure("Malformed expansion payload.", IsTransient: false), ct);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var compliance = scope.ServiceProvider.GetRequiredService<Compliance.IComplianceGuard>();

            var campaign = await db.Campaigns
                .IgnoreQueryFilters()
                .Include(c => c.Template)
                .FirstOrDefaultAsync(c => c.Id == job.CampaignId, ct);

            if (campaign is null || campaign.IsDeleted || campaign.Channel != MessageChannel.WhatsApp
                || campaign.Status is not (CampaignStatus.Scheduled or CampaignStatus.Sending))
            {
                // Deleted, cancelled, paused, or already finished since this run was queued.
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            if (campaign.Template is null)
            {
                _logger.LogError("WhatsApp campaign {CampaignId} has no template and cannot be sent.", campaign.Id);
                campaign.Status = CampaignStatus.Failed;
                await db.SaveChangesAsync(ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            if (campaign.Status == CampaignStatus.Scheduled)
            {
                campaign.Status = CampaignStatus.Sending;
                await db.SaveChangesAsync(ct);
            }

            // Segment audiences are re-resolved at send time (see CampaignAudienceRefresher).
            await Segments.CampaignAudienceRefresher.RefreshAsync(
                db, scope.ServiceProvider.GetRequiredService<Segments.ISegmentService>(), campaign, ct);

            await Campaigns.AbTestService.AssignLateJoinersAsync(db, campaign, ct);

            var pageSize = Math.Max(100, _options.CurrentValue.RecipientPageSize);
            var visibility = TimeSpan.FromSeconds(_queueOptions.CurrentValue.Queue.VisibilityTimeoutSeconds);
            var lastId = 0;
            var enqueued = 0;

            while (!ct.IsCancellationRequested)
            {
                // Keyset paging: constant cost per page however large the campaign is.
                var page = await db.CampaignContacts
                    .IgnoreQueryFilters()
                    .Where(cc => cc.CampaignId == campaign.Id && cc.Status == MessageStatus.Pending && !cc.HeldForWinner && cc.Id > lastId)
                    .OrderBy(cc => cc.Id)
                    .Select(cc => new { cc.Id, cc.ContactId, cc.Contact.TimeZone })
                    .Take(pageSize)
                    .ToListAsync(ct);

                if (page.Count == 0) break;
                lastId = page[^1].Id;

                // Compliance: consent, WhatsApp opt-in, the frequency cap, and when each recipient
                // may be sent to. Excluded recipients are Skipped with the reason.
                var decisions = await compliance.EvaluateAsync(
                    campaign,
                    page.Select(r => new Compliance.ComplianceCandidate(r.Id, r.ContactId, r.TimeZone)).ToList(),
                    DateTime.UtcNow,
                    ct);

                var excluded = decisions.Where(d => d.Value.IsExcluded).ToList();
                foreach (var group in excluded.GroupBy(d => d.Value.ExclusionReason!))
                {
                    var skipIds = group.Select(d => d.Key).ToArray();
                    var reason = group.Key.Length > 500 ? group.Key[..500] : group.Key;
                    await db.CampaignContacts.IgnoreQueryFilters()
                        .Where(cc => skipIds.Contains(cc.Id) && cc.Status == MessageStatus.Pending)
                        .ExecuteUpdateAsync(u => u
                            .SetProperty(cc => cc.Status, MessageStatus.Skipped)
                            .SetProperty(cc => cc.ErrorMessage, reason), ct);
                }
                if (excluded.Count > 0) await CampaignFinalizer.ReconcileWhatsAppCountersAsync(db, campaign.Id, ct);

                var ids = page.Where(r => !decisions.TryGetValue(r.Id, out var d) || !d.IsExcluded).Select(r => r.Id).ToList();

                foreach (var chunk in ids.Chunk(200))
                {
                    enqueued += (await _queue.EnqueueAsync(chunk.Select(id => new QueueMessage
                    {
                        QueueName = QueueNames.WhatsAppSend,
                        Payload = JsonSerializer.Serialize(new WhatsAppSendJob(campaign.Id, id, job.RunId)),
                        // Per run: a resumed campaign must be able to queue its still-pending
                        // recipients again. The recipient compare-and-set in the send worker is
                        // what stops anyone receiving the message twice.
                        IdempotencyKey = $"wa:send:{campaign.Id}:{id}:{job.RunId}",
                        PartitionKey = campaign.Id.ToString(),
                        AvailableAt = decisions.TryGetValue(id, out var timing) ? timing.NotBeforeUtc : null
                    }).ToList(), ct)).Enqueued;
                }

                if (!await _queue.ExtendLeaseAsync(lease, visibility, ct))
                {
                    _logger.LogWarning("Lost the expansion lease for WhatsApp campaign {CampaignId}; another worker has it.", campaign.Id);
                    return;
                }

                if (page.Count < pageSize) break;
            }

            // Nothing left to send (every recipient already handled): finish it now, since no
            // send job will ever arrive to do so.
            if (enqueued == 0)
            {
                await CampaignFinalizer.TryFinalizeAsync(db, campaign.Id, ct);
            }

            _logger.LogInformation("WhatsApp campaign {CampaignId} expanded: {Count} send job(s) queued.", campaign.Id, enqueued);
            await _queue.CompleteAsync(lease, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Left leased; reclaimed after the visibility timeout.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Expanding WhatsApp campaign {CampaignId} failed.", job.CampaignId);
            await _queue.FailAsync(lease, new QueueFailure(ex.Message, IsTransient: true), CancellationToken.None);
        }
    }
}

/// <summary>
/// Sends WhatsApp campaign messages, one queued job per recipient.
/// </summary>
/// <remarks>
/// Replaces the in-memory send loop. That loop lived in a fire-and-forget task, so a restart
/// mid-campaign left it stuck in Sending forever; two instances each sent the whole campaign;
/// Meta 429/5xx responses failed recipients outright; and every recipient paid a campaign reload,
/// a daily-limit COUNT and a fixed 100 ms sleep. Here the queue gives durability, retries with
/// backoff and multi-instance safety; the rate is set by a shared token bucket; the daily limit is
/// a counter; and a per-recipient compare-and-set guarantees no one is sent the message twice.
/// </remarks>
public sealed class WhatsAppCampaignSendWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _queueOptions;
    private readonly IOptionsMonitor<WhatsAppDispatchOptions> _options;
    private readonly ILogger<WhatsAppCampaignSendWorker> _logger;

    public WhatsAppCampaignSendWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmailOptions> queueOptions,
        IOptionsMonitor<WhatsAppDispatchOptions> options,
        ILogger<WhatsAppCampaignSendWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _queueOptions = queueOptions;
        _options = options;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = Enumerable.Range(0, Math.Max(1, _options.CurrentValue.WorkerCount))
            .Select(i => RunLoopAsync($"{Environment.MachineName}-wa-send-{i}-{Guid.NewGuid().ToString("N")[..4]}", stoppingToken));
        return Task.WhenAll(loops);
    }

    private async Task RunLoopAsync(string workerId, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var claimed = 0;
                try
                {
                    var leases = await _queue.ClaimAsync(new QueueClaimRequest
                    {
                        QueueName = QueueNames.WhatsAppSend,
                        WorkerId = workerId,
                        BatchSize = Math.Max(1, _options.CurrentValue.BatchSize)
                    }, stoppingToken);

                    claimed = leases.Count;
                    var visibility = TimeSpan.FromSeconds(_queueOptions.CurrentValue.Queue.VisibilityTimeoutSeconds);

                    foreach (var lease in leases)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        if (!await _queue.ExtendLeaseAsync(lease, visibility, stoppingToken)) continue;

                        // The provider call is not cancelled mid-flight: a message on the wire must
                        // be recorded, or a retry would send it again.
                        await ProcessAsync(lease, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WhatsApp send loop {WorkerId} failed a cycle.", workerId);
                }

                await Task.Delay(claimed > 0 ? 0 : _queueOptions.CurrentValue.Queue.IdleBackoffMs, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAsync(QueueLease lease, CancellationToken ct)
    {
        var job = JsonSerializer.Deserialize<WhatsAppSendJob>(lease.Payload);
        if (job is null || job.CampaignContactId <= 0)
        {
            await _queue.FailAsync(lease, new QueueFailure("Malformed send payload.", IsTransient: false), ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var whatsApp = services.GetRequiredService<IWhatsAppService>();
        var chatService = services.GetRequiredService<IChatService>();
        var rateLimiter = services.GetRequiredService<IEmailRateLimiter>();
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        var publisher = services.GetRequiredService<IEventPublisher>();
        var compliance = services.GetRequiredService<Compliance.IComplianceGuard>();
        var options = _options.CurrentValue;

        int? reservedFor = null;

        try
        {
            var campaign = await db.Campaigns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(c => c.Template)
                .Include(c => c.Variables)
                .FirstOrDefaultAsync(c => c.Id == job.CampaignId, ct);

            // Paused, cancelled or deleted mid-run: stop without touching the recipient, so a
            // resume picks it up again.
            if (campaign is null || campaign.IsDeleted || campaign.Status != CampaignStatus.Sending || campaign.Template is null)
            {
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var recipient = await db.CampaignContacts
                .IgnoreQueryFilters()
                .Include(cc => cc.Contact)
                .FirstOrDefaultAsync(cc => cc.Id == job.CampaignContactId, ct);

            if (recipient is null || recipient.Status != MessageStatus.Pending)
            {
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // An attempt stamped with no WhatsApp id means an earlier attempt may have reached
            // Meta before the process died. Sending again could deliver twice.
            if (recipient.SendAttemptedAt is not null && string.IsNullOrEmpty(recipient.WhatsAppMessageId))
            {
                await FailRecipientAsync(db, publisher, campaign.Id, recipient.Id,
                    "A previous send attempt did not complete cleanly. Not retried automatically to avoid sending twice.", ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var contact = recipient.Contact;
            if (contact is null || !contact.IsActive || contact.IsDeleted || string.IsNullOrWhiteSpace(contact.Phone))
            {
                await FailRecipientAsync(db, publisher, campaign.Id, recipient.Id,
                    string.IsNullOrWhiteSpace(contact?.Phone) ? "The contact has no phone number." : "Contact is inactive or deleted.", ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Compliance, re-checked at send time: an opt-out (a STOP reply) can arrive after
            // expansion, and a job can come due inside the recipient's quiet hours.
            var complianceCheck = await compliance.RecheckAsync(campaign, contact.Id, contact.TimeZone, DateTime.UtcNow, ct);
            if (complianceCheck.IsExcluded)
            {
                var reason = complianceCheck.ExclusionReason!;
                await db.CampaignContacts.IgnoreQueryFilters()
                    .Where(cc => cc.Id == recipient.Id && cc.Status == MessageStatus.Pending)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(cc => cc.Status, MessageStatus.Skipped)
                        .SetProperty(cc => cc.ErrorMessage, reason.Length > 500 ? reason[..500] : reason), ct);
                await CampaignFinalizer.ReconcileWhatsAppCountersAsync(db, campaign.Id, ct);
                await CampaignFinalizer.TryFinalizeAsync(db, campaign.Id, ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }
            if (complianceCheck.NotBeforeUtc is { } releaseAt)
            {
                await _queue.DeferAsync(lease, releaseAt - DateTime.UtcNow, "Held for the recipient's quiet hours.", ct);
                return;
            }

            if (campaign.ConnectionId is { } connectionId)
            {
                if (await rateLimiter.TryReserveAsync(connectionId, 1, options.SendRatePerSecond, ct) == 0)
                {
                    await _queue.DeferAsync(lease, TimeSpan.FromMilliseconds(250), "Rate limited; waiting for send capacity.", ct);
                    return;
                }

                reservedFor = connectionId;

                var (allowed, used, limit) = await ReserveDailySlotAsync(db, dataSource, connectionId, options.DefaultDailyLimit, ct);
                if (!allowed)
                {
                    await rateLimiter.ReleaseAsync(connectionId, 1, ct);
                    reservedFor = null;
                    await FailRecipientAsync(db, publisher, campaign.Id, recipient.Id,
                        $"Daily message limit reached ({used}/{limit}) for this connection.", ct);
                    await _queue.CompleteAsync(lease, ct);
                    return;
                }
            }

            // Claim the recipient: only the worker whose UPDATE matches may send.
            var attemptAt = DateTime.UtcNow;
            var claimed = await db.CampaignContacts
                .IgnoreQueryFilters()
                .Where(cc => cc.Id == recipient.Id && cc.Status == MessageStatus.Pending && cc.SendAttemptedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.SendAttemptedAt, attemptAt), ct);

            if (claimed == 0)
            {
                if (reservedFor is { } r) await rateLimiter.ReleaseAsync(r, 1, ct);
                await _queue.DeferAsync(lease, TimeSpan.FromSeconds(60), "Another worker is sending this recipient.", ct);
                return;
            }

            var attemptEntry = db.Entry(recipient).Property(cc => cc.SendAttemptedAt);
            attemptEntry.CurrentValue = attemptAt;
            attemptEntry.OriginalValue = attemptAt;

            await SendAsync(db, whatsApp, chatService, publisher, campaign, recipient, lease, ct);
            reservedFor = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "WhatsApp send job {JobId} failed unexpectedly.", lease.JobId);
            if (reservedFor is { } connectionId)
            {
                try { await rateLimiter.ReleaseAsync(connectionId, 1, CancellationToken.None); }
                catch (Exception releaseEx) { _logger.LogWarning(releaseEx, "Could not return a rate-limit token."); }
            }

            // Clear the attempt marker only if nothing was sent (no message id), so the retry may send.
            await db.CampaignContacts.IgnoreQueryFilters()
                .Where(cc => cc.Id == job.CampaignContactId && cc.Status == MessageStatus.Pending && cc.WhatsAppMessageId == null)
                .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.SendAttemptedAt, (DateTime?)null), CancellationToken.None);

            await _queue.FailAsync(lease, new QueueFailure(ex.Message, IsTransient: true), CancellationToken.None);
        }
    }

    private async Task SendAsync(
        AppDbContext db,
        IWhatsAppService whatsApp,
        IChatService chatService,
        IEventPublisher publisher,
        Campaign campaign,
        CampaignContact recipient,
        QueueLease lease,
        CancellationToken ct)
    {
        // A/B tests: the recipient's variant decides the template.
        var template = campaign.Template!;
        if (recipient.VariantId is { } variantId)
        {
            var variantTemplate = await db.CampaignVariants.AsNoTracking()
                .Where(v => v.Id == variantId && v.TemplateId != null)
                .Select(v => v.Template)
                .FirstOrDefaultAsync(ct);
            if (variantTemplate is not null) template = variantTemplate;
        }
        var contact = recipient.Contact;

        // Merge fields resolved per recipient, the same convention as before.
        var messageVars = new Dictionary<string, string>();
        foreach (var v in campaign.Variables)
        {
            var value = v.VariableValue ?? string.Empty;
            if (v.MergeField == "@name") value = contact.Name;
            else if (v.MergeField == "@phone") value = contact.Phone;
            messageVars[v.VariableName] = value;
        }

        var attachmentUrl = campaign.FileUrl;
        string? mediaType = null;
        string? mediaFileName = null;
        if (!string.IsNullOrEmpty(attachmentUrl))
        {
            mediaType = CampaignService.GetMediaTypeFromUrl(attachmentUrl);
            mediaFileName = campaign.FileName ?? Path.GetFileName(attachmentUrl);
        }

        var previewText = CampaignService.BuildRecipientMessagePreview(campaign, recipient);
        var hasMediaHeader = template.HeaderType != HeaderType.None;

        var chatMessage = await chatService.CreateOrUpdateCampaignMessageAsync(
            campaign, recipient, previewText,
            hasMediaHeader ? attachmentUrl : null,
            hasMediaHeader ? mediaType : null,
            hasMediaHeader ? mediaFileName : null);

        var result = await whatsApp.SendTemplateMessageWithResultAsync(
            contact.Phone,
            template.Name,
            template.Language,
            messageVars,
            campaign.ConnectionId);

        if (result.Success && !string.IsNullOrWhiteSpace(result.MessageId))
        {
            // Recorded with no cancellation: the message is already with Meta.
            recipient.WhatsAppMessageId = result.MessageId;
            recipient.Status = MessageStatus.Sent;
            recipient.SentAt ??= DateTime.UtcNow;
            recipient.ErrorMessage = null;
            await db.SaveChangesAsync(CancellationToken.None);

            await chatService.MarkCampaignMessageSentAsync(chatMessage.Id, result.MessageId);
            await IncrementAsync(db, campaign.Id, sent: 1, failed: 0);

            // A template without a media header still carries the campaign attachment: it goes as
            // a separate media message, exactly as before.
            if (!hasMediaHeader && !string.IsNullOrEmpty(attachmentUrl))
            {
                await SendSeparateAttachmentAsync(db, whatsApp, chatService, campaign, recipient, attachmentUrl, mediaType!, mediaFileName);
            }

            await publisher.PublishEmailEventAsync(new CampaignEmailEventNotification(
                campaign.Id, EmailEventKind.Sent, null, null, DateTime.UtcNow, SentDelta: 1,
                NewCampaignStatus: await CampaignFinalizer.TryFinalizeAsync(db, campaign.Id, CancellationToken.None)));

            await _queue.CompleteAsync(lease, CancellationToken.None);
            return;
        }

        var error = result.ErrorMessage ?? "Failed to send via WhatsApp Cloud API";

        // 429 (throughput / spam limits) and 5xx are Meta telling us to come back later, and a
        // missing status is a network failure. Those retry with backoff; anything else is final.
        var transient = result.HttpStatusCode is null or 429 or >= 500;
        if (transient && !lease.IsFinalAttempt)
        {
            await db.CampaignContacts.IgnoreQueryFilters()
                .Where(cc => cc.Id == recipient.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.SendAttemptedAt, (DateTime?)null), CancellationToken.None);

            await _queue.FailAsync(lease, new QueueFailure(error, IsTransient: true,
                RetryAfter: result.HttpStatusCode == 429 ? TimeSpan.FromSeconds(30) : null), CancellationToken.None);
            return;
        }

        await chatService.MarkCampaignMessageFailedAsync(chatMessage.Id, error);
        await FailRecipientAsync(db, publisher, campaign.Id, recipient.Id, error, CancellationToken.None);
        await _queue.CompleteAsync(lease, CancellationToken.None);
    }

    private async Task SendSeparateAttachmentAsync(
        AppDbContext db,
        IWhatsAppService whatsApp,
        IChatService chatService,
        Campaign campaign,
        CampaignContact recipient,
        string attachmentUrl,
        string mediaType,
        string? mediaFileName)
    {
        try
        {
            string? phoneNumberId = null;
            if (campaign.ConnectionId.HasValue)
            {
                phoneNumberId = await db.WabaPhoneNumbers.AsNoTracking()
                    .Where(p => p.ConnectionId == campaign.ConnectionId.Value)
                    .Select(p => p.PhoneNumberId)
                    .FirstOrDefaultAsync();
            }

            var mediaResult = await whatsApp.SendMediaMessageAsync(
                recipient.Contact.Phone, attachmentUrl, mediaType, mediaFileName, null, phoneNumberId);

            if (!mediaResult.Success) return;

            var conversation = await chatService.GetOrCreateConversationAsync(recipient.ContactId, campaign.ConnectionId);
            db.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversation.Id,
                ContactId = recipient.ContactId,
                ConnectionId = campaign.ConnectionId,
                Direction = ChatMessageDirection.Outgoing,
                Status = ChatMessageStatus.Sent,
                Text = $"[Attachment: {mediaFileName}]",
                IsTemplate = false,
                MediaUrl = attachmentUrl,
                MediaType = mediaType,
                MediaFileName = mediaFileName,
                WhatsAppMessageId = mediaResult.MessageId
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The template itself was delivered; a failed follow-up attachment must not undo that.
            _logger.LogWarning(ex, "Separate attachment for campaign {CampaignId} recipient {RecipientId} failed.",
                campaign.Id, recipient.Id);
        }
    }

    private static async Task FailRecipientAsync(
        AppDbContext db, IEventPublisher publisher, int campaignId, int recipientId, string reason, CancellationToken ct)
    {
        var message = reason.Length > 500 ? reason[..500] : reason;
        var rows = await db.CampaignContacts
            .IgnoreQueryFilters()
            .Where(cc => cc.Id == recipientId && cc.Status == MessageStatus.Pending)
            .ExecuteUpdateAsync(u => u
                .SetProperty(cc => cc.Status, MessageStatus.Failed)
                .SetProperty(cc => cc.ErrorMessage, message)
                .SetProperty(cc => cc.SendAttemptedAt, (DateTime?)null), ct);

        if (rows == 0) return;

        await IncrementAsync(db, campaignId, sent: 0, failed: 1);
        await publisher.PublishEmailEventAsync(new CampaignEmailEventNotification(
            campaignId, EmailEventKind.Failed, recipientId, null, DateTime.UtcNow, FailedDelta: 1,
            NewCampaignStatus: await CampaignFinalizer.TryFinalizeAsync(db, campaignId, ct)));
    }

    private static Task IncrementAsync(AppDbContext db, int campaignId, int sent, int failed) =>
        db.Campaigns.IgnoreQueryFilters()
            .Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.SentCount, c => c.SentCount + sent)
                .SetProperty(c => c.FailedCount, c => c.FailedCount + failed)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), CancellationToken.None);

    /// <summary>
    /// Reserves one of today's sends for a connection. The counter is seeded from the day's actual
    /// messages the first time it is touched each day, so sends made outside campaigns still count.
    /// </summary>
    private static async Task<(bool Allowed, int Used, int Limit)> ReserveDailySlotAsync(
        AppDbContext db, NpgsqlDataSource dataSource, int connectionId, int defaultLimit, CancellationToken ct)
    {
        var limitText = await db.WabaPhoneNumbers.AsNoTracking()
            .Where(p => p.ConnectionId == connectionId)
            .Select(p => p.MessageLimit)
            .FirstOrDefaultAsync(ct);

        var limit = int.TryParse(limitText, out var custom) && custom > 0 ? custom : defaultLimit;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayStart = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await using (var update = new NpgsqlCommand("""
            UPDATE "ConnectionDailySendCounters" SET "Count" = "Count" + 1
             WHERE "ConnectionId" = @c AND "Day" = @d
            RETURNING "Count"
            """, connection))
        {
            update.Parameters.AddWithValue("c", connectionId);
            update.Parameters.AddWithValue("d", today);
            if (await update.ExecuteScalarAsync(ct) is int used)
            {
                return await SettleAsync(connection, connectionId, today, used, limit, ct);
            }
        }

        // First send of the day on this connection: seed from what has already gone out today.
        await using var insert = new NpgsqlCommand("""
            INSERT INTO "ConnectionDailySendCounters" ("ConnectionId", "Day", "Count")
            SELECT @c, @d, 1 + count(*)
              FROM "ChatMessages"
             WHERE "ConnectionId" = @c AND "Direction" = 'Outgoing'
               AND ("IsTemplate" OR "CampaignContactId" IS NOT NULL)
               AND "CreatedAt" >= @start
            ON CONFLICT ("ConnectionId", "Day") DO UPDATE SET "Count" = "ConnectionDailySendCounters"."Count" + 1
            RETURNING "Count"
            """, connection);
        insert.Parameters.AddWithValue("c", connectionId);
        insert.Parameters.AddWithValue("d", today);
        insert.Parameters.AddWithValue("start", dayStart);

        var seeded = Convert.ToInt32(await insert.ExecuteScalarAsync(ct));
        return await SettleAsync(connection, connectionId, today, seeded, limit, ct);
    }

    private static async Task<(bool Allowed, int Used, int Limit)> SettleAsync(
        NpgsqlConnection connection, int connectionId, DateOnly day, int used, int limit, CancellationToken ct)
    {
        if (used <= limit) return (true, used, limit);

        // Over the limit: give the slot back so the figure stays the true number sent.
        await using var undo = new NpgsqlCommand("""
            UPDATE "ConnectionDailySendCounters" SET "Count" = "Count" - 1
             WHERE "ConnectionId" = @c AND "Day" = @d
            """, connection);
        undo.Parameters.AddWithValue("c", connectionId);
        undo.Parameters.AddWithValue("d", day);
        await undo.ExecuteNonQueryAsync(ct);

        return (false, used - 1, limit);
    }
}
