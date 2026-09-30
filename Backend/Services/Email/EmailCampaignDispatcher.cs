using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Submits an email campaign for sending.
/// </summary>
public interface IEmailCampaignDispatcher
{
    /// <summary>
    /// Consults the execution gate and, if approved, queues the campaign for expansion.
    ///
    /// <para>
    /// Returns without queueing when the gate parks or rejects the campaign, having already moved
    /// it to the matching status. Safe to call more than once for the same campaign: the
    /// expansion job carries an idempotency key.
    /// </para>
    /// </summary>
    /// <param name="runId">
    /// Distinguishes a deliberate re-run (resume, re-schedule) from a repeated submission of the
    /// same run. Null keeps the one-expansion-per-campaign key.
    /// </param>
    Task<CampaignExecutionOutcome> SubmitAsync(int campaignId, int? requestedByUserId, CancellationToken ct = default, string? runId = null);
}

/// <inheritdoc />
public class EmailCampaignDispatcher : IEmailCampaignDispatcher
{
    private readonly AppDbContext _dbContext;
    private readonly IJobQueue _queue;
    private readonly ICampaignExecutionGate _executionGate;
    private readonly IAuditService _auditService;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<EmailCampaignDispatcher> _logger;

    public EmailCampaignDispatcher(
        AppDbContext dbContext,
        IJobQueue queue,
        ICampaignExecutionGate executionGate,
        IAuditService auditService,
        IOptionsMonitor<EmailOptions> options,
        ILogger<EmailCampaignDispatcher> logger)
    {
        _dbContext = dbContext;
        _queue = queue;
        _executionGate = executionGate;
        _auditService = auditService;
        _options = options;
        _logger = logger;
    }

    public async Task<CampaignExecutionOutcome> SubmitAsync(
        int campaignId,
        int? requestedByUserId,
        CancellationToken ct = default,
        string? runId = null)
    {
        // Refused here rather than queued. Program.cs only registers the workers when the
        // channel is switched on, so enqueueing while it is off produced a campaign that sat in
        // "Sending" forever with every recipient Pending and no error anywhere — the exact
        // opposite of the "do nothing rather than accept campaigns it cannot send" this flag was
        // introduced to guarantee.
        if (!_options.CurrentValue.Enabled)
        {
            throw new InvalidOperationException(
                "The email channel is switched off, so this campaign cannot be sent. "
              + "Set Email:Enabled to true and restart the API before sending email campaigns.");
        }

        var campaign = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.EmailTemplate)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw new KeyNotFoundException($"Campaign {campaignId} not found.");

        if (campaign.Channel != MessageChannel.Email)
        {
            throw new InvalidOperationException(
                $"Campaign {campaignId} is on the {campaign.Channel} channel and is not dispatched by the email pipeline.");
        }

