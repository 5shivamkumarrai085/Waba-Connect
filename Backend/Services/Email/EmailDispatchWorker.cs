using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Storage;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Sends the queued emails.
///
/// <para>
/// Runs several concurrent loops per instance, and any number of instances. The real send rate is
/// bounded by the cross-instance token bucket rather than by loop count, so adding workers adds
/// throughput up to the provider's configured rate and no further — which is what stops horizontal
/// scaling from becoming an accidental way to get a mail account throttled.
/// </para>
/// </summary>
public class EmailDispatchWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<EmailDispatchWorker> _logger;

    public EmailDispatchWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmailOptions> options,
        ILogger<EmailDispatchWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Max(1, _options.CurrentValue.Dispatch.WorkerCount);

        _logger.LogInformation("Email dispatch starting {WorkerCount} worker loop(s).", workerCount);

        // Independent loops rather than one loop with internal parallelism: each claims and
        // completes its own leases, so a slow provider call blocks only its own loop.
        var loops = Enumerable.Range(0, workerCount)
            .Select(index => RunLoopAsync(
                $"{Environment.MachineName}-send-{index}-{Guid.NewGuid().ToString("N")[..4]}",
                stoppingToken))
            .ToArray();

        await Task.WhenAll(loops);

        _logger.LogInformation("Email dispatch stopped.");
    }

    private async Task RunLoopAsync(string workerId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;

            try
            {
                claimed = await ClaimAndSendAsync(workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email dispatch loop {WorkerId} failed a cycle.", workerId);
            }

            try
            {
                var delay = claimed > 0
                    ? _options.CurrentValue.Queue.PollIntervalMs
                    : _options.CurrentValue.Queue.IdleBackoffMs;

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<int> ClaimAndSendAsync(string workerId, CancellationToken ct)
    {
        var batchSize = Math.Max(1, _options.CurrentValue.Dispatch.BatchSize);

        var leases = await _queue.ClaimAsync(new QueueClaimRequest
        {
            QueueName = QueueNames.EmailSend,
            WorkerId = workerId,
            BatchSize = batchSize
        }, ct);

        if (leases.Count == 0) return 0;

        var visibility = TimeSpan.FromSeconds(_options.CurrentValue.Queue.VisibilityTimeoutSeconds);

        foreach (var lease in leases)
        {
            if (ct.IsCancellationRequested) break;

            // Jobs in a batch run one after another, so the last one can wait for the others'
            // provider calls. Renewing each lease just before its turn stops it expiring in the
            // queue and being claimed (and sent) by a second worker. A lease that has already
            // lapsed belongs to someone else now, so it is left alone.
            if (!await _queue.ExtendLeaseAsync(lease, visibility, ct))
            {
                _logger.LogWarning("Lease on job {JobId} lapsed before it was processed; skipping it.", lease.JobId);
                continue;
            }

            // Not passing ct into the send itself: a shutdown mid-send would otherwise abandon a
            // message whose fate is unknown. Each send is allowed to finish, and the loop stops
            // afterwards.
            await ProcessOneAsync(lease, ct);
        }

        return leases.Count;
    }

    private async Task ProcessOneAsync(QueueLease lease, CancellationToken ct)
    {
        EmailSendJob? job;

        try
        {
            job = JsonSerializer.Deserialize<EmailSendJob>(lease.Payload);
        }
        catch (JsonException ex)
        {
            await _queue.FailAsync(lease, new QueueFailure($"Malformed payload: {ex.Message}", IsTransient: false), ct);
            return;
        }

        if (job is null || job.CampaignContactId <= 0)
        {
            await _queue.FailAsync(lease, new QueueFailure("Payload has no recipient id.", IsTransient: false), ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<AppDbContext>();
        var rateLimiter = services.GetRequiredService<IEmailRateLimiter>();
        var suppression = services.GetRequiredService<IEmailSuppressionService>();
        var providerFactory = services.GetRequiredService<IEmailProviderFactory>();
        var mimeBuilder = services.GetRequiredService<IMimeMessageBuilder>();
        var unsubscribe = services.GetRequiredService<IUnsubscribeTokenService>();
        var recorder = services.GetRequiredService<IEmailSendRecorder>();
        var trackingService = services.GetRequiredService<IEmailTrackingService>();
        var eventProcessor = services.GetRequiredService<ICampaignEmailEventProcessor>();
        var fileStorage = services.GetRequiredService<IFileStorage>();
        var compliance = services.GetRequiredService<Compliance.IComplianceGuard>();
        var attachmentCache = services.GetRequiredService<IMemoryCache>();

        // Declared out here so the catch blocks below can report against the right recipient and
        // give back a token they may have reserved. Inside the try they were invisible to the
        // handlers, which is how a failure came to leave no trace anywhere an operator looks.
        CampaignContact? failingRecipient = null;
        int? reservedForConnection = null;

        try
        {
            var context = await LoadSendContextAsync(dbContext, job, ct);

            if (context is null)
            {
                // The campaign or recipient is gone, or already handled. Nothing wrong, so this
                // completes rather than dead-lettering.
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var (campaign, recipient, detail, template, subjectOverride) = context.Value;
            failingRecipient = recipient;

            // Expansion assigns tracking ids before it enqueues, so this is only a fallback for
            // jobs enqueued by an older build. Compare-and-set, so it can never replace an id that
            // is already in a sent message's pixel and links.
            if (string.IsNullOrEmpty(recipient.TrackingId))
            {
                var candidate = trackingService.GenerateTrackingId();
                var targetId = recipient.Id;
                await dbContext.CampaignContacts.IgnoreQueryFilters()
                    .Where(cc => cc.Id == targetId && cc.TrackingId == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.TrackingId, candidate), ct);

                var stored = await dbContext.CampaignContacts.IgnoreQueryFilters().AsNoTracking()
                    .Where(cc => cc.Id == targetId)
                    .Select(cc => cc.TrackingId)
                    .FirstOrDefaultAsync(ct);

                var entry = dbContext.Entry(recipient).Property(cc => cc.TrackingId);
                entry.CurrentValue = stored;
                entry.OriginalValue = stored;
            }

            // The idempotency guard. A redelivered job — after a lease lapsed, or a crash between
            // sending and committing — must not send again. Anything other than Pending means
            // this recipient has already been dealt with.
            if (recipient.Status != MessageStatus.Pending)
            {
                _logger.LogDebug(
                    "Recipient {RecipientId} is already {Status}; skipping a duplicate send.",
                    recipient.Id, recipient.Status);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // A send attempt stamped with no provider id means a previous attempt may have
            // reached the provider before the process died. Sending again could duplicate real
            // mail, so it is escalated rather than retried: a possible duplicate is worse than a
            // message an operator has to re-send deliberately.
            if (recipient.SendAttemptedAt is not null && string.IsNullOrEmpty(recipient.ProviderMessageId))
            {
                _logger.LogWarning(
                    "Recipient {RecipientId} has an in-flight send attempt from {AttemptedAt} with no provider id. "
                  + "Not re-sending, to avoid a duplicate.",
                    recipient.Id, recipient.SendAttemptedAt);

                await FailRecipientAsync(dbContext, eventProcessor, recipient,
                    "A previous send attempt did not complete cleanly. Not retried automatically to avoid "
                  + "sending twice — re-send this recipient manually if nothing arrived.", ct);

                await _queue.CompleteAsync(lease, ct);
                return;
            }

            if (campaign.Status is CampaignStatus.Cancelled or CampaignStatus.Paused)
            {
                // Mid-flight cancellation. The job is completed and the recipient left Pending,
                // so resuming a paused campaign can pick it up again.
                _logger.LogDebug("Campaign {CampaignId} is {Status}; not sending.", campaign.Id, campaign.Status);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var emailAddress = recipient.Contact.Email;
            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                await FailRecipientAsync(dbContext, eventProcessor, recipient, "The contact has no email address.", ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Second suppression check. The first was at expansion, and a complaint or a bounce
            // can arrive in the minutes between — which for a large campaign is routine, not
            // theoretical.
            if (await suppression.IsSuppressedAsync(emailAddress, campaign.ConnectionId, ct))
            {
                recipient.Status = MessageStatus.Suppressed;
                recipient.ErrorMessage = "The address was suppressed before this message was sent.";
                await dbContext.SaveChangesAsync(ct);

                var suppressedCampaignId = campaign.Id;
                await dbContext.Campaigns.IgnoreQueryFilters()
                    .Where(c => c.Id == suppressedCampaignId)
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.SuppressedCount, c => c.SuppressedCount + 1), ct);

                await CampaignFinalizer.TryFinalizeAsync(dbContext, campaign.Id, ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Compliance, re-checked at send time: an opt-out can arrive after expansion, and a job
            // can come due inside the recipient's quiet hours. Held jobs are deferred without
            // spending an attempt.
            var complianceCheck = await compliance.RecheckAsync(campaign, recipient.ContactId, recipient.Contact.TimeZone, DateTime.UtcNow, ct);
            if (complianceCheck.IsExcluded)
            {
                await SkipRecipientAsync(dbContext, recipient, campaign.Id, complianceCheck.ExclusionReason!, ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }
            if (complianceCheck.NotBeforeUtc is { } releaseAt)
            {
                await _queue.DeferAsync(lease, releaseAt - DateTime.UtcNow, "Held for the recipient's quiet hours.", ct);
                return;
            }

            if (campaign.ConnectionId is not { } connectionId)
            {
                await FailRecipientAsync(dbContext, eventProcessor, recipient, "The campaign has no email connection.", ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Rate limiting. Returning the job to the queue rather than sleeping keeps the worker
            // free for another campaign's traffic, and lets a differently-limited connection make
            // progress meanwhile.
            var granted = await rateLimiter.TryReserveAsync(connectionId, 1, ct);
            if (granted > 0) reservedForConnection = connectionId;

            if (granted == 0)
            {
                // Deferred, not failed: waiting for capacity is not an attempt. Failing here spent
                // the job's retries on queueing, so any campaign that outran its send rate had
                // recipients dead-lettered and left Pending for good.
                await _queue.DeferAsync(lease, TimeSpan.FromSeconds(1), "Rate limited; waiting for send capacity.", ct);
                return;
            }

            var (provider, providerContext) = await providerFactory.ResolveAsync(connectionId, ct);

            var attachments = await LoadAttachmentsAsync(campaign, detail, fileStorage, attachmentCache, ct);

            var message = BuildMessage(
                campaign, recipient, detail, template, subjectOverride, emailAddress!, attachments,
                providerContext, mimeBuilder, unsubscribe, trackingService, out var buildFailure);

            if (message is null)
            {
                await rateLimiter.ReleaseAsync(connectionId, 1, ct);
                reservedForConnection = null;

                var reason = buildFailure ?? "The message could not be rendered.";
                // The reason is stored on the recipient, where the campaign's Executed list shows it.
                await FailRecipientAsync(dbContext, eventProcessor, recipient, reason, ct);

                // Completed, not failed: retrying cannot help — the template needs a value it
                // does not have, and only an operator can supply it.
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Stamped before the provider call and committed immediately, so a crash in the next
            // few milliseconds is detectable as "possibly sent" by the guard above. SMTP offers no
            // idempotent send, so this marker is the only protection there is.
            //
            // A compare-and-set rather than a tracked save: two workers that both loaded this
            // recipient as Pending cannot both send. Only the one whose UPDATE matched goes on.
            var attemptAt = DateTime.UtcNow;
            var recipientId = recipient.Id;
            var claimed = await dbContext.CampaignContacts.IgnoreQueryFilters()
                .Where(cc => cc.Id == recipientId && cc.Status == MessageStatus.Pending && cc.SendAttemptedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.SendAttemptedAt, attemptAt), ct);

            if (claimed == 0)
            {
                await rateLimiter.ReleaseAsync(connectionId, 1, ct);
                reservedForConnection = null;

                // Another worker is sending this recipient right now. Look again shortly: by then
                // it is Sent (complete) or its attempt marker is stale (the guard above decides).
                await _queue.DeferAsync(lease, TimeSpan.FromSeconds(60), "Another worker is sending this recipient.", ct);
                return;
            }

            var attemptEntry = dbContext.Entry(recipient).Property(cc => cc.SendAttemptedAt);
            attemptEntry.CurrentValue = attemptAt;
            attemptEntry.OriginalValue = attemptAt;

            var result = await provider.SendAsync(message, providerContext, CancellationToken.None);

            if (result.Success)
            {
                // CancellationToken.None from here on, deliberately. The message is already on
                // the wire: cancelling the write that records it — because the host happens to be
                // shutting down — would lose all evidence the send happened, and the job would
                // then be redelivered and send the same mail again. Recording an outcome that has
                // already occurred is not a cancellable operation.
                // The Sent event this records also finalises the campaign when this was the last
                // pending recipient.
                await recorder.RecordSentAsync(
                    campaign, recipient, message, result, provider.ProviderName, CancellationToken.None);

                reservedForConnection = null;
                await _queue.CompleteAsync(lease, CancellationToken.None);
                return;
            }

            // The provider decided whether this is worth retrying. A transient failure returns
            // the token — the send never happened, so it should not count against the rate.
            await rateLimiter.ReleaseAsync(connectionId, 1, CancellationToken.None);
            reservedForConnection = null;

            if (result.IsTransient && !lease.IsFinalAttempt)
            {
                // Left Pending so a retry can send it. The attempt marker is cleared, because the
                // provider told us it did not accept the message — so there is no in-flight send
                // to be cautious about. Also uncancellable: leaving the marker set would make the
                // next attempt treat this as a possible duplicate and refuse to send at all.
                recipient.SendAttemptedAt = null;
                await dbContext.SaveChangesAsync(CancellationToken.None);

                await _queue.FailAsync(lease, new QueueFailure(
                    result.ErrorMessage ?? "Transient send failure.",
                    IsTransient: true,
                    result.RetryAfter), CancellationToken.None);
                return;
            }

            await recorder.RecordFailedAsync(
                campaign, recipient, message, result, provider.ProviderName, CancellationToken.None);

            await _queue.FailAsync(lease, new QueueFailure(
                result.ErrorMessage ?? "Send failed.", result.IsTransient), CancellationToken.None);
        }
        catch (KeyNotFoundException ex)
        {
            // A missing connection or configuration. Permanent — the operator has to fix it.
            _logger.LogError(ex, "Email send job {JobId} has an unusable configuration.", lease.JobId);
            await AbandonAsync(dbContext, rateLimiter, recorder, eventProcessor, lease, failingRecipient,
                reservedForConnection, ex.Message, isTransient: false);
        }
        catch (EmailConnectionUnavailableException ex)
        {
            // The connection itself is unusable (unreadable credential, switched off). No
            // recipient did anything wrong, so none is failed: the campaign is put on hold with
            // the reason, every recipient stays Pending, and Resume carries on once the
            // connection is fixed.
            _logger.LogError(ex, "Email send job {JobId}: the connection is unavailable; holding the campaign.", lease.JobId);
            await HoldCampaignAsync(dbContext, rateLimiter, services, lease, failingRecipient, reservedForConnection, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // A disconnected connection.
            _logger.LogError(ex, "Email send job {JobId} cannot proceed.", lease.JobId);
            await AbandonAsync(dbContext, rateLimiter, recorder, eventProcessor, lease, failingRecipient,
                reservedForConnection, ex.Message, isTransient: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure on email send job {JobId}.", lease.JobId);
            await AbandonAsync(dbContext, rateLimiter, recorder, eventProcessor, lease, failingRecipient,
                reservedForConnection, ex.Message, isTransient: true);
        }
    }

    /// <summary>
    /// Puts the campaign on hold because its connection cannot be used, keeping the recipient
    /// Pending. The job completes (like any job of a paused campaign); Resume queues a new run.
    /// </summary>
    private async Task HoldCampaignAsync(
        AppDbContext dbContext,
        IEmailRateLimiter rateLimiter,
        IServiceProvider services,
        QueueLease lease,
        CampaignContact? recipient,
        int? reservedForConnection,
        string reason)
    {
        if (reservedForConnection is { } connectionId)
        {
            try { await rateLimiter.ReleaseAsync(connectionId, 1, CancellationToken.None); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not return the rate-limit token for connection {ConnectionId}.", connectionId); }
        }

        if (recipient is not null)
        {
            var campaignId = recipient.CampaignId;
            var recipientId = recipient.Id;
            var holdReason = $"On hold: {reason}";
            if (holdReason.Length > Campaign.PausedReasonMaxLength) holdReason = holdReason[..Campaign.PausedReasonMaxLength];

            await dbContext.CampaignContacts.IgnoreQueryFilters()
                .Where(cc => cc.Id == recipientId && cc.Status == MessageStatus.Pending)
                .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.SendAttemptedAt, (DateTime?)null), CancellationToken.None);

            // Only the first job to notice changes the status; the rest see Paused and stop.
            var held = await dbContext.Campaigns.IgnoreQueryFilters()
                .Where(c => c.Id == campaignId && c.Status == CampaignStatus.Sending)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(c => c.Status, CampaignStatus.Paused)
                    .SetProperty(c => c.PausedReason, holdReason), CancellationToken.None);

            if (held > 0)
            {
                await services.GetRequiredService<Interfaces.IAuditService>().LogAsync(
                    "Campaign.Held", "Data",
                    $"Campaign {campaignId} was put on hold: {reason}",
                    "Campaign", campaignId.ToString());

                try
                {
                    await services.GetRequiredService<IEventPublisher>().PublishEmailEventAsync(new CampaignEmailEventNotification(
                        CampaignId: campaignId,
                        Kind: EmailEventKind.Sent,
                        CampaignContactId: null,
                        RecipientAddress: null,
                        OccurredAt: DateTime.UtcNow,
                        NewCampaignStatus: nameof(CampaignStatus.Paused)));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not publish the hold of campaign {CampaignId}.", campaignId);
                }
            }
        }

        await _queue.CompleteAsync(lease, CancellationToken.None);
    }

    /// <summary>
    /// Ends a job that threw, leaving the same trail a provider rejection would.
    ///
    /// <para>
    /// Previously these paths only dead-lettered the queue row. The recipient stayed
    /// <see cref="MessageStatus.Pending"/> with no error, nothing reached the activity log, and
    /// the reserved rate-limit token was never given back — so the campaign looked like it had
    /// simply not got round to that recipient yet, and the connection quietly lost send capacity
    /// with every failure.
    /// </para>
    /// <para>
    /// A transient failure leaves the recipient Pending on purpose: the retry has to be able to
    /// pick it up. Only a permanent one marks it Failed.
    /// </para>
    /// </summary>
    private async Task AbandonAsync(
        AppDbContext dbContext,
        IEmailRateLimiter rateLimiter,
        IEmailSendRecorder recorder,
        ICampaignEmailEventProcessor eventProcessor,
        QueueLease lease,
        CampaignContact? recipient,
        int? reservedForConnection,
        string reason,
        bool isTransient)
    {
        // CancellationToken.None throughout: this runs while the host may already be shutting
        // down, and an unrecorded failure is exactly the state being fixed here.
        if (reservedForConnection is { } connectionId)
        {
            try
            {
                await rateLimiter.ReleaseAsync(connectionId, 1, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Never let the cleanup replace the real error with a less useful one.
                _logger.LogWarning(ex, "Could not return the rate-limit token for connection {ConnectionId}.",
                    connectionId);
            }
        }

        if (recipient is not null && !isTransient)
        {
            try
            {
                await FailRecipientAsync(dbContext, eventProcessor, recipient, reason, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not record the failure for recipient {RecipientId}.", recipient.Id);
            }
        }

        await _queue.FailAsync(lease, new QueueFailure(reason, isTransient), CancellationToken.None);
    }

    private static async Task<(Campaign Campaign, CampaignContact Recipient, EmailCampaignDetail Detail, EmailTemplate Template, string? SubjectOverride)?>
        LoadSendContextAsync(AppDbContext dbContext, EmailSendJob job, CancellationToken ct)
    {
        var recipient = await dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Include(cc => cc.Contact)
            .FirstOrDefaultAsync(cc => cc.Id == job.CampaignContactId, ct);

        if (recipient?.Contact is null) return null;

        var campaign = await dbContext.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.EmailTemplate)
            .Include(c => c.EmailDetail)!
                .ThenInclude(d => d!.SenderIdentity)
            .Include(c => c.Variables)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == job.CampaignId, ct);

        if (campaign is null || campaign.IsDeleted) return null;
        if (campaign.EmailDetail is null || campaign.EmailTemplate is null) return null;

        // A/B tests: the recipient's variant may swap the template and the subject. Read without
        // tracking, so nothing about the variant can be written back onto the campaign.
        if (recipient.VariantId is { } variantId)
        {
            var variant = await dbContext.CampaignVariants.AsNoTracking()
                .Include(v => v.EmailTemplate)
                .FirstOrDefaultAsync(v => v.Id == variantId, ct);
            if (variant?.EmailTemplate is not null)
            {
                return (campaign, recipient, campaign.EmailDetail, variant.EmailTemplate, variant.SubjectOverride ?? campaign.EmailDetail.SubjectOverride);
            }
        }

        return (campaign, recipient, campaign.EmailDetail, campaign.EmailTemplate, campaign.EmailDetail.SubjectOverride);
    }

    /// <summary>
    /// Renders the template for one recipient.
    /// </summary>
    /// <returns>
    /// The message, or null with <paramref name="failureReason"/> set when this recipient cannot
    /// be sent to. The reason is returned rather than logged so the caller can put it on the
    /// recipient row, where an operator will actually see it.
    /// </returns>
    private EmailMessage? BuildMessage(
        Campaign campaign,
        CampaignContact recipient,
        EmailCampaignDetail detail,
        EmailTemplate template,
        string? subjectOverride,
        string emailAddress,
        IReadOnlyList<EmailAttachment> attachments,
        EmailProviderContext providerContext,
        IMimeMessageBuilder mimeBuilder,
        IUnsubscribeTokenService unsubscribe,
        IEmailTrackingService trackingService,
        out string? failureReason)
    {
        failureReason = null;

        var sender = detail.SenderIdentity;
        if (sender is null)
        {
            failureReason = "The message could not be rendered — the sender identity is missing.";
            return null;
        }

        var contact = recipient.Contact;

        // Contact fields first, then the campaign's own variables — so a campaign variable can
        // deliberately override a contact field, which is how "use this company name for everyone
        // in this send" is expressed.
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = contact.Name,
            ["user_name"] = contact.Name,
            ["contact_name"] = contact.Name,
            ["first_name"] = contact.Name?.Split(' ').FirstOrDefault(),
            ["email"] = contact.Email,
            ["phone"] = contact.Phone,
            ["contact_phone"] = contact.Phone,
            ["company"] = contact.Company,
            ["company_name"] = contact.Company,
            ["city"] = contact.City,
            ["country"] = contact.Country,
            ["site_name"] = sender.DisplayName
        };

        // A visible opt-out in the body, alongside the List-Unsubscribe header: templates place
        // {{unsubscribe_url}} (one click) or {{preferences_url}} (choose topics) where they want it.
        var unsubscribeUrl = unsubscribe.BuildUnsubscribeUrl(emailAddress, campaign.Id, recipient.ContactId);
        values["unsubscribe_url"] = unsubscribeUrl;
        values["preferences_url"] = unsubscribeUrl.Replace("/unsubscribe?", "/preferences?", StringComparison.Ordinal);

        foreach (var variable in campaign.Variables)
        {
            // The same merge-field convention the WhatsApp path uses, so an operator's mental
            // model carries across channels.
            var value = variable.MergeField switch
            {
                "@name" => contact.Name,
                "@phone" => contact.Phone,
                "@email" => contact.Email,
                _ => variable.VariableValue
            };

            values[variable.VariableName] = value;
        }

        var subject = MergeFieldRenderer.Render(
            string.IsNullOrWhiteSpace(subjectOverride) ? template.Subject : subjectOverride,
            values);

        var body = MergeFieldRenderer.Render(template.BodyHtml, values);
        var text = MergeFieldRenderer.Render(template.TextBody, values);

        // Refuse rather than deliver a literal placeholder. The seeded templates put their link
        // variables inside href attributes, so an unfilled one produces <a href="{reset_link}">
        // — a broken link that mailbox providers score as obfuscation, which is how a
        // legitimate message ends up in the spam folder.
        //
        // The subject matters as much as the body: "Welcome to {site_name}" in a subject line is
        // both embarrassing and a strong spam signal.
        var unresolved = MergeFieldRenderer.Extract(subject + " " + body)
            .Concat(MergeFieldRenderer.Extract(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unresolved.Count > 0)
        {
            failureReason =
                $"Not sent: {string.Join(", ", unresolved.Select(f => "{" + f + "}"))} "
              + (unresolved.Count == 1 ? "has no value." : "have no value.")
              + " Fill it in on the campaign's Variables step, or use a template that does not need it.";
            return null;
        }

        // A message with nothing in it is not worth sending, and one carrying List-Unsubscribe
        // headers and no content looks exactly like the spam those headers exist to prevent.
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(text))
        {
            failureReason = "Not sent: the template rendered to an empty message.";
            return null;
        }

        var fromDomain = sender.EmailAddress.Contains('@') ? sender.EmailAddress.Split('@')[1] : "localhost";

        var headers = new Dictionary<string, string>
        {
            // RFC 8058 one-click unsubscribe. Both headers are required together: without the
            // Post header, Gmail and Outlook will not show the unsubscribe affordance at all,
            // and recipients use the spam button instead — which costs far more reputation.
            ["List-Unsubscribe"] = $"<{unsubscribe.BuildUnsubscribeUrl(emailAddress, campaign.Id, recipient.ContactId)}>",
            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",

            // Our own correlation id, echoed on replies, so an inbound message can be tied back
            // to the exact campaign and recipient even when the provider id is unavailable.
            ["X-Omni-Campaign-Id"] = campaign.Id.ToString(),
            ["X-Omni-Recipient-Id"] = recipient.Id.ToString()
        };

        if (!string.IsNullOrWhiteSpace(template.PreheaderText))
        {
            // Prepended to the body as a hidden element, which is the only mechanism mail clients
            // actually honour — they take the preheader from the first text in the body. It was
            // previously written to an X-Omni-Preheader header, which nothing reads, so the
            // feature the template entity documents never worked.
            //
            // The preheader is rendered through the same values, so it cannot smuggle an
            // unresolved placeholder past the check above.
            var preheader = MergeFieldRenderer.Render(template.PreheaderText, values);

            body = $"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">"
                 + $"{System.Net.WebUtility.HtmlEncode(preheader)}</div>{body}";
        }

        var trackingEnabled = _options.CurrentValue.Tracking.Enabled;

        // Inject click-tracking redirect URLs if enabled
        if (trackingEnabled && detail.TrackClicks && !string.IsNullOrEmpty(recipient.TrackingId) && !string.IsNullOrWhiteSpace(body))
        {
            body = RewriteLinks(body, recipient.TrackingId, trackingService);
        }

        // Inject open-tracking 1x1 transparent pixel if enabled
        if (trackingEnabled && detail.TrackOpens && !string.IsNullOrEmpty(recipient.TrackingId))
        {
            var openUrl = trackingService.BuildOpenUrl(recipient.TrackingId);
            var pixelTag = $"<img src=\"{openUrl}\" width=\"1\" height=\"1\" border=\"0\" alt=\"\" style=\"display:block;width:1px;min-width:1px;height:1px;min-height:1px;border:0;outline:none;\" />";
            if (!string.IsNullOrWhiteSpace(body))
            {
                body = body.Contains("</body>", StringComparison.OrdinalIgnoreCase)
                    ? body.Replace("</body>", $"{pixelTag}</body>", StringComparison.OrdinalIgnoreCase)
                    : $"{body}{pixelTag}";
            }
        }

        return new EmailMessage
        {
            From = new EmailAddress(sender.EmailAddress, sender.DisplayName),
            ReplyTo = ResolveReplyTo(detail, sender, providerContext),
            To = [new EmailAddress(emailAddress, contact.Name)],
            Subject = subject,
            HtmlBody = body,
            TextBody = string.IsNullOrWhiteSpace(text) ? null : text,
            Attachments = attachments.ToList(),
            Headers = headers,
            MessageId = mimeBuilder.NewMessageId(fromDomain),
            Tags = new Dictionary<string, string>
            {
                ["campaign_id"] = campaign.Id.ToString(),
                ["channel"] = "email"
            }
        };
    }

    /// <summary>
    /// The campaign's attachments, loaded once per campaign and cached rather than re-read (or
    /// re-downloaded) for every recipient. References resolve only inside the uploads root or to an
    /// allow-listed host — see <see cref="IFileStorage"/> — so an attachment URL can no longer read
    /// an arbitrary server file or reach into the internal network.
    /// </summary>
    private async Task<IReadOnlyList<EmailAttachment>> LoadAttachmentsAsync(
        Campaign campaign,
        EmailCampaignDetail detail,
        IFileStorage storage,
        IMemoryCache cache,
        CancellationToken ct)
    {
        var source = !string.IsNullOrWhiteSpace(detail.AttachmentsJson) ? detail.AttachmentsJson : campaign.FileUrl;
        if (string.IsNullOrWhiteSpace(source)) return [];

        var cacheKey = $"email-attachments:{campaign.Id}:{source.GetHashCode()}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<EmailAttachment>? cached) && cached is not null)
        {
            return cached;
        }

        var maxBytes = MaxAttachmentBytes;
        var loaded = new List<EmailAttachment>();

        try
        {
            if (!string.IsNullOrWhiteSpace(detail.AttachmentsJson))
            {
                var requests = JsonSerializer.Deserialize<List<Models.DTOs.Campaigns.CampaignAttachmentRequest>>(detail.AttachmentsJson) ?? [];
                foreach (var att in requests)
                {
                    var bytes = await storage.ReadAsync(att.Url, maxBytes, ct);
                    if (bytes is { Length: > 0 })
                    {
                        var type = !string.IsNullOrWhiteSpace(att.ContentType) ? att.ContentType : GetMimeType(att.FileName);
                        loaded.Add(new EmailAttachment(att.FileName, type, bytes));
                    }
                    else
                    {
                        _logger.LogWarning("Attachment {FileName} for campaign {CampaignId} could not be loaded; sending without it.",
                            att.FileName, campaign.Id);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(campaign.FileUrl))
            {
                var bytes = await storage.ReadAsync(campaign.FileUrl, maxBytes, ct);
                if (bytes is { Length: > 0 })
                {
                    var fileName = !string.IsNullOrWhiteSpace(campaign.FileName) ? campaign.FileName : Path.GetFileName(campaign.FileUrl);
                    loaded.Add(new EmailAttachment(fileName, GetMimeType(fileName), bytes));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load attachments for campaign {CampaignId}.", campaign.Id);
        }

        // Short-lived: long enough to serve a whole campaign run, short enough that a corrected
        // attachment is picked up without a restart.
        cache.Set(cacheKey, (IReadOnlyList<EmailAttachment>)loaded, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            Size = loaded.Sum(a => (long)a.Content.Length)
        });

        return loaded;
    }

    /// <summary>Per-attachment ceiling; the provider enforces its own total message size.</summary>
    private const long MaxAttachmentBytes = 20L * 1024 * 1024;

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    private static readonly Regex AnchorHref = new(
        @"(<a\b[^>]*?\bhref\s*=\s*[""'])(https?://[^""'>\s]+)([""'][^>]*>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    /// <summary>Opt-out and tracking links are never wrapped: rewriting them would record a click instead of honouring them.</summary>
    private static bool IsTrackable(string url) =>
        !(url.Contains("/api/public/email/unsubscribe", StringComparison.OrdinalIgnoreCase)
          || url.Contains("/api/public/email/preferences", StringComparison.OrdinalIgnoreCase)
          || url.Contains("/api/unsubscribe", StringComparison.OrdinalIgnoreCase)
          || url.Contains("/api/t/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// How many links in a template body will be click-tracked — the same rule the rewriter uses,
    /// so the pre-flight check can say up front when the Links report will stay empty.
    /// </summary>
    public static int CountTrackableLinks(string? html) =>
        string.IsNullOrWhiteSpace(html) ? 0 : AnchorHref.Matches(html).Count(m => IsTrackable(WebUtility.HtmlDecode(m.Groups[2].Value)));

    /// <summary>
    /// Wraps each link in a signed click-tracking redirect.
    /// </summary>
    /// <remarks>
    /// The href is HTML-decoded before signing: in markup a URL's query separator is written
    /// <c>&amp;amp;</c>, and signing the encoded form redirected recipients to a URL whose
    /// parameters were mangled. Unsubscribe and tracking links are left alone — rewriting the
    /// opt-out link would record a click instead of honouring the unsubscribe.
    /// </remarks>
    private static string RewriteLinks(string html, string trackingId, IEmailTrackingService tracking)
    {
        var linkIndex = 0;
        return AnchorHref.Replace(html, match =>
        {
            var prefix = match.Groups[1].Value;
            var originalUrl = WebUtility.HtmlDecode(match.Groups[2].Value);
            var suffix = match.Groups[3].Value;

            if (!IsTrackable(originalUrl)) return match.Value;

            var clickUrl = tracking.BuildClickUrl(trackingId, originalUrl, linkIndex++);
            return $"{prefix}{WebUtility.HtmlEncode(clickUrl)}{suffix}";
        });
    }

    private static EmailAddress? ResolveReplyTo(
        EmailCampaignDetail detail,
        EmailSenderIdentity sender,
        EmailProviderContext providerContext)
    {
        // Most specific wins: the campaign's override, then the sender's own, then the
        // connection default.
        var address = detail.ReplyToOverride
                   ?? sender.ReplyTo
                   ?? providerContext.DefaultReplyTo;

        return string.IsNullOrWhiteSpace(address) ? null : new EmailAddress(address);
    }

    /// <summary>
    /// Fails a recipient that never reached the provider, through the event pipeline so the
    /// campaign's Failed figure, the live page and completion all see it. These paths used to
    /// change the row only, which left the campaign counters short and the campaign unfinished.
    /// </summary>
    /// <summary>Marks a recipient Skipped by a compliance rule and counts it; finishes the campaign if that was the last one.</summary>
    private static async Task SkipRecipientAsync(AppDbContext dbContext, CampaignContact recipient, int campaignId, string reason, CancellationToken ct)
    {
        var skipped = await dbContext.CampaignContacts.IgnoreQueryFilters()
            .Where(cc => cc.Id == recipient.Id && cc.Status == MessageStatus.Pending)
            .ExecuteUpdateAsync(u => u
                .SetProperty(cc => cc.Status, MessageStatus.Skipped)
                .SetProperty(cc => cc.ErrorMessage, reason.Length > 500 ? reason[..500] : reason), ct);

        if (skipped > 0)
        {
            await dbContext.Campaigns.IgnoreQueryFilters()
                .Where(c => c.Id == campaignId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.SkippedCount, c => c.SkippedCount + 1), ct);
        }

        await CampaignFinalizer.TryFinalizeAsync(dbContext, campaignId, ct);
    }

    private static async Task FailRecipientAsync(
        AppDbContext dbContext,
        ICampaignEmailEventProcessor eventProcessor,
        CampaignContact recipient,
        string reason,
        CancellationToken ct)
    {
        recipient.Status = MessageStatus.Failed;
        recipient.ErrorMessage = reason.Length > 500 ? reason[..500] : reason;
        recipient.SendAttemptedAt = null;
        await dbContext.SaveChangesAsync(ct);

        await eventProcessor.ProcessAsync(
            kind: EmailEventKind.Failed,
            idempotencyKey: $"smtp:fail:{recipient.Id}",
            source: "Dispatch",
            campaignId: recipient.CampaignId,
            campaignContactId: recipient.Id,
            recipientAddress: recipient.Contact?.Email,
            ct: ct);
    }
}
