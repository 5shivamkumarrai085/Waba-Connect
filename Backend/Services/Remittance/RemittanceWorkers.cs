using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace WhatsAppCampaignApi.Services.Remittance;

/// <summary>
/// Queue payload for one provider callback. Written only after the adapter verified the signature,
/// so the worker never sees unauthenticated input.
/// </summary>
/// <param name="ReceivedAt">When the callback arrived; a callback for an order not yet known is retried for a while from then.</param>
public sealed record RemittanceCallbackJob(string Kind, string Provider, PaymentWebhookEvent? Payment, PayoutWebhookEvent? Payout, DateTime? ReceivedAt = null)
{
    public const string PaymentKind = "payment";
    public const string PayoutKind = "payout";
}

/// <summary>
/// Verifies a provider callback with its adapter and stores it on the queue. Shared by the webhook
/// controller and the development checkout page, so both take exactly the same path.
/// </summary>
public interface IRemittanceCallbackIngestion
{
    /// <returns>Null when accepted; otherwise the HTTP status to answer with.</returns>
    Task<int?> IngestAsync(string kind, string provider, IReadOnlyDictionary<string, string> headers, string rawBody, CancellationToken ct);
}

public sealed class RemittanceCallbackIngestion : IRemittanceCallbackIngestion
{
    private readonly IRemittanceProviderRegistry _providers;
    private readonly IJobQueue _queue;
    private readonly ILogger<RemittanceCallbackIngestion> _logger;

    public RemittanceCallbackIngestion(IRemittanceProviderRegistry providers, IJobQueue queue, ILogger<RemittanceCallbackIngestion> logger)
    {
        _providers = providers;
        _queue = queue;
        _logger = logger;
    }

    public async Task<int?> IngestAsync(string kind, string provider, IReadOnlyDictionary<string, string> headers, string rawBody, CancellationToken ct)
    {
        RemittanceCallbackJob job;
        string eventId;
        var now = DateTimeOffset.UtcNow;

        try
        {
            if (kind == RemittanceCallbackJob.PaymentKind)
            {
                var adapter = _providers.Payment(provider);
                // The adapter's own name, whatever case the URL used: orders store it exactly so.
                provider = adapter.ProviderName;
                var parsed = adapter.ParseWebhook(headers, rawBody, now);
                if (!parsed.SignatureValid)
                {
                    _logger.LogWarning("Rejected a {Provider} payment callback: {Error}", provider, parsed.Error);
                    return StatusCodes.Status401Unauthorized;
                }
                if (parsed.Event is null)
                {
                    _logger.LogWarning("Ignored a {Provider} payment callback: {Error}", provider, parsed.Error);
                    return StatusCodes.Status400BadRequest;
                }
                job = new(kind, provider, parsed.Event, null, now.UtcDateTime);
                eventId = parsed.Event.ProviderEventId;
            }
            else
            {
                var adapter = _providers.Payout(provider);
                provider = adapter.ProviderName;
                var parsed = adapter.ParseWebhook(headers, rawBody, now);
                if (!parsed.SignatureValid)
                {
                    _logger.LogWarning("Rejected a {Provider} payout callback: {Error}", provider, parsed.Error);
                    return StatusCodes.Status401Unauthorized;
                }
                if (parsed.Event is null)
                {
                    _logger.LogWarning("Ignored a {Provider} payout callback: {Error}", provider, parsed.Error);
                    return StatusCodes.Status400BadRequest;
                }
                job = new(kind, provider, null, parsed.Event, now.UtcDateTime);
                eventId = parsed.Event.ProviderEventId;
            }
        }
        catch (ProviderNotConfiguredException)
        {
            return StatusCodes.Status404NotFound;
        }

        await _queue.EnqueueAsync(new[]
        {
            new QueueMessage
            {
                QueueName = QueueNames.RemittanceCallback,
                Payload = JsonSerializer.Serialize(job),
                // A redelivered callback is dropped at the queue; the event table is the second guard.
                IdempotencyKey = $"{kind}:{provider}:{eventId}",
                Priority = 10
            }
        }, ct);

        return null;
    }
}

