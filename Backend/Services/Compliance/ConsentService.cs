using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Compliance;

public sealed record ContactConsentDto(
    string Channel, string Topic, string Status, string Source, DateTime CapturedAt, DateTime UpdatedAt);

public sealed record ConsentEventDto(
    string Channel, string Topic, string Status, string Source, DateTime OccurredAt, string? ProofJson, string? IpAddress, int? ActorUserId);

public sealed record SetConsentRequest(string Channel, string? Topic, string Status, string? Note);

/// <summary>
/// Records consent: the current answer per (contact, channel, topic) plus an append-only history
/// with the evidence. Every opt-in and opt-out in the product — agents, the unsubscribe link, the
/// preference centre, WhatsApp STOP/START keywords, imports — goes through here.
/// </summary>
public interface IConsentService
{
    Task<IReadOnlyList<ContactConsentDto>> GetForContactAsync(int contactId, CancellationToken ct = default);
    Task<IReadOnlyList<ConsentEventDto>> GetHistoryAsync(int contactId, int limit = 100, CancellationToken ct = default);

    /// <summary>Upserts the consent and appends a history row. Returns false when nothing changed.</summary>
    Task<bool> SetAsync(int contactId, MessageChannel channel, string? topic, ConsentStatus status, string source,
        object? proof = null, int? actorUserId = null, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>Applies an email opt-out (or opt-in) to every contact with this address.</summary>
    Task<int> SetForEmailAsync(string emailAddress, string? topic, ConsentStatus status, string source,
        object? proof = null, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>
    /// Recognises a WhatsApp opt-out (STOP…) or opt-in (START…) keyword in an inbound message and
    /// records it. Returns the new status, or null when the text is not a keyword.
    /// </summary>
    Task<ConsentStatus?> TryApplyWhatsAppKeywordAsync(int contactId, string? text, string? messageId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ConsentService : IConsentService
{
    public const string OptOutKeywordsKey = "compliance.optOutKeywords";
    public const string OptInKeywordsKey = "compliance.optInKeywords";

    private static readonly string[] DefaultOptOut = ["STOP", "UNSUBSCRIBE", "STOP ALL", "OPT OUT"];
    private static readonly string[] DefaultOptIn = ["START", "SUBSCRIBE", "OPT IN"];

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IOmniSettingsService _settings;

    private readonly WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter? _webhooks;

    public ConsentService(AppDbContext db, IAuditService audit, IOmniSettingsService settings, WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter? webhooks = null)
    {
        _webhooks = webhooks;
        _db = db;
        _audit = audit;
        _settings = settings;
    }

    public async Task<IReadOnlyList<ContactConsentDto>> GetForContactAsync(int contactId, CancellationToken ct = default) =>
        await _db.ContactConsents.AsNoTracking()
            .Where(c => c.ContactId == contactId)
            .OrderBy(c => c.Channel).ThenBy(c => c.Topic)
            .Select(c => new ContactConsentDto(c.Channel.ToString(), c.Topic, c.Status.ToString(), c.Source, c.CapturedAt, c.UpdatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ConsentEventDto>> GetHistoryAsync(int contactId, int limit = 100, CancellationToken ct = default) =>
        await _db.ConsentEvents.AsNoTracking()
            .Where(e => e.ContactId == contactId)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(e => new ConsentEventDto(e.Channel.ToString(), e.Topic, e.Status.ToString(), e.Source, e.OccurredAt, e.ProofJson, e.IpAddress, e.ActorUserId))
            .ToListAsync(ct);

    public async Task<bool> SetAsync(int contactId, MessageChannel channel, string? topic, ConsentStatus status, string source,
        object? proof = null, int? actorUserId = null, string? ipAddress = null, CancellationToken ct = default)
    {
        var normalizedTopic = ConsentTopics.Normalize(topic);
        var now = DateTime.UtcNow;
        var proofJson = proof is null ? null : JsonSerializer.Serialize(proof);
        var trimmedSource = Truncate(source, 40) ?? "agent";

        var current = await _db.ContactConsents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ContactId == contactId && c.Channel == channel && c.Topic == normalizedTopic, ct);

        if (current is not null && current.Status == status) return false;

        if (current is null)
        {
            _db.ContactConsents.Add(new ContactConsent
            {
                ContactId = contactId,
                Channel = channel,
                Topic = normalizedTopic,
                Status = status,
                Source = trimmedSource,
                ProofJson = proofJson,
                CapturedAt = now,
                CapturedByUserId = actorUserId,
                UpdatedAt = now
            });
        }
        else
        {
            current.Status = status;
            current.Source = trimmedSource;
            current.ProofJson = proofJson;
            current.CapturedAt = now;
            current.CapturedByUserId = actorUserId;
            current.UpdatedAt = now;
        }

        _db.ConsentEvents.Add(new ConsentEvent
        {
            ContactId = contactId,
            Channel = channel,
            Topic = normalizedTopic,
            Status = status,
            Source = trimmedSource,
            ProofJson = proofJson,
            IpAddress = Truncate(ipAddress, 64),
            ActorUserId = actorUserId,
            OccurredAt = now
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (current is null)
        {
            // Two captures of the same first answer racing (e.g. a double-clicked link). The other
            // one recorded it; re-reading keeps the history consistent with the stored state.
            _db.ChangeTracker.Clear();
            return await SetAsync(contactId, channel, topic, status, source, proof, actorUserId, ipAddress, ct);
        }

        await _audit.LogAsync(
            status == ConsentStatus.OptedOut ? "Consent.OptedOut" : "Consent.OptedIn",
            "Compliance",
            $"Contact #{contactId} {(status == ConsentStatus.OptedOut ? "opted out of" : "opted in to")} {channel} '{normalizedTopic}' via {trimmedSource}.",
            entityType: "Contact",
            entityId: contactId.ToString(),
            actorUserId: actorUserId);

        if (_webhooks is not null)
        {
            await _webhooks.EmitAsync("consent.changed", null, new Dictionary<string, object?>
            {
                ["contactId"] = contactId,
                ["channel"] = channel.ToString(),
                ["topic"] = normalizedTopic,
                ["status"] = status.ToString(),
                ["source"] = trimmedSource
            }, null, ct);
        }

        return true;
    }

    public async Task<int> SetForEmailAsync(string emailAddress, string? topic, ConsentStatus status, string source,
        object? proof = null, string? ipAddress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress)) return 0;
        var normalized = emailAddress.Trim().ToLower();

        var contactIds = await _db.Contacts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Email != null && c.Email.ToLower() == normalized)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var changed = 0;
        foreach (var contactId in contactIds)
        {
            if (await SetAsync(contactId, MessageChannel.Email, topic, status, source, proof, null, ipAddress, ct)) changed++;
        }

        return changed;
    }

    public async Task<ConsentStatus?> TryApplyWhatsAppKeywordAsync(int contactId, string? text, string? messageId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 40) return null;
        var normalized = string.Join(' ', text.Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var optOut = await KeywordsAsync(OptOutKeywordsKey, DefaultOptOut);
        var optIn = await KeywordsAsync(OptInKeywordsKey, DefaultOptIn);

        ConsentStatus? status = optOut.Contains(normalized) ? ConsentStatus.OptedOut
            : optIn.Contains(normalized) ? ConsentStatus.OptedIn
            : null;
        if (status is null) return null;

        await SetAsync(contactId, MessageChannel.WhatsApp, ConsentTopics.All, status.Value, Catalogs.ConsentCatalog.Sources.Keyword,
            new { messageId, text = text.Trim() }, ct: ct);
        return status;
    }

    private async Task<HashSet<string>> KeywordsAsync(string key, string[] fallback)
    {
        var configured = await _settings.GetListAsync(key);
        var source = configured.Count > 0 ? configured : fallback;
        return source.Select(k => k.Trim().ToUpperInvariant()).Where(k => k.Length > 0).ToHashSet();
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
