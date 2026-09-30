using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Queue;

namespace WhatsAppCampaignApi.Services.Integrations;

/// <summary>
/// Raises an event for outbound webhooks. Never throws and never waits on a receiver: it records a
/// delivery per matching subscription and queues it, and <see cref="WebhookDeliveryWorker"/> sends.
/// </summary>
public interface IWebhookEmitter
{
    /// <param name="connectionId">The connection the event belongs to, for subscriptions limited to some.</param>
    /// <param name="data">The event's data, safe for any subscriber.</param>
    /// <param name="personalData">Addresses and phone numbers, added only for subscriptions that opted in.</param>
    ValueTask EmitAsync(string eventType, int? connectionId, IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, object?>? personalData = null, CancellationToken ct = default);
}

/// <remarks>
/// Deliveries are written in batches by a background loop (one insert and one enqueue per batch of
/// up to 200), because a campaign's events arrive by the thousand and the database is a round trip
/// away: one write per event would hold up the event consumer, and the live counters with it.
/// Without the loop running (tests, tools) each event is written straight away.
/// </remarks>
public sealed class WebhookEmitter : BackgroundService, IWebhookEmitter
{
    public const string CacheKey = "webhooks:active-subscriptions";
    private const int BatchSize = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobQueue _queue;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WebhookEmitter> _logger;

    // Bounded, and a full buffer makes the raiser wait: back-pressure rather than lost events.
    private readonly Channel<List<WebhookDelivery>> _pending = Channel.CreateBounded<List<WebhookDelivery>>(
        new BoundedChannelOptions(20_000) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private volatile bool _batching;

    public WebhookEmitter(IServiceScopeFactory scopeFactory, IJobQueue queue, IMemoryCache cache, ILogger<WebhookEmitter> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _batching = true;
        var reader = _pending.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                var batch = new List<WebhookDelivery>();
                while (batch.Count < BatchSize && reader.TryRead(out var item)) batch.AddRange(item);
                // Not the stopping token: cancelling between saving the rows and queueing them
                // would leave deliveries that never send. A batch is two short statements.
                await WriteAsync(batch, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            // Shutting down: write what is buffered rather than drop it.
            _batching = false;
            var rest = new List<WebhookDelivery>();
            while (reader.TryRead(out var item)) rest.AddRange(item);
            if (rest.Count > 0) await WriteAsync(rest, CancellationToken.None);
        }
    }

    private async Task WriteAsync(List<WebhookDelivery> deliveries, CancellationToken ct)
    {
        if (deliveries.Count == 0) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.WebhookDeliveries.AddRange(deliveries);
            await db.SaveChangesAsync(ct);
            await _queue.EnqueueAsync(deliveries.Select(WebhookDeliveryWorker.JobFor).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An integration must never break the thing that raised the event.
            _logger.LogWarning(ex, "Could not queue {Count} webhook deliveries.", deliveries.Count);
        }
    }

    private sealed record ActiveSubscription(int Id, string EventTypes, HashSet<int>? ConnectionIds, bool IncludePersonalData);

    public async ValueTask EmitAsync(string eventType, int? connectionId, IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, object?>? personalData = null, CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await ActiveAsync(ct);
            var targets = subscriptions.Where(s =>
                WebhookEventCatalog.Matches(s.EventTypes, eventType)
                && (s.ConnectionIds is null || (connectionId is { } c && s.ConnectionIds.Contains(c)))).ToList();
            if (targets.Count == 0) return;

            var eventId = "evt_" + Guid.NewGuid().ToString("N");
            var occurredAt = DateTime.UtcNow;

            string Body(bool personal)
            {
                var merged = new Dictionary<string, object?>(data);
                if (personal && personalData is not null)
                    foreach (var (k, v) in personalData) merged[k] = v;
                return JsonSerializer.Serialize(new { id = eventId, type = eventType, occurredAt, data = merged }, Json);
            }

            var deliveries = targets.Select(t => new WebhookDelivery
            {
                SubscriptionId = t.Id,
                EventId = eventId,
                EventType = eventType,
                PayloadJson = Body(t.IncludePersonalData),
                CreatedAt = occurredAt
            }).ToList();

            if (_batching) await _pending.Writer.WriteAsync(deliveries, ct);
            else await WriteAsync(deliveries, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An integration must never break the thing that raised the event.
            _logger.LogWarning(ex, "Could not queue webhook event {EventType}.", eventType);
        }
    }

    private async Task<List<ActiveSubscription>> ActiveAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out List<ActiveSubscription>? cached) && cached is not null) return cached;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.WebhookSubscriptions.AsNoTracking().Where(s => s.IsActive)
            .Select(s => new { s.Id, s.EventTypes, s.ConnectionIds, s.IncludePersonalData })
            .ToListAsync(ct);
        var list = rows.Select(r => new ActiveSubscription(r.Id, r.EventTypes,
            string.IsNullOrWhiteSpace(r.ConnectionIds) ? null : r.ConnectionIds.Split(',').Select(int.Parse).ToHashSet(),
            r.IncludePersonalData)).ToList();

        _cache.Set(CacheKey, list, TimeSpan.FromSeconds(30));
        return list;
    }
}

