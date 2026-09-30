using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Security;

namespace WhatsAppCampaignApi.Services.Integrations;

public sealed class SaveWebhookRequest
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> EventTypes { get; set; } = [];
    /// <summary>Empty or null: every connection (administrators only).</summary>
    public List<int>? ConnectionIds { get; set; }
    public bool IncludePersonalData { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class WebhookSubscriptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> EventTypes { get; set; } = [];
    public List<int> ConnectionIds { get; set; } = [];
    public bool IncludePersonalData { get; set; }
    public bool IsActive { get; set; }
    public string? DisabledReason { get; set; }
    public string? LastStatus { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
    public int FailureStreak { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Only on create and rotation: the signing secret, shown once.</summary>
    public string? Secret { get; set; }
}

public sealed class WebhookDeliveryDto
{
    public long Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int? ResponseCode { get; set; }
    public int? DurationMs { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

public interface IWebhookSubscriptionService
{
    Task<List<WebhookSubscriptionDto>> ListAsync(CancellationToken ct = default);
    Task<WebhookSubscriptionDto> CreateAsync(SaveWebhookRequest request, CancellationToken ct = default);
    Task<WebhookSubscriptionDto> UpdateAsync(int id, SaveWebhookRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<WebhookSubscriptionDto> RotateSecretAsync(int id, CancellationToken ct = default);
    Task<WebhookDeliveryDto> SendTestAsync(int id, CancellationToken ct = default);
    Task<PagedResponse<WebhookDeliveryDto>> GetDeliveriesAsync(int id, int page, int pageSize, string? status, CancellationToken ct = default);
    Task<WebhookDeliveryDto> ReplayAsync(long deliveryId, CancellationToken ct = default);
}

public sealed class WebhookSubscriptionService : IWebhookSubscriptionService
{
    private readonly AppDbContext _db;
    private readonly WebhookUrlGuard _guard;
    private readonly WebhookSender _sender;
    private readonly IJobQueue _queue;
    private readonly IMemoryCache _cache;
    private readonly IAccessScope _scope;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;

    public WebhookSubscriptionService(AppDbContext db, WebhookUrlGuard guard, WebhookSender sender, IJobQueue queue,
        IMemoryCache cache, IAccessScope scope, ICurrentUserService currentUser, IAuditService audit)
    {
        _db = db;
        _guard = guard;
        _sender = sender;
        _queue = queue;
        _cache = cache;
        _scope = scope;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<List<WebhookSubscriptionDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.WebhookSubscriptions.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var allowed = await _scope.GetAllowedConnectionIdsAsync();
        // A scoped user sees only subscriptions confined to connections they may see.
        return rows.Where(r => allowed is null || (Connections(r).Count > 0 && Connections(r).All(allowed.Contains)))
            .Select(r => ToDto(r)).ToList();
    }

    public async Task<WebhookSubscriptionDto> CreateAsync(SaveWebhookRequest request, CancellationToken ct = default)
    {
        var subscription = new WebhookSubscription { Secret = WebhookSigner.NewSecret(), CreatedByUserId = _currentUser.UserId };
        await ApplyAsync(subscription, request, ct);
        _db.WebhookSubscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);
        Invalidate();

        await _audit.LogAsync("Webhook.Created", "Security", $"Created webhook \"{subscription.Name}\" to {HostOf(subscription.Url)}.", "Webhook", subscription.Id.ToString());
        return ToDto(subscription, includeSecret: true);
    }

    public async Task<WebhookSubscriptionDto> UpdateAsync(int id, SaveWebhookRequest request, CancellationToken ct = default)
    {
        var subscription = await LoadAsync(id, ct);
        var urlChanged = !string.Equals(subscription.Url, request.Url?.Trim(), StringComparison.Ordinal);
        await ApplyAsync(subscription, request, ct);
        if (subscription.IsActive)
        {
            subscription.FailureStreak = 0;
            subscription.DisabledReason = null;
        }
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        Invalidate();

        await _audit.LogAsync("Webhook.Updated", "Security",
            $"Updated webhook \"{subscription.Name}\"{(urlChanged ? $" (now to {HostOf(subscription.Url)})" : "")}.", "Webhook", id.ToString());
        return ToDto(subscription);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var subscription = await LoadAsync(id, ct);
        _db.WebhookSubscriptions.Remove(subscription);
        await _db.SaveChangesAsync(ct);
        Invalidate();
        await _audit.LogAsync("Webhook.Deleted", "Security", $"Deleted webhook \"{subscription.Name}\".", "Webhook", id.ToString());
    }

    public async Task<WebhookSubscriptionDto> RotateSecretAsync(int id, CancellationToken ct = default)
    {
        var subscription = await LoadAsync(id, ct);
        subscription.Secret = WebhookSigner.NewSecret();
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Webhook.SecretRotated", "Security", $"Rotated the signing secret of webhook \"{subscription.Name}\".", "Webhook", id.ToString());
        return ToDto(subscription, includeSecret: true);
    }

    /// <summary>Sends a <c>webhook.test</c> event now and reports what the receiver answered.</summary>
    public async Task<WebhookDeliveryDto> SendTestAsync(int id, CancellationToken ct = default)
    {
        var subscription = await LoadAsync(id, ct);
        await _guard.ValidateAsync(subscription.Url, ct);

        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var delivery = new WebhookDelivery
        {
            SubscriptionId = id,
            EventId = eventId,
            EventType = "webhook.test",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = eventId, type = "webhook.test", occurredAt = DateTime.UtcNow,
                data = new { message = "Test event from Waba Connect.", webhookId = id }
            }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        };
        _db.WebhookDeliveries.Add(delivery);

        var result = await _sender.SendAsync(subscription, delivery, ct);
        delivery.Attempts = 1;
        delivery.LastAttemptAt = DateTime.UtcNow;
        delivery.ResponseCode = result.StatusCode;
        delivery.DurationMs = result.DurationMs;
        delivery.Error = result.Error;
        delivery.Status = result.Success ? "Delivered" : "Failed";
        if (result.Success) delivery.DeliveredAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(delivery);
    }

    public async Task<PagedResponse<WebhookDeliveryDto>> GetDeliveriesAsync(int id, int page, int pageSize, string? status, CancellationToken ct = default)
    {
        await LoadAsync(id, ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.WebhookDeliveries.AsNoTracking().Where(d => d.SubscriptionId == id);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var known = Catalogs.WebhookCatalog.DeliveryStatuses.FirstOrDefault(s => s.Value.Equals(status, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"\"{status}\" is not a delivery status.");
            query = query.Where(d => d.Status == known.Value);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(d => d.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<WebhookDeliveryDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>Sends a delivery again, with the same event id so the receiver can de-duplicate.</summary>
    public async Task<WebhookDeliveryDto> ReplayAsync(long deliveryId, CancellationToken ct = default)
    {
        var delivery = await _db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct)
            ?? throw new KeyNotFoundException("Delivery not found.");
        var subscription = await LoadAsync(delivery.SubscriptionId, ct);
        if (!subscription.IsActive) throw new InvalidOperationException("Switch the webhook on before replaying deliveries.");
        if (delivery.Status == "Pending") throw new InvalidOperationException("This delivery is still being sent.");

        delivery.Status = "Pending";
        delivery.ReplayCount++;
        delivery.Error = null;
        await _db.SaveChangesAsync(ct);
        await _queue.EnqueueAsync([WebhookDeliveryWorker.JobFor(delivery)], ct);

        await _audit.LogAsync("Webhook.Replayed", "Data", $"Replayed {delivery.EventType} delivery {delivery.EventId} to webhook \"{subscription.Name}\".", "Webhook", subscription.Id.ToString());
        return ToDto(delivery);
    }

    private async Task ApplyAsync(WebhookSubscription subscription, SaveWebhookRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100) throw new ArgumentException("Give the webhook a name (up to 100 characters).");
        var uri = await _guard.ValidateAsync(request.Url, ct);
        var events = WebhookEventCatalog.Normalize(request.EventTypes);

        var connections = (request.ConnectionIds ?? []).Distinct().OrderBy(c => c).ToList();
        if (connections.Count > 0)
        {
            foreach (var c in connections) await _scope.EnsureConnectionAllowedAsync(c);
            var existing = await _db.Connections.CountAsync(c => connections.Contains(c.Id), ct);
            if (existing != connections.Count) throw new ArgumentException("One of the chosen connections does not exist.");
        }
        else if (await _scope.GetAllowedConnectionIdsAsync() is not null)
        {
            // "Every connection" would include connections this user may not see.
            throw new ForbiddenException("Choose the connections this webhook covers.");
        }

        subscription.Name = name;
        subscription.Url = uri.AbsoluteUri;
        subscription.EventTypes = string.Join(',', events);
        subscription.ConnectionIds = connections.Count > 0 ? string.Join(',', connections) : null;
        subscription.IncludePersonalData = request.IncludePersonalData;
        subscription.IsActive = request.IsActive;
    }

    private async Task<WebhookSubscription> LoadAsync(int id, CancellationToken ct)
    {
        var subscription = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException("Webhook not found.");
        var allowed = await _scope.GetAllowedConnectionIdsAsync();
        var connections = Connections(subscription);
        if (allowed is not null && (connections.Count == 0 || !connections.All(allowed.Contains)))
            throw new KeyNotFoundException("Webhook not found.");
        return subscription;
    }

    private void Invalidate() => _cache.Remove(WebhookEmitter.CacheKey);

    private static List<int> Connections(WebhookSubscription s) =>
        string.IsNullOrWhiteSpace(s.ConnectionIds) ? [] : s.ConnectionIds.Split(',').Select(int.Parse).ToList();

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?";

    private static WebhookSubscriptionDto ToDto(WebhookSubscription s, bool includeSecret = false) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Url = s.Url,
        EventTypes = s.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        ConnectionIds = Connections(s),
        IncludePersonalData = s.IncludePersonalData,
        IsActive = s.IsActive,
        DisabledReason = s.DisabledReason,
        LastStatus = s.LastStatus,
        LastDeliveryAt = s.LastDeliveryAt,
        FailureStreak = s.FailureStreak,
        CreatedAt = s.CreatedAt,
        Secret = includeSecret ? s.Secret : null
    };

    private static WebhookDeliveryDto ToDto(WebhookDelivery d) => new()
    {
        Id = d.Id,
        EventId = d.EventId,
        EventType = d.EventType,
        Status = d.Status,
        Attempts = d.Attempts,
        ResponseCode = d.ResponseCode,
        DurationMs = d.DurationMs,
        Error = d.Error,
        CreatedAt = d.CreatedAt,
        LastAttemptAt = d.LastAttemptAt,
        PayloadJson = d.PayloadJson
    };
}
