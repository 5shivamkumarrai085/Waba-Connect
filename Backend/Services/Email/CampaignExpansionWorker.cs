using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Queue;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Turns an approved email campaign into one send job per recipient.
///
/// <para>
/// A separate stage from dispatch, rather than one worker doing both, because the two have
/// completely different failure modes. Expansion is a database operation that either works or
/// does not; dispatch is hundreds of independent network calls, each of which can fail on its own
/// and needs its own retry. Fusing them would mean one failed recipient could jeopardise a whole
/// campaign's progress, and a restart mid-send would have nothing to resume from.
/// </para>
/// <para>
/// Safe to run on any number of instances, and safe to re-run: every send job carries a
/// per-recipient idempotency key, so a second expansion of the same campaign enqueues nothing.
/// </para>
/// </summary>
public class CampaignExpansionWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<CampaignExpansionWorker> _logger;
    private readonly string _workerId;

    public CampaignExpansionWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmailOptions> options,
        ILogger<CampaignExpansionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;

        _workerId = $"{Environment.MachineName}-expand-{Guid.NewGuid().ToString("N")[..6]}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Campaign expansion worker {WorkerId} started.", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            var didWork = false;

            try
            {
                var leases = await _queue.ClaimAsync(new QueueClaimRequest
                {
                    QueueName = QueueNames.CampaignExpansion,
                    WorkerId = _workerId,

                    // One at a time. Expanding a campaign is a long database operation, and
                    // holding leases on several while working through them only risks their
                    // visibility timeouts lapsing.
                    BatchSize = 1
                }, stoppingToken);

                foreach (var lease in leases)
                {
                    didWork = true;
                    await ProcessAsync(lease, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The claim itself failed — a database blip. Logged and retried after a pause;
                // the loop must not die.
                _logger.LogError(ex, "Campaign expansion worker failed to claim work.");
            }

            try
            {
                // Backs right off when idle, so an empty queue is not polled at full rate.
                var delay = didWork
                    ? _options.CurrentValue.Queue.PollIntervalMs
                    : _options.CurrentValue.Queue.IdleBackoffMs;

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Campaign expansion worker {WorkerId} stopped.", _workerId);
    }

    private async Task ProcessAsync(QueueLease lease, CancellationToken ct)
    {
        CampaignExpansionJob? job;

        try
        {
            job = JsonSerializer.Deserialize<CampaignExpansionJob>(lease.Payload);
        }
        catch (JsonException ex)
        {
            // Unparseable payload. Permanently broken — retrying cannot fix malformed JSON.
            await _queue.FailAsync(lease, new QueueFailure($"Malformed payload: {ex.Message}", IsTransient: false), ct);
            return;
        }

        if (job is null || job.CampaignId <= 0)
        {
            await _queue.FailAsync(lease, new QueueFailure("Payload has no campaign id.", IsTransient: false), ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suppression = scope.ServiceProvider.GetRequiredService<IEmailSuppressionService>();
        var senderGate = scope.ServiceProvider.GetRequiredService<IEmailSenderGate>();
        var executionGate = scope.ServiceProvider.GetRequiredService<ICampaignExecutionGate>();

        try
        {
            var campaign = await dbContext.Campaigns
                .IgnoreQueryFilters()
                .Include(c => c.EmailTemplate)
                .Include(c => c.EmailDetail)
                .FirstOrDefaultAsync(c => c.Id == job.CampaignId, ct);

            if (campaign is null || campaign.IsDeleted)
            {
                // Deleted while queued. Completing rather than failing: there is nothing wrong,
                // and dead-lettering it would leave an operator a job to investigate that has no
                // problem to find.
                _logger.LogInformation("Campaign {CampaignId} no longer exists; dropping its expansion job.", job.CampaignId);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            if (campaign.Status is CampaignStatus.Cancelled or CampaignStatus.Paused)
            {
                _logger.LogInformation(
                    "Campaign {CampaignId} is {Status}; not expanding.", campaign.Id, campaign.Status);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Re-checked here, not just at submission. A worker picking this up after a restart
            // cannot assume the earlier decision still stands, and a host may have revoked it.
            var decision = await executionGate.EvaluateAsync(new CampaignExecutionRequest(
                campaign.Id, campaign.Name, campaign.Channel.ToString(), campaign.TotalRecipients,
                campaign.EmailTemplate?.Name, null, null), ct);

            if (decision.Outcome != CampaignExecutionOutcome.Approved)
            {
                campaign.Status = decision.Outcome == CampaignExecutionOutcome.Rejected
                    ? CampaignStatus.Cancelled
                    : CampaignStatus.AwaitingApproval;

                await dbContext.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Campaign {CampaignId} is not approved to run ({Outcome}); not expanding.",
                    campaign.Id, decision.Outcome);

                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var detail = campaign.EmailDetail;
            if (detail is null || campaign.EmailTemplateId is null)
            {
                await FailCampaignAsync(dbContext, campaign,
                    "This email campaign has no template or sender configured.", ct);
                await _queue.FailAsync(lease,
                    new QueueFailure("Campaign is missing its email configuration.", IsTransient: false), ct);
                return;
            }

            if (campaign.EmailTemplate is { IsEnabled: false })
            {
                await FailCampaignAsync(dbContext, campaign,
                    $"Template '{campaign.EmailTemplate.Name}' is disabled and cannot be sent.", ct);
                await _queue.FailAsync(lease,
                    new QueueFailure("Template is disabled.", IsTransient: false), ct);
                return;
            }

            // The send gate. Checked once here so a misconfigured campaign fails immediately and
            // with an explanation, rather than producing one identical failure per recipient.
            var (canSend, reason) = await senderGate.CanSenderSendAsync(detail.SenderIdentityId, ct);
            if (!canSend)
            {
                await FailCampaignAsync(dbContext, campaign, reason ?? "The sender cannot send.", ct);

                // Non-transient: waiting will not verify a domain. The operator has to act.
                await _queue.FailAsync(lease, new QueueFailure(reason ?? "Sender not usable.", IsTransient: false), ct);
                return;
            }

            campaign.Status = CampaignStatus.Sending;
            await dbContext.SaveChangesAsync(ct);

            // Segment audiences are re-resolved now, at send time, so the campaign reaches the
            // segment as it is today rather than as it was when the campaign was created.
            await WhatsAppCampaignApi.Services.Segments.CampaignAudienceRefresher.RefreshAsync(
                dbContext, scope.ServiceProvider.GetRequiredService<WhatsAppCampaignApi.Services.Segments.ISegmentService>(), campaign, ct);

            // A/B tests: recipients who joined a segment since creation get a variant (or the winner).
            await WhatsAppCampaignApi.Services.Campaigns.AbTestService.AssignLateJoinersAsync(dbContext, campaign, ct);

            var enqueued = await ExpandAsync(dbContext, suppression, campaign, job.RunId, lease, ct);

            _logger.LogInformation(
                "Campaign {CampaignId} expanded: {Enqueued} send job(s) queued.", campaign.Id, enqueued);

            await _queue.CompleteAsync(lease, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down. Left leased so it is reclaimed once the visibility timeout lapses,
            // rather than being marked failed for something that is not a failure.
            _logger.LogInformation("Expansion of campaign {CampaignId} interrupted by shutdown.", job.CampaignId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expand campaign {CampaignId}.", job.CampaignId);
            await _queue.FailAsync(lease, new QueueFailure(ex.Message, IsTransient: true), CancellationToken.None);
        }
    }

    /// <summary>
    /// Pages through recipients, filters out the ones that must not be mailed, and queues the rest.
    /// </summary>
    private async Task<int> ExpandAsync(
        AppDbContext dbContext,
        IEmailSuppressionService suppression,
        Campaign campaign,
        string? runId,
        QueueLease lease,
        CancellationToken ct)
    {
        var options   = _options.CurrentValue;
        var pageSize  = options.Expansion.RecipientPageSize;
        var batchSize = options.Expansion.JobBatchSize;

        // Resolve the tracking service for generating per-recipient tracking IDs
        using var trackingScope = _scopeFactory.CreateScope();
        var trackingService = trackingScope.ServiceProvider.GetRequiredService<IEmailTrackingService>();
        var compliance = trackingScope.ServiceProvider.GetRequiredService<Compliance.IComplianceGuard>();

        var totalEnqueued = 0;
        var lastId = 0;

        while (!ct.IsCancellationRequested)
        {
            // Keyset pagination on the recipient id, not Skip/Take. Offset paging over a large
            // campaign re-scans everything it has already passed on every page, and rows shifting
            // underneath it can make it skip recipients entirely.
            var page = await dbContext.CampaignContacts
                .IgnoreQueryFilters()
                .Where(cc => cc.CampaignId == campaign.Id
                          && cc.Id > lastId
                          && cc.Status == MessageStatus.Pending
                          && !cc.HeldForWinner)
                .OrderBy(cc => cc.Id)
                .Take(pageSize)
                .Select(cc => new
                {
                    cc.Id,
                    cc.ContactId,
                    Email = cc.Contact.Email,
                    cc.Contact.IsActive,
                    cc.Contact.IsDeleted,
                    cc.Contact.TimeZone
                })
                .ToListAsync(ct);

            if (page.Count == 0) break;

            lastId = page[^1].Id;

            // One suppression query per page rather than per recipient.
            var addresses = page
                .Where(r => !string.IsNullOrWhiteSpace(r.Email))
                .Select(r => r.Email!)
                .ToList();

            var suppressed = await suppression.FilterSuppressedAsync(addresses, campaign.ConnectionId, ct);

            var sendable = new List<int>(page.Count);
            var skipped = new List<(int Id, MessageStatus Status, string Reason)>();

            foreach (var recipient in page)
            {
                if (string.IsNullOrWhiteSpace(recipient.Email))
                {
                    // A contact with no email address. Recorded as failed rather than silently
                    // dropped, so the campaign's totals still add up and the operator can see why.
                    skipped.Add((recipient.Id, MessageStatus.Failed, "The contact has no email address."));
                    continue;
                }

                if (!recipient.IsActive || recipient.IsDeleted)
                {
                    skipped.Add((recipient.Id, MessageStatus.Failed, "The contact is inactive or deleted."));
                    continue;
                }

                if (suppressed.Contains(recipient.Email))
                {
                    // Its own status, distinct from Failed: an unsubscribed recipient is a
                    // correct outcome, and reporting it as a failure would make a healthy
                    // campaign look broken.
                    skipped.Add((recipient.Id, MessageStatus.Suppressed,
                        "The address is on the suppression list (unsubscribed, bounced or complained)."));
                    continue;
                }

                sendable.Add(recipient.Id);
            }

            // Compliance: consent, the frequency cap, and when each recipient may be sent to (a
            // local-time schedule, quiet hours). Excluded recipients are Skipped with the reason.
            var decisions = await compliance.EvaluateAsync(
                campaign,
                page.Where(r => sendable.Contains(r.Id))
                    .Select(r => new Compliance.ComplianceCandidate(r.Id, r.ContactId, r.TimeZone))
                    .ToList(),
                DateTime.UtcNow,
                ct);

            foreach (var (recipientId, decision) in decisions)
            {
                if (!decision.IsExcluded) continue;
                sendable.Remove(recipientId);
                skipped.Add((recipientId, MessageStatus.Skipped, decision.ExclusionReason!));
            }

            // Tracking ids are assigned for the whole page in one statement, and BEFORE any job
            // for the page is enqueued. The old order (enqueue every hundred, save ids at the end
            // of the page) let a send worker mint its own id and mail it, only for this save to
            // overwrite it — so that recipient's opens and clicks matched nothing.
            await AssignTrackingIdsAsync(dbContext, sendable, trackingService, ct);

            foreach (var chunk in sendable.Chunk(batchSize))
            {
                var messages = chunk.Select(id => new QueueMessage
                {
                    QueueName = QueueNames.EmailSend,
                    Payload = JsonSerializer.Serialize(new EmailSendJob(campaign.Id, id)),
                    // Per run, so a resumed campaign can queue recipients whose earlier job ended
                    // while it was paused. The dispatch worker's compare-and-set on the recipient
                    // is what guarantees nobody is mailed twice.
                    IdempotencyKey = runId is null
                        ? $"send:campaign:{campaign.Id}:recipient:{id}"
                        : $"send:campaign:{campaign.Id}:recipient:{id}:{runId}",
                    PartitionKey = campaign.Id.ToString(),
                    AvailableAt = decisions.TryGetValue(id, out var timing) ? timing.NotBeforeUtc : null
                }).ToList();

                totalEnqueued += (await _queue.EnqueueAsync(messages, ct)).Enqueued;

                // Expanding a very large campaign can outlast the visibility timeout. The
                // heartbeat keeps the lease alive; losing it means another worker has taken
                // over, so this one stops rather than competing with it.
                if (!await _queue.ExtendLeaseAsync(lease, TimeSpan.FromSeconds(
                    _options.CurrentValue.Queue.VisibilityTimeoutSeconds), ct))
                {
                    _logger.LogWarning(
                        "Lost the expansion lease for campaign {CampaignId} mid-run; another worker has it.",
                        campaign.Id);
                    return totalEnqueued;
                }
            }

            if (skipped.Count > 0)
            {
                await MarkSkippedAsync(dbContext, skipped, ct);
            }

            // Nothing from this page is needed again; keep the change tracker from growing with
            // every page of a million-recipient campaign.
            dbContext.ChangeTracker.Clear();

            // A short page means the end of the recipients.
            if (page.Count < pageSize) break;
        }

        await RecalculateTotalsAsync(dbContext, campaign.Id, ct);
        return totalEnqueued;
    }

    /// <summary>Gives every recipient in <paramref name="recipientIds"/> a tracking id, if it has none.</summary>
    private static async Task AssignTrackingIdsAsync(
        AppDbContext dbContext,
        IReadOnlyList<int> recipientIds,
        IEmailTrackingService trackingService,
        CancellationToken ct)
    {
        if (recipientIds.Count == 0) return;

        var ids = recipientIds.ToArray();
        var tokens = ids.Select(_ => trackingService.GenerateTrackingId()).ToArray();

        // Set-based and conditional: one round trip per page, and an id that already exists (a
        // re-expansion after a crash) is never replaced, so links already mailed keep working.
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "CampaignContacts" cc
               SET "TrackingId" = t.tracking_id
              FROM unnest({ids}::int[], {tokens}::text[]) AS t(id, tracking_id)
             WHERE cc."Id" = t.id
               AND cc."TrackingId" IS NULL
            """, ct);
    }

    private static async Task MarkSkippedAsync(
        AppDbContext dbContext,
        List<(int Id, MessageStatus Status, string Reason)> skipped,
        CancellationToken ct)
    {
        // One UPDATE per distinct outcome instead of loading every row.
        foreach (var group in skipped.GroupBy(s => (s.Status, s.Reason)))
        {
            var ids = group.Select(s => s.Id).ToList();
            var status = group.Key.Status;
            var reason = group.Key.Reason.Length > 500 ? group.Key.Reason[..500] : group.Key.Reason;

            await dbContext.CampaignContacts
                .IgnoreQueryFilters()
                .Where(cc => ids.Contains(cc.Id) && cc.Status == MessageStatus.Pending)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(cc => cc.Status, status)
                    .SetProperty(cc => cc.ErrorMessage, reason), ct);
        }
    }

    /// <summary>
    /// Sets the recipient total, reconciles the counters from the recipient rows (the skipped ones
    /// were never events), and finishes the campaign if nothing is left to send — which is the
    /// only way a campaign whose every recipient was skipped ever reaches a final status.
    /// </summary>
    private static async Task RecalculateTotalsAsync(AppDbContext dbContext, int campaignId, CancellationToken ct)
    {
        var total = await dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .CountAsync(cc => cc.CampaignId == campaignId, ct);

        await dbContext.Campaigns
            .IgnoreQueryFilters()
            .Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.TotalRecipients, total), ct);

        await CampaignFinalizer.ReconcileEmailCountersAsync(dbContext, campaignId, ct);
        await CampaignFinalizer.TryFinalizeAsync(dbContext, campaignId, ct);
    }

    private static async Task FailCampaignAsync(
        AppDbContext dbContext,
        Campaign campaign,
        string reason,
        CancellationToken ct)
    {
        var trimmed = reason.Length > 500 ? reason[..500] : reason;
        var campaignId = campaign.Id;

        // Written onto every pending recipient too, so the reason is visible on the campaign's
        // own detail screen rather than only in the logs. Set-based: a failed million-recipient
        // campaign must not be loaded into memory to be marked failed.
        await dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Where(cc => cc.CampaignId == campaignId && cc.Status == MessageStatus.Pending)
            .ExecuteUpdateAsync(u => u
                .SetProperty(cc => cc.Status, MessageStatus.Failed)
                .SetProperty(cc => cc.ErrorMessage, trimmed), ct);

        campaign.Status = CampaignStatus.Failed;
        await dbContext.SaveChangesAsync(ct);

        await CampaignFinalizer.ReconcileEmailCountersAsync(dbContext, campaignId, ct);
    }
}
