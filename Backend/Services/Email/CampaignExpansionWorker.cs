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

        // Machine name plus a short random suffix: several instances on one host would otherwise
        // be indistinguishable in the LeasedBy column, which is the first thing anyone looks at
        // when diagnosing a stuck job.
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
        var domains = scope.ServiceProvider.GetRequiredService<IEmailDomainService>();
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
            var (canSend, reason) = await domains.CanSenderSendAsync(detail.SenderIdentityId, ct);
            if (!canSend)
            {
                await FailCampaignAsync(dbContext, campaign, reason ?? "The sender cannot send.", ct);

                // Non-transient: waiting will not verify a domain. The operator has to act.
                await _queue.FailAsync(lease, new QueueFailure(reason ?? "Sender not usable.", IsTransient: false), ct);
                return;
            }

            campaign.Status = CampaignStatus.Sending;
            await dbContext.SaveChangesAsync(ct);

            var enqueued = await ExpandAsync(dbContext, suppression, campaign, lease, ct);

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
        QueueLease lease,
        CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var pageSize = options.Expansion.RecipientPageSize;
        var batchSize = options.Expansion.JobBatchSize;

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
                          && cc.Status == MessageStatus.Pending)
                .OrderBy(cc => cc.Id)
                .Take(pageSize)
                .Select(cc => new
                {
                    cc.Id,
                    cc.ContactId,
                    Email = cc.Contact.Email,
                    cc.Contact.IsActive,
                    cc.Contact.IsDeleted
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

            var messages = new List<QueueMessage>(batchSize);
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

                messages.Add(new QueueMessage
                {
                    QueueName = QueueNames.EmailSend,
                    Payload = JsonSerializer.Serialize(new EmailSendJob(campaign.Id, recipient.Id)),

                    // One job per recipient, ever. This is what makes re-expansion — after a
                    // crash, or a duplicate submission — a no-op rather than a double send.
                    IdempotencyKey = $"send:campaign:{campaign.Id}:recipient:{recipient.Id}",

                    // Partitioned by campaign so the queue's fairness cap stops one large
                    // campaign monopolising the dispatch workers.
                    PartitionKey = campaign.Id.ToString()
                });

                if (messages.Count >= batchSize)
                {
                    totalEnqueued += (await _queue.EnqueueAsync(messages, ct)).Enqueued;
                    messages.Clear();

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
            }

            if (messages.Count > 0)
            {
                totalEnqueued += (await _queue.EnqueueAsync(messages, ct)).Enqueued;
            }

            if (skipped.Count > 0)
            {
                await MarkSkippedAsync(dbContext, skipped, ct);
            }

            // A short page means the end of the recipients.
            if (page.Count < pageSize) break;
        }

        await RecalculateTotalsAsync(dbContext, campaign.Id, ct);
        return totalEnqueued;
    }

    private static async Task MarkSkippedAsync(
        AppDbContext dbContext,
        List<(int Id, MessageStatus Status, string Reason)> skipped,
        CancellationToken ct)
    {
        var ids = skipped.Select(s => s.Id).ToList();

        var rows = await dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Where(cc => ids.Contains(cc.Id))
            .ToListAsync(ct);

        var byId = skipped.ToDictionary(s => s.Id);

        foreach (var row in rows)
        {
            if (!byId.TryGetValue(row.Id, out var skip)) continue;

            row.Status = skip.Status;
            row.ErrorMessage = skip.Reason.Length > 500 ? skip.Reason[..500] : skip.Reason;
        }

        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Recomputes the campaign's counters from the recipient rows.
    ///
    /// <para>
    /// Derived rather than incremented. Incrementing a counter from several workers is exactly the
    /// kind of arithmetic that drifts, and a campaign whose header disagrees with its recipient
    /// list is the bug this project has already had once on the WhatsApp side.
    /// </para>
    /// </summary>
    private static async Task RecalculateTotalsAsync(AppDbContext dbContext, int campaignId, CancellationToken ct)
    {
        var counts = await dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Where(cc => cc.CampaignId == campaignId)
            .GroupBy(cc => cc.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var campaign = await dbContext.Campaigns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null) return;

        int CountOf(MessageStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        campaign.TotalRecipients = counts.Sum(c => c.Count);

        // Delivered counts Delivered and Read, matching how the WhatsApp side already reports it,
        // so one campaigns list can show both channels without two sets of rules.
        campaign.DeliveredCount = CountOf(MessageStatus.Delivered) + CountOf(MessageStatus.Read);

        // Bounces and complaints are failures for the header count. They are told apart from
        // ordinary send failures in the email-specific stats, where the distinction is useful.
        campaign.FailedCount = CountOf(MessageStatus.Failed)
                             + CountOf(MessageStatus.Bounced)
                             + CountOf(MessageStatus.Complained);

        await dbContext.SaveChangesAsync(ct);
    }

    private static async Task FailCampaignAsync(
        AppDbContext dbContext,
        Campaign campaign,
        string reason,
        CancellationToken ct)
    {
        campaign.Status = CampaignStatus.Failed;

        // Written onto every pending recipient too, so the reason is visible on the campaign's
        // own detail screen rather than only in the logs.
        var pending = await dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Where(cc => cc.CampaignId == campaign.Id && cc.Status == MessageStatus.Pending)
            .ToListAsync(ct);

        foreach (var recipient in pending)
        {
            recipient.Status = MessageStatus.Failed;
            recipient.ErrorMessage = reason.Length > 500 ? reason[..500] : reason;
        }

        campaign.FailedCount = await dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .CountAsync(cc => cc.CampaignId == campaign.Id && cc.Status == MessageStatus.Failed, ct);

        await dbContext.SaveChangesAsync(ct);
    }
}
