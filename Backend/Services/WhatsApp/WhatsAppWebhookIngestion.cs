using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;

namespace WhatsAppCampaignApi.Services.WhatsApp;

/// <summary>
/// Verifies Meta's <c>X-Hub-Signature-256</c> header against the app secrets on record.
/// </summary>
/// <remarks>
/// Without this anyone who knows the webhook URL can inject inbound "customer" messages and forge
/// delivery statuses. Several connections may belong to different Meta apps, so a signature valid
/// for any stored app secret is accepted.
/// </remarks>
public interface IWhatsAppWebhookSignatureVerifier
{
    Task<WebhookSignatureResult> VerifyAsync(byte[] body, string? signatureHeader, CancellationToken ct);
}

public enum WebhookSignatureResult { Valid, Invalid, NotConfigured }

/// <inheritdoc />
public sealed class WhatsAppWebhookSignatureVerifier : IWhatsAppWebhookSignatureVerifier
{
    private const string CacheKey = "wa-webhook-app-secrets";

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public WhatsAppWebhookSignatureVerifier(AppDbContext dbContext, IConfiguration configuration, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _cache = cache;
    }

    public async Task<WebhookSignatureResult> VerifyAsync(byte[] body, string? signatureHeader, CancellationToken ct)
    {
        var secrets = await GetSecretsAsync(ct);
        if (secrets.Count == 0) return WebhookSignatureResult.NotConfigured;

        const string prefix = "sha256=";
        if (string.IsNullOrWhiteSpace(signatureHeader)
            || !signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookSignatureResult.Invalid;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signatureHeader[prefix.Length..].Trim());
        }
        catch (FormatException)
        {
            return WebhookSignatureResult.Invalid;
        }

        foreach (var secret in secrets)
        {
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
            if (CryptographicOperations.FixedTimeEquals(expected, provided)) return WebhookSignatureResult.Valid;
        }

        return WebhookSignatureResult.Invalid;
    }

    private async Task<IReadOnlyList<string>> GetSecretsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<string>? cached) && cached is not null) return cached;

        var stored = await _dbContext.WabaConfigurations
            .AsNoTracking()
            .Where(c => c.FacebookAppSecret != null && c.FacebookAppSecret != "")
            .Select(c => c.FacebookAppSecret!)
            .ToListAsync(ct);

        var configured = _configuration.GetSection("WhatsApp:AppSecrets").Get<string[]>() ?? [];
        var single = _configuration["WhatsApp:AppSecret"];

        var all = stored
            .Concat(configured)
            .Append(single ?? string.Empty)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Short-lived: a newly connected app is picked up within a minute.
        _cache.Set(CacheKey, (IReadOnlyList<string>)all, TimeSpan.FromMinutes(1));
        return all;
    }
}

/// <summary>Queue payload for one received webhook delivery.</summary>
public sealed record WhatsAppWebhookJob(string Body, DateTime ReceivedAt);

/// <summary>
/// The ordering key of a webhook delivery carrying customer messages: one customer on one business
/// number (<c>wa:{phone_number_id}:{wa_id}</c>). Deliveries with the same key are processed one at
/// a time in arrival order, on whichever instance claims them; different customers run in parallel.
/// </summary>
public static class WhatsAppWebhookPartition
{
    /// <summary>The key, or null when the body names no customer (it then runs independently).</summary>
    public static string? For(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) return null;

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;

                    var phoneNumberId = value.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("phone_number_id", out var pid)
                        ? pid.GetString()
                        : null;
                    // Messages only. Delivery statuses are applied with compare-and-set and need no
                    // order, and a status waiting to retry must never hold up the customer's next
                    // message behind it.
                    var customer = First(value, "messages", "from");
                    if (!string.IsNullOrWhiteSpace(customer))
                        return $"wa:{phoneNumberId ?? "?"}:{customer}";
                }
            }
        }
        catch (JsonException)
        {
            // Unreadable bodies are rejected by the worker; they need no ordering.
        }
        return null;

        static string? First(JsonElement value, string array, string field) =>
            value.TryGetProperty(array, out var items) && items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray().Select(i => i.TryGetProperty(field, out var f) ? f.GetString() : null).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                : null;
    }
}

//// <summary>
/// Processes stored webhook deliveries. The HTTP endpoint only verifies, stores and acknowledges,
/// so Meta gets its 200 in milliseconds whatever the database is doing, a burst of traffic queues
/// instead of exhausting request threads, and a failure is retried from our queue rather than
/// depending on Meta's redelivery schedule.
/// </summary>
/// <remarks>
/// <para>
/// Several loops claim in parallel (<c>WhatsApp:Webhook:Concurrency</c>, default 4), each with the
/// ordered claim: one customer's messages run one at a time and in order on any instance, while
/// different customers run side by side. The bot reply is part of the job (not a detached task),
/// so a crash or restart retries it instead of losing it.
/// </para>
/// <para>
/// A delivery status for a message we have not stored yet is retried for a few minutes (it may
/// have raced its own send) and then dropped: statuses for messages another system sent on the
/// same number never become known here, and used to fill the dead-letter queue.
/// </para>
/// </remarks>
public sealed class WhatsAppWebhookWorker : BackgroundService
{
    /// <summary>How long a status for an unknown message keeps being retried before it is dropped.</summary>
    internal static readonly TimeSpan UnknownStatusGrace = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan LeaseLength = TimeSpan.FromMinutes(2);