        var existingApproval = await _dbContext.CampaignApprovalStates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.CampaignId == campaignId, ct);

        var decision = await _executionGate.EvaluateAsync(new CampaignExecutionRequest(
            campaign.Id,
            campaign.Name,
            campaign.Channel.ToString(),
            campaign.TotalRecipients,
            campaign.EmailTemplate?.Name,
            requestedByUserId,
            existingApproval?.ExternalReferenceId), ct);

        switch (decision.Outcome)
        {
            case CampaignExecutionOutcome.Rejected:
                campaign.Status = CampaignStatus.Cancelled;
                await RecordApprovalAsync(campaignId, "Rejected", decision, existingApproval, requestedByUserId, ct);
                await _dbContext.SaveChangesAsync(ct);

                await _auditService.LogAsync(
                    "Campaign.ExecutionRejected", "Data",
                    $"Campaign \"{campaign.Name}\" was rejected by the {_executionGate.GateName} execution gate: "
                  + (decision.Reason ?? "no reason given."),
                    "Campaign", campaignId.ToString());

                return CampaignExecutionOutcome.Rejected;

            case CampaignExecutionOutcome.AwaitingExternalApproval:
                // Parked. Nothing is expanded and nothing is sent — which is the whole point: a
                // campaign awaiting approval must not have already put mail on the wire.
                campaign.Status = CampaignStatus.AwaitingApproval;
                await RecordApprovalAsync(campaignId, "Pending", decision, existingApproval, requestedByUserId, ct);
                await _dbContext.SaveChangesAsync(ct);

                await _auditService.LogAsync(
                    "Campaign.AwaitingApproval", "Data",
                    $"Campaign \"{campaign.Name}\" is awaiting external approval (reference: "
                  + $"{decision.ExternalReferenceId ?? "none"}).",
                    "Campaign", campaignId.ToString());

                return CampaignExecutionOutcome.AwaitingExternalApproval;
        }

        // Approved. Scheduled campaigns are queued with a future availability time rather than
        // being left for a poller to notice, so the queue itself is the scheduler — one mechanism
        // instead of two.
        var availableAt = campaign.ScheduleType != ScheduleType.Immediate && campaign.ScheduledAt.HasValue
            ? DateTime.SpecifyKind(campaign.ScheduledAt.Value, DateTimeKind.Utc)
            : (DateTime?)null;

        campaign.Status = campaign.ScheduleType != ScheduleType.Immediate
            ? CampaignStatus.Scheduled
            : CampaignStatus.Sending;

        if (existingApproval is not null)
        {
            // An approver's decision (time and comment) is kept as they recorded it.
            existingApproval.State = "Approved";
            existingApproval.DecidedAt ??= DateTime.UtcNow;
            existingApproval.Reason = decision.Reason ?? existingApproval.Reason;
        }

        await _dbContext.SaveChangesAsync(ct);

        var result = await _queue.EnqueueAsync(
        [
            new QueueMessage
            {
                QueueName = QueueNames.CampaignExpansion,
                Payload = JsonSerializer.Serialize(new CampaignExpansionJob(campaign.Id, runId)),

                // One expansion per campaign run. Re-submitting the same run — after a restart,
                // or from a retried request — must not fan the same recipients out twice; a new
                // run (resume) must be able to queue the recipients still pending.
                IdempotencyKey = runId is null ? $"expand:campaign:{campaign.Id}" : $"expand:campaign:{campaign.Id}:{runId}",
                PartitionKey = campaign.Id.ToString(),
                AvailableAt = availableAt
            }
        ], ct);

        _logger.LogInformation(
            "Campaign {CampaignId} queued for expansion ({Enqueued} enqueued, {Duplicates} already queued), "
          + "available at {AvailableAt}.",
            campaign.Id, result.Enqueued, result.Duplicates, availableAt?.ToString("O") ?? "immediately");

        return CampaignExecutionOutcome.Approved;
    }

    private async Task RecordApprovalAsync(
        int campaignId,
        string state,
        CampaignExecutionDecision decision,
        Models.Entities.CampaignApprovalState? existing,
        int? requestedByUserId,
        CancellationToken ct)
    {
        if (existing is not null)
        {
            if (state == "Pending" && requestedByUserId is not null) existing.RequestedByUserId = requestedByUserId;
            existing.State = state;
            existing.ExternalReferenceId = decision.ExternalReferenceId ?? existing.ExternalReferenceId;
            existing.Reason = decision.Reason;
            if (state != "Pending") existing.DecidedAt = DateTime.UtcNow;
            return;
        }

        _dbContext.CampaignApprovalStates.Add(new Models.Entities.CampaignApprovalState
        {
            CampaignId = campaignId,
            State = state,
            ExternalReferenceId = decision.ExternalReferenceId,
            Reason = decision.Reason,
            RequestedByUserId = requestedByUserId,
            RequestedAt = DateTime.UtcNow,
            DecidedAt = state == "Pending" ? null : DateTime.UtcNow
        });

        await Task.CompletedTask;
    }
}
