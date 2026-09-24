using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Operational visibility into the background send queue.
///
/// <para>
/// A queue with no way to inspect it fails silently: nobody notices a stalled worker or a pile of
/// dead-lettered jobs until customers report mail that never arrived. These endpoints exist so
/// that state is answerable without database access.
/// </para>
/// </summary>
[ApiController]
[Route("api/email/queue")]
[Authorize]
public class EmailQueueController : ControllerBase
{
    private readonly IJobQueue _queue;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly IAuditService _auditService;

    public EmailQueueController(
        IJobQueue queue,
        IOptionsMonitor<EmailOptions> options,
        IAuditService auditService)
    {
        _queue = queue;
        _options = options;
        _auditService = auditService;
    }

    /// <summary>
    /// Depth and age for every queue.
    ///
    /// <para>
    /// The number that matters most is <c>oldestPendingMinutes</c>, not the counts: depth alone
    /// cannot tell a healthy burst apart from a queue nothing is draining.
    /// </para>
    /// </summary>
    [HttpGet("stats")]
    [RequiresPermission("EmailQueue.View")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var queues = new List<object>();

        foreach (var queueName in QueueNames.All)
        {
            var snapshot = await _queue.GetDepthAsync(queueName, ct);
            queues.Add(new
            {
                queue = snapshot.QueueName,
                snapshot.Pending,
                snapshot.Leased,
                snapshot.DeadLettered,
                snapshot.Completed,
                snapshot.ExpiredLeases,
                snapshot.OldestPendingAt,
                oldestPendingMinutes = snapshot.OldestPendingAt is { } oldest
                    ? Math.Round((DateTime.UtcNow - oldest).TotalMinutes, 1)
                    : (double?)null
            });
        }

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new
            {
                transport = _queue.TransportName,
                channelEnabled = options.Enabled,
                visibilityTimeoutSeconds = options.Queue.VisibilityTimeoutSeconds,
                maxAttempts = options.Queue.MaxAttempts,
                queues
            }
        });
    }

    /// <summary>
    /// Returns dead-lettered jobs to the queue.
    ///
    /// <para>
    /// Deliberately manual. A job dead-letters because something was actually wrong — an
    /// unverified sender, an expired credential, a provider rejecting the payload — and replaying
    /// it before that is fixed just burns send quota to reach the same conclusion. The count is
    /// capped per call so a mistaken click cannot replay a hundred thousand sends.
    /// </para>
    /// </summary>
    [HttpPost("{queueName}/requeue")]
    [RequiresPermission("EmailQueue.Requeue")]
    public async Task<IActionResult> RequeueDeadLettered(
        string queueName,
        [FromQuery] int maxCount = 100,
        CancellationToken ct = default)
    {
        if (!QueueNames.All.Contains(queueName))
        {
            return BadRequest(new ApiResponse
            {
                Success = false,
                Message = $"Unknown queue '{queueName}'. Valid queues: {string.Join(", ", QueueNames.All)}."
            });
        }

        var capped = Math.Clamp(maxCount, 1, 1000);
        var requeued = await _queue.RequeueDeadLetteredAsync(queueName, capped, ct);

        // Audited: this re-attempts real sends, so who replayed what needs to be answerable.
        await _auditService.LogAsync(
            "EmailQueue.Requeued", "Settings",
            $"Requeued {requeued} dead-lettered job(s) on '{queueName}'.",
            "JobQueue", queueName);

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = requeued == 0
                ? "No dead-lettered jobs to requeue."
                : $"Requeued {requeued} job(s).",
            Data = new { queue = queueName, requeued }
        });
    }
}