    private readonly IJobQueue _queue;
    private readonly QueueSignal _signal;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppWebhookWorker> _logger;

    public WhatsAppWebhookWorker(
        IJobQueue queue,
        QueueSignal signal,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmailOptions> options,
        IConfiguration configuration,
        ILogger<WhatsAppWebhookWorker> logger)
    {
        _queue = queue;
        _signal = signal;
        _scopeFactory = scopeFactory;
        _options = options;
        _configuration = configuration;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = Math.Clamp(_configuration.GetValue("WhatsApp:Webhook:Concurrency", 4), 1, 64);
        var instance = Guid.NewGuid().ToString("N")[..4];
        return Task.WhenAll(Enumerable.Range(0, loops)
            .Select(i => RunLoopAsync($"{Environment.MachineName}-wa-webhook-{instance}-{i}", stoppingToken)));
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
                        QueueName = QueueNames.WhatsAppWebhook,
                        WorkerId = workerId,
                        // One at a time per loop: a job waiting behind others in a local batch
                        // has no heartbeat, and an expired lease would let another instance
                        // answer the same message. Parallelism comes from the loops instead.
                        BatchSize = 1,
                        VisibilityTimeout = LeaseLength,
                        OrderedPerPartition = true
                    }, stoppingToken);

                    claimed = leases.Count;
                    foreach (var lease in leases)
                    {
                        await ProcessAsync(lease, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WhatsApp webhook worker cycle failed.");
                }

                // Busy: go straight back for more. Idle: sleep until this instance's webhook
                // endpoint signals new work, or the poll interval passes (work enqueued elsewhere).
                if (claimed > 0) await Task.Delay(20, stoppingToken);
                else await _signal.WaitAsync(TimeSpan.FromMilliseconds(_options.CurrentValue.Queue.PollIntervalMs), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAsync(QueueLease lease, CancellationToken ct)
    {
        WhatsAppWebhookJob? job;
        WhatsAppWebhookPayload? payload;
        try
        {
            job = JsonSerializer.Deserialize<WhatsAppWebhookJob>(lease.Payload);
            payload = job is null ? null : JsonSerializer.Deserialize<WhatsAppWebhookPayload>(job.Body, WebJson);
        }
        catch (JsonException ex)
        {
            await _queue.FailAsync(lease, new QueueFailure($"Malformed webhook payload: {ex.Message}", IsTransient: false), ct);
            return;
        }

        if (payload is null || job is null)
        {
            await _queue.CompleteAsync(lease, ct);
            return;
        }

        // A bot turn can take a while (several WhatsApp sends); keep the lease alive meanwhile so
        // no other instance takes the same customer's message and answers it twice.
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var keepAlive = KeepLeaseAliveAsync(lease, heartbeat.Token);
        var queuedMs = (long)Math.Max(0, (DateTime.UtcNow - job.ReceivedAt).TotalMilliseconds);
        var timer = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            await whatsApp.ProcessWebhookAsync(payload, isRetry: lease.Attempt > 1);

            scope.ServiceProvider.GetService<IDashboardCacheService>()?.InvalidateCache();
            await StopAsync(heartbeat, keepAlive);
            await _queue.CompleteAsync(lease, ct);

            // Waited = webhook received until a worker took it; processed = storing and replying.
            if (lease.PartitionKey is not null)
            {
                _logger.LogInformation("Inbound webhook job {JobId}: waited {QueuedMs} ms in the queue, processed in {ProcessedMs} ms.",
                    lease.JobId, queuedMs, timer.ElapsedMilliseconds);
            }
        }
        catch (WhatsAppStatusNotYetKnownException ex)
        {
            await StopAsync(heartbeat, keepAlive);
            var age = DateTime.UtcNow - job.ReceivedAt;
            if (age < UnknownStatusGrace)
            {
                // The rest of the payload is applied; look again shortly for the statuses that
                // raced their send. Deferred, so waiting does not use up the job's attempts.
                var delay = age < TimeSpan.FromSeconds(20) ? TimeSpan.FromSeconds(5)
                          : age < TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(15)
                          : TimeSpan.FromSeconds(30);
                await _queue.DeferAsync(lease, delay, ex.Message, ct);
            }
            else
            {
                _logger.LogInformation(
                    "Dropping delivery statuses for messages not sent from this app ({MessageIds}).", ex.MessageId);
                await _queue.CompleteAsync(lease, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Includes HttpClient timeouts (TaskCanceledException): a failed job, not a stopping worker.
            await StopAsync(heartbeat, keepAlive);
            _logger.LogError(ex, "Processing WhatsApp webhook job {JobId} failed (attempt {Attempt}).", lease.JobId, lease.Attempt);
            await _queue.FailAsync(lease, new QueueFailure(ex.Message, IsTransient: true), CancellationToken.None);
        }
    }

    private async Task KeepLeaseAliveAsync(QueueLease lease, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(LeaseLength / 3, ct);
                if (!await _queue.ExtendLeaseAsync(lease, LeaseLength, ct))
                {
                    _logger.LogWarning("Lost the lease on webhook job {JobId} while processing it.", lease.JobId);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extend the lease on webhook job {JobId}.", lease.JobId);
        }
    }

    private static async Task StopAsync(CancellationTokenSource heartbeat, Task keepAlive)
    {
        await heartbeat.CancelAsync();
        await keepAlive;
    }

    /// <summary>Same binding rules MVC used for the payload: case-insensitive property names.</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
}