/// <summary>The outcome of one HTTP attempt.</summary>
public sealed record WebhookAttemptResult(bool Success, int? StatusCode, int DurationMs, string? Error, bool IsTransient);

/// <summary>Sends one signed delivery. Shared by the worker and the "send test" button.</summary>
public sealed class WebhookSender
{
    public const string HttpClientName = "webhooks-out";

    private readonly IHttpClientFactory _httpClientFactory;

    public WebhookSender(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    public async Task<WebhookAttemptResult> SendAsync(WebhookSubscription subscription, WebhookDelivery delivery, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
        {
            Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(WebhookSigner.SignatureHeader, WebhookSigner.Sign(subscription.Secret, timestamp, delivery.PayloadJson));
        request.Headers.Add(WebhookSigner.EventIdHeader, delivery.EventId);
        request.Headers.Add(WebhookSigner.EventTypeHeader, delivery.EventType);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)response.StatusCode;
            var ms = (int)stopwatch.ElapsedMilliseconds;
            if (response.IsSuccessStatusCode) return new(true, code, ms, null, false);

            // Throttling, timeouts and server errors may pass on a later try; anything else is the
            // receiver refusing this payload and will refuse it again.
            var transient = code is 408 or 425 or 429 || code >= 500;
            var redirect = code is >= 300 and < 400;
            return new(false, code, ms, redirect ? $"The receiver redirected ({code}); webhooks do not follow redirects." : $"The receiver answered {code}.", transient);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, (int)stopwatch.ElapsedMilliseconds, "The receiver did not answer within 10 seconds.", true);
        }
        catch (HttpRequestException ex)
        {
            var refused = ex.Message.StartsWith("Refused to connect", StringComparison.Ordinal);
            return new(false, null, (int)stopwatch.ElapsedMilliseconds, Truncate(ex.Message, 300), !refused);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Sends queued webhook deliveries, retrying with backoff (by the queue) for up to eight attempts,
/// and switches a subscription off after too many deliveries in a row fail completely. Also
/// removes deliveries older than <c>Webhooks:RetentionDays</c> (30).
/// </summary>
public sealed class WebhookDeliveryWorker : BackgroundService
{
    private sealed record Job(long DeliveryId);

    public static QueueMessage JobFor(WebhookDelivery delivery) => new()
    {
        QueueName = QueueNames.WebhookOut,
        Payload = JsonSerializer.Serialize(new Job(delivery.Id)),
        IdempotencyKey = $"delivery:{delivery.Id}:r{delivery.ReplayCount}",
        PartitionKey = delivery.SubscriptionId.ToString(),
        MaxAttempts = 8
    };

    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WebhookDeliveryWorker> _logger;
    private DateTime _nextCleanup = DateTime.MinValue;

    public WebhookDeliveryWorker(IJobQueue queue, IServiceScopeFactory scopeFactory, IOptionsMonitor<EmailOptions> options,
        IConfiguration configuration, IMemoryCache cache, ILogger<WebhookDeliveryWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{Environment.MachineName}-webhook-out-{Guid.NewGuid().ToString("N")[..4]}";
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var claimed = 0;
                try
                {
                    var leases = await _queue.ClaimAsync(new QueueClaimRequest
                    {
                        QueueName = QueueNames.WebhookOut,
                        WorkerId = workerId,
                        BatchSize = 10,
                        // One slow receiver must not hold up everyone else's deliveries.
                        PerPartitionCap = 3
                    }, stoppingToken);
                    claimed = leases.Count;
                    await Task.WhenAll(leases.Select(l => ProcessAsync(l, stoppingToken)));

                    if (DateTime.UtcNow >= _nextCleanup)
                    {
                        _nextCleanup = DateTime.UtcNow.AddHours(1);
                        await CleanupAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Webhook delivery cycle failed.");
                }

                await Task.Delay(claimed > 0 ? 50 : _options.CurrentValue.Queue.PollIntervalMs, stoppingToken);
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
            var job = JsonSerializer.Deserialize<Job>(lease.Payload);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = job is null ? null : await db.WebhookDeliveries.Include(d => d.Subscription).FirstOrDefaultAsync(d => d.Id == job.DeliveryId, ct);

            if (delivery?.Subscription is null || delivery.Status == "Delivered")
            {
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var subscription = delivery.Subscription;
            if (!subscription.IsActive)
            {
                delivery.Status = "Skipped";
                delivery.Error = "The webhook is switched off.";
                await db.SaveChangesAsync(ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var result = await scope.ServiceProvider.GetRequiredService<WebhookSender>().SendAsync(subscription, delivery, ct);
            // Once the request has gone out its outcome is always recorded, shutdown or not: an
            // unrecorded success would be sent again when the lease expires.
            await RecordAsync(db, subscription, delivery, result, finalAttempt: lease.IsFinalAttempt, CancellationToken.None);

            if (result.Success || !result.IsTransient || lease.IsFinalAttempt)
                await _queue.CompleteAsync(lease, CancellationToken.None);
            else
                await _queue.FailAsync(lease, new QueueFailure(result.Error ?? "Delivery failed.", IsTransient: true), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Webhook delivery job {JobId} failed.", lease.JobId);
            await _queue.FailAsync(lease, new QueueFailure(ex.Message, IsTransient: true), CancellationToken.None);
        }
    }

    /// <summary>Stores an attempt's outcome on the delivery and the subscription.</summary>
    internal async Task RecordAsync(AppDbContext db, WebhookSubscription subscription, WebhookDelivery delivery,
        WebhookAttemptResult result, bool finalAttempt, CancellationToken ct)
    {
        delivery.Attempts++;
        delivery.LastAttemptAt = DateTime.UtcNow;
        delivery.ResponseCode = result.StatusCode;
        delivery.DurationMs = result.DurationMs;
        delivery.Error = result.Error;
        subscription.LastDeliveryAt = DateTime.UtcNow;

        if (result.Success)
        {
            delivery.Status = "Delivered";
            delivery.DeliveredAt = DateTime.UtcNow;
            subscription.LastStatus = "Delivered";
            subscription.FailureStreak = 0;
        }
        else if (!result.IsTransient || finalAttempt)
        {
            delivery.Status = "Failed";
            subscription.LastStatus = "Failed";
            subscription.FailureStreak++;

            var limit = _configuration.GetValue("Webhooks:DisableAfterFailures", 20);
            if (subscription.FailureStreak >= limit && subscription.IsActive && delivery.EventType != "webhook.test")
            {
                subscription.IsActive = false;
                subscription.DisabledReason = $"Switched off after {subscription.FailureStreak} deliveries in a row failed. Last: {result.Error}";
                if (subscription.DisabledReason.Length > 300) subscription.DisabledReason = subscription.DisabledReason[..300];
                _cache.Remove(WebhookEmitter.CacheKey);
                _logger.LogWarning("Webhook {SubscriptionId} switched off after {Count} failed deliveries.", subscription.Id, subscription.FailureStreak);
            }
        }
        else
        {
            subscription.LastStatus = "Retrying";
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var days = Math.Max(1, _configuration.GetValue("Webhooks:RetentionDays", 30));
        var cutoff = DateTime.UtcNow.AddDays(-days);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Deliveries saved but never queued (the process died in between) are queued now. Jobs
        // that already exist are skipped by their idempotency key, so this cannot double-send.
        var stale = DateTime.UtcNow.AddMinutes(-10);
        var orphans = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.Status == "Pending" && d.CreatedAt < stale && d.LastAttemptAt == null)
            .OrderBy(d => d.Id).Take(1000).ToListAsync(ct);
        if (orphans.Count > 0) await _queue.EnqueueAsync(orphans.Select(JobFor).ToList(), ct);

        int removed;
        do
        {
            // Ids first, then delete: an ordered, limited subquery inside the delete would lose its
            // ordering (EF drops it), making the batch arbitrary.
            var batch = await db.WebhookDeliveries.AsNoTracking()
                .Where(d => d.CreatedAt < cutoff && d.Status != "Pending")
                .OrderBy(d => d.Id).Select(d => d.Id).Take(5000).ToListAsync(ct);
            removed = batch.Count == 0 ? 0 : await db.WebhookDeliveries.Where(d => batch.Contains(d.Id)).ExecuteDeleteAsync(ct);
        } while (removed == 5000 && !ct.IsCancellationRequested);
    }
}
