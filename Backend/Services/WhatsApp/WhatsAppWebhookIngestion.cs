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
/// Processes stored webhook deliveries. The HTTP endpoint only verifies, stores and acknowledges,
/// so Meta gets its 200 in milliseconds whatever the database is doing, a burst of traffic queues
/// instead of exhausting request threads, and a failure is retried from our queue rather than
/// depending on Meta's redelivery schedule.
/// </summary>
public sealed class WhatsAppWebhookWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<WhatsAppWebhookWorker> _logger;

    public WhatsAppWebhookWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmailOptions> options,
        ILogger<WhatsAppWebhookWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{Environment.MachineName}-wa-webhook-{Guid.NewGuid().ToString("N")[..4]}";

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
                        BatchSize = 20
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

                // Busy: go straight back for more. Idle: the short poll keeps inbound latency low.
                await Task.Delay(claimed > 0 ? 50 : _options.CurrentValue.Queue.PollIntervalMs, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAsync(QueueLease lease, CancellationToken ct)
    {
        WhatsAppWebhookPayload? payload;
        try
        {
            var job = JsonSerializer.Deserialize<WhatsAppWebhookJob>(lease.Payload);
            payload = job is null ? null : JsonSerializer.Deserialize<WhatsAppWebhookPayload>(job.Body, WebJson);
        }
        catch (JsonException ex)
        {
            await _queue.FailAsync(lease, new QueueFailure($"Malformed webhook payload: {ex.Message}", IsTransient: false), ct);
            return;
        }

        if (payload is null)
        {
            await _queue.CompleteAsync(lease, ct);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            await whatsApp.ProcessWebhookAsync(payload);

            scope.ServiceProvider.GetService<IDashboardCacheService>()?.InvalidateCache();
            await _queue.CompleteAsync(lease, ct);
        }
        catch (WhatsAppStatusNotYetKnownException ex)
        {
            // The rest of the payload is applied; retry soon for the statuses that raced their send.
            await _queue.FailAsync(lease, new QueueFailure(ex.Message, IsTransient: true, RetryAfter: TimeSpan.FromSeconds(5)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Processing WhatsApp webhook job {JobId} failed.", lease.JobId);
            await _queue.FailAsync(lease, new QueueFailure(ex.Message, IsTransient: true), CancellationToken.None);
        }
    }

    /// <summary>Same binding rules MVC used for the payload: case-insensitive property names.</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
}