/// <summary>Applies queued provider callbacks and submits paid transfers to their payout provider.</summary>
public sealed class RemittanceQueueWorker : BackgroundService
{
    /// <summary>How long a callback naming an order we have not saved yet keeps being retried.</summary>
    private static readonly TimeSpan UnknownOrderGrace = TimeSpan.FromMinutes(10);

    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _queueOptions;
    private readonly ILogger<RemittanceQueueWorker> _logger;

    public RemittanceQueueWorker(IJobQueue queue, IServiceScopeFactory scopeFactory, IOptionsMonitor<EmailOptions> queueOptions, ILogger<RemittanceQueueWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _queueOptions = queueOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{Environment.MachineName}-remit-{Guid.NewGuid().ToString("N")[..4]}";

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var claimed = 0;
                try
                {
                    // Payouts one at a time: a job waiting behind a slow provider call in a local
                    // batch could outlive its lease and be taken by another instance meanwhile.
                    foreach (var (queueName, batch) in new[] { (QueueNames.RemittanceCallback, 10), (QueueNames.RemittancePayout, 1) })
                    {
                        var leases = await _queue.ClaimAsync(new QueueClaimRequest { QueueName = queueName, WorkerId = workerId, BatchSize = batch }, stoppingToken);
                        claimed += leases.Count;
                        foreach (var lease in leases) await ProcessAsync(lease, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Send Money queue worker cycle failed.");
                }

                await Task.Delay(claimed > 0 ? 50 : _queueOptions.CurrentValue.Queue.PollIntervalMs, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAsync(QueueLease lease, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<ITransferOrderService>();

            if (lease.QueueName == QueueNames.RemittanceCallback)
            {
                var job = JsonSerializer.Deserialize<RemittanceCallbackJob>(lease.Payload);
                var outcome = job?.Payment is { } payment ? await orders.ApplyPaymentEventAsync(job.Provider, payment, ct)
                            : job?.Payout is { } payout ? await orders.ApplyPayoutEventAsync(job.Provider, payout, ct)
                            : CallbackOutcome.Ignored;

                // A provider can call back before we have saved its payment or payout id (the
                // call that created it has not returned yet). Look again shortly instead of
                // dropping it, which would leave a paid order looking unpaid.
                if (outcome == CallbackOutcome.UnknownOrder && job?.ReceivedAt is { } receivedAt
                    && DateTime.UtcNow - receivedAt < UnknownOrderGrace)
                {
                    await _queue.DeferAsync(lease, TimeSpan.FromSeconds(15), "Order not known yet", ct);
                    return;
                }
                if (outcome == CallbackOutcome.UnknownOrder)
                {
                    _logger.LogError("Send Money callback job {JobId} names an order that never appeared; dropped.", lease.JobId);
                }
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var payoutJob = JsonSerializer.Deserialize<RemittancePayoutJob>(lease.Payload);
            if (payoutJob is null)
            {
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var result = await orders.SubmitPayoutAsync(payoutJob.OrderId, ct);
            if (!result.Accepted && result.IsTransient)
            {
                if (lease.IsFinalAttempt)
                {
                    // Out of retries: the customer has paid, so this must reach a person rather
                    // than sit in the dead-letter queue unnoticed.
                    await orders.MarkNeedsReviewAsync(payoutJob.OrderId, $"Payout failed after {lease.Attempt} attempts: {result.Error}", ct);
                }
                await _queue.FailAsync(lease, new QueueFailure(result.Error ?? "Payout provider unavailable.", IsTransient: true), ct);
                return;
            }
            await _queue.CompleteAsync(lease, ct);
        }
        catch (JsonException ex)
        {
            await _queue.FailAsync(lease, new QueueFailure($"Malformed job: {ex.Message}", IsTransient: false), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Send Money job {JobId} on {Queue} failed.", lease.JobId, lease.QueueName);
            await _queue.FailAsync(lease, new QueueFailure(ex.Message, IsTransient: true), CancellationToken.None);
        }
    }
}

/// <summary>
/// Housekeeping on a timer: expires unpaid orders past their window, makes sure every new order's
/// "pay here" message went out even if WhatsApp's completion webhook never arrived, and closes
/// abandoned Flow sessions.
/// </summary>
public sealed class RemittanceMaintenanceWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for the Flow's completion webhook before sending the order message anyway.</summary>
    private static readonly TimeSpan NotifyGrace = TimeSpan.FromSeconds(45);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Npgsql.NpgsqlDataSource _dataSource;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly ILogger<RemittanceMaintenanceWorker> _logger;

    public RemittanceMaintenanceWorker(IServiceScopeFactory scopeFactory, Npgsql.NpgsqlDataSource dataSource,
        IOptionsMonitor<RemittanceOptions> options, ILogger<RemittanceMaintenanceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _dataSource = dataSource;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Off means off; and one instance at a time, since the sweeps message customers.
                    if (_options.CurrentValue.Enabled)
                    {
                        await LeaderLock.TryRunAsync(_dataSource, "remittance-maintenance", async () =>
                        {
                            using var scope = _scopeFactory.CreateScope();
                            await RunOnceAsync(scope.ServiceProvider, stoppingToken);
                        }, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Send Money maintenance cycle failed.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public static async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var orders = services.GetRequiredService<ITransferOrderService>();
        var completion = services.GetRequiredService<IOrderCreatedNotification>();

        await orders.ExpireOverdueAsync(ct);
        await orders.ReconcileAsync(ct);

        var cutoff = DateTime.UtcNow - NotifyGrace;
        var unannounced = await db.TransferOrders.AsNoTracking()
            .Where(o => o.CustomerNotifiedAt == null && o.OrderStatus == TransferOrderStatus.AwaitingPayment && o.CreatedAt < cutoff)
            .Select(o => o.Id).Take(20).ToListAsync(ct);
        foreach (var id in unannounced) await completion.SendOnceAsync(id, ct);

        await db.RemittanceSessions
            .Where(s => s.ExpiresAt < DateTime.UtcNow && (s.Status == RemittanceSessionStatus.Started || s.Status == RemittanceSessionStatus.Verified))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, RemittanceSessionStatus.Expired), ct);
    }
}

/// <summary>Sends an order's "created — pay here" message exactly once, whichever path gets there first.</summary>
public interface IOrderCreatedNotification
{
    Task SendOnceAsync(int orderId, CancellationToken ct);
}

public sealed class OrderCreatedNotification : IOrderCreatedNotification
{
    /// <summary>How long after the order a failed "pay here" message keeps being retried.</summary>
    private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IRemittanceNotifier _notifier;

    public OrderCreatedNotification(AppDbContext db, IRemittanceNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task SendOnceAsync(int orderId, CancellationToken ct)
    {
        // Claim first: only the caller whose UPDATE wins sends, so the completion webhook and the
        // maintenance sweep cannot both message the customer.
        var claimed = await _db.TransferOrders
            .Where(o => o.Id == orderId && o.CustomerNotifiedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.CustomerNotifiedAt, DateTime.UtcNow), ct);
        if (claimed == 0) return;

        if (await _notifier.SendOrderCreatedAsync(orderId, ct)) return;

        // Not delivered: release the claim so the maintenance sweep tries again, for a few
        // minutes. Without this one failed send meant the customer never got the payment link.
        var retryUntil = DateTime.UtcNow - RetryWindow;
        await _db.TransferOrders
            .Where(o => o.Id == orderId && o.CreatedAt > retryUntil)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.CustomerNotifiedAt, (DateTime?)null), ct);
    }
}
