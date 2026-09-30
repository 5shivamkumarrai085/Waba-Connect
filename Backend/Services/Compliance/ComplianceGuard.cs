using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Compliance;

/// <summary>The account's compliance settings, read once per batch (they are cached by OmniSettings).</summary>
public sealed record CompliancePolicy(
    bool QuietHoursEnabled,
    int QuietStartHour,
    int QuietEndHour,
    TimeZoneInfo DefaultZone,
    int FrequencyCapMax,
    int FrequencyCapDays,
    bool WhatsAppRequiresOptIn);

/// <summary>One recipient to evaluate: the campaign-recipient row, its contact and time zone.</summary>
public sealed record ComplianceCandidate(int CampaignContactId, int ContactId, string? TimeZone);

/// <summary>
/// The outcome for one recipient: excluded (with a reason shown to the operator), or allowed —
/// optionally not before a given UTC time (local-time schedule, quiet hours).
/// </summary>
public sealed record ComplianceDecision(string? ExclusionReason, DateTime? NotBeforeUtc)
{
    public static readonly ComplianceDecision Allowed = new(null, null);
    public bool IsExcluded => ExclusionReason is not null;
}

/// <summary>
/// The rules every campaign send passes, on both channels: consent, WhatsApp opt-in, the
/// frequency cap, quiet hours and recipient-local send time.
/// </summary>
/// <remarks>
/// Evaluated in batches at expansion (one query per rule per page, not per recipient), and
/// re-checked cheaply at send time for the things that can change in between (an opt-out, the
/// clock entering quiet hours). Transactional campaigns are exempt from the cap and quiet hours,
/// but never from an explicit opt-out.
/// </remarks>
public interface IComplianceGuard
{
    Task<CompliancePolicy> GetPolicyAsync(CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, ComplianceDecision>> EvaluateAsync(
        Campaign campaign, IReadOnlyList<ComplianceCandidate> recipients, DateTime nowUtc, CancellationToken ct = default);

    Task<ComplianceDecision> RecheckAsync(
        Campaign campaign, int contactId, string? timeZone, DateTime nowUtc, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ComplianceGuard : IComplianceGuard
{
    public const string QuietHoursEnabledKey = "compliance.quietHours.enabled";
    public const string QuietStartKey = "compliance.quietHours.startHour";
    public const string QuietEndKey = "compliance.quietHours.endHour";
    public const string TimeZoneKey = "compliance.timeZone";
    public const string CapMaxKey = "compliance.frequencyCap.maxMessages";
    public const string CapDaysKey = "compliance.frequencyCap.days";
    public const string WhatsAppOptInKey = "compliance.whatsapp.requireOptIn";

    private readonly AppDbContext _db;
    private readonly IOmniSettingsService _settings;

    public ComplianceGuard(AppDbContext db, IOmniSettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<CompliancePolicy> GetPolicyAsync(CancellationToken ct = default) => new(
        QuietHoursEnabled: await _settings.GetFlagAsync(QuietHoursEnabledKey),
        QuietStartHour: Math.Clamp(await _settings.GetNumberAsync(QuietStartKey, 21), 0, 23),
        QuietEndHour: Math.Clamp(await _settings.GetNumberAsync(QuietEndKey, 9), 0, 23),
        DefaultZone: ResolveZone(await _settings.GetValueAsync(TimeZoneKey), TimeZoneInfo.Utc),
        FrequencyCapMax: Math.Max(0, await _settings.GetNumberAsync(CapMaxKey, 0)),
        FrequencyCapDays: Math.Clamp(await _settings.GetNumberAsync(CapDaysKey, 7), 1, 90),
        WhatsAppRequiresOptIn: await _settings.GetFlagAsync(WhatsAppOptInKey));

    public async Task<IReadOnlyDictionary<int, ComplianceDecision>> EvaluateAsync(
        Campaign campaign, IReadOnlyList<ComplianceCandidate> recipients, DateTime nowUtc, CancellationToken ct = default)
    {
        var result = new Dictionary<int, ComplianceDecision>(recipients.Count);
        if (recipients.Count == 0) return result;

        var policy = await GetPolicyAsync(ct);
        var contactIds = recipients.Select(r => r.ContactId).Distinct().ToList();

        var consent = await LoadConsentAsync(campaign, contactIds, ct);
        var recentCounts = await LoadRecentCountsAsync(campaign, policy, contactIds, nowUtc, ct);

        foreach (var recipient in recipients)
        {
            // TryGetValue, not GetValueOrDefault: the default ConsentStatus is OptedIn, which would
            // read "no record" as "opted in".
            ConsentStatus? recorded = consent.TryGetValue(recipient.ContactId, out var found) ? found : null;
            var reason = ExclusionFor(campaign, policy, recorded, recentCounts.GetValueOrDefault(recipient.ContactId));
            result[recipient.CampaignContactId] = reason is not null
                ? new ComplianceDecision(reason, null)
                : new ComplianceDecision(null, EarliestSendUtc(campaign, policy, recipient.TimeZone, nowUtc));
        }

        return result;
    }

    public async Task<ComplianceDecision> RecheckAsync(
        Campaign campaign, int contactId, string? timeZone, DateTime nowUtc, CancellationToken ct = default)
    {
        var policy = await GetPolicyAsync(ct);
        var consent = await LoadConsentAsync(campaign, [contactId], ct);

        // The cap is not re-counted here: it was applied at expansion, and a per-send count would
        // put one grouped query on every message of a million-recipient campaign.
        ConsentStatus? recorded = consent.TryGetValue(contactId, out var found) ? found : null;
        var reason = ExclusionFor(campaign, policy, recorded, recentCount: 0);
        if (reason is not null) return new ComplianceDecision(reason, null);

        // Only quiet hours at send time: the local-time schedule was already applied to the job.
        var release = campaign.IsTransactional ? null : QuietHoursRelease(policy, ResolveZone(timeZone, policy.DefaultZone), nowUtc);
        return new ComplianceDecision(null, release);
    }

    // ── Rules ──────────────────────────────────────────────────────────────────────────────

    private static string? ExclusionFor(Campaign campaign, CompliancePolicy policy, ConsentStatus? consent, int recentCount)
    {
        if (consent == ConsentStatus.OptedOut)
            return "The contact has opted out of these messages.";

        if (campaign.Channel == MessageChannel.WhatsApp && policy.WhatsAppRequiresOptIn
            && !campaign.IsTransactional && consent != ConsentStatus.OptedIn)
            return "No WhatsApp opt-in is on record for this contact.";

        if (!campaign.IsTransactional && policy.FrequencyCapMax > 0 && recentCount >= policy.FrequencyCapMax)
            return $"Frequency cap reached: {recentCount} marketing message(s) in the last {policy.FrequencyCapDays} day(s).";

        return null;
    }

    /// <summary>
    /// The contact's consent for this campaign's channel and topic. An explicit answer on the topic
    /// wins; otherwise one on "all". An opt-out on "all" still wins over an opt-in on the topic.
    /// </summary>
    private async Task<Dictionary<int, ConsentStatus>> LoadConsentAsync(Campaign campaign, IReadOnlyCollection<int> contactIds, CancellationToken ct)
    {
        var topic = ConsentTopics.Normalize(campaign.Topic);
        var ids = contactIds.ToArray();
        var rows = await _db.ContactConsents.AsNoTracking()
            .Where(c => ids.Contains(c.ContactId)
                     && c.Channel == campaign.Channel
                     && (c.Topic == topic || c.Topic == ConsentTopics.All))
            .Select(c => new { c.ContactId, c.Topic, c.Status })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.ContactId).ToDictionary(
            g => g.Key,
            g =>
            {
                if (g.Any(r => r.Topic == ConsentTopics.All && r.Status == ConsentStatus.OptedOut)) return ConsentStatus.OptedOut;
                var specific = g.FirstOrDefault(r => r.Topic == topic);
                return specific?.Status ?? g.First().Status;
            });
    }

    /// <summary>Marketing messages each contact was sent in the policy window, across both channels.</summary>
    private async Task<Dictionary<int, int>> LoadRecentCountsAsync(
        Campaign campaign, CompliancePolicy policy, IReadOnlyCollection<int> contactIds, DateTime nowUtc, CancellationToken ct)
    {
        if (campaign.IsTransactional || policy.FrequencyCapMax <= 0) return [];

        var since = nowUtc.AddDays(-policy.FrequencyCapDays);
        var ids = contactIds.ToArray();
        return await _db.CampaignContacts.IgnoreQueryFilters().AsNoTracking()
            .Where(cc => ids.Contains(cc.ContactId)
                      && cc.SentAt != null && cc.SentAt >= since
                      && cc.CampaignId != campaign.Id
                      && !cc.Campaign.IsTransactional)
            .GroupBy(cc => cc.ContactId)
            .Select(g => new { ContactId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ContactId, x => x.Count, ct);
    }

    // ── Timing ─────────────────────────────────────────────────────────────────────────────

    /// <summary>When this recipient may be sent to: the local-time target (if any), then out of quiet hours.</summary>
    public static DateTime? EarliestSendUtc(Campaign campaign, CompliancePolicy policy, string? timeZone, DateTime nowUtc)
    {
        var zone = ResolveZone(timeZone, policy.DefaultZone);
        var candidate = nowUtc;

        if (campaign.ScheduleType == ScheduleType.RecipientLocalTime && campaign.LocalSendAt is { } wallClock)
        {
            var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
            var target = TimeZoneInfo.ConvertTimeToUtc(AdjustForGap(local, zone), zone);
            if (target > candidate) candidate = target;
        }

        if (!campaign.IsTransactional && QuietHoursRelease(policy, zone, candidate) is { } release)
            candidate = release;

        return candidate > nowUtc ? candidate : null;
    }

    /// <summary>
    /// If <paramref name="atUtc"/> falls in the recipient's quiet hours, the UTC time they end;
    /// otherwise null. A window like 21→9 spans midnight.
    /// </summary>
    public static DateTime? QuietHoursRelease(CompliancePolicy policy, TimeZoneInfo zone, DateTime atUtc)
    {
        if (!policy.QuietHoursEnabled || policy.QuietStartHour == policy.QuietEndHour) return null;

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc), zone);
        var hour = local.Hour;
        var start = policy.QuietStartHour;
        var end = policy.QuietEndHour;

        var inQuiet = start < end ? hour >= start && hour < end : hour >= start || hour < end;
        if (!inQuiet) return null;

        var releaseDate = local.Date;
        if (start > end && hour >= start) releaseDate = releaseDate.AddDays(1);
        var releaseLocal = DateTime.SpecifyKind(releaseDate.AddHours(end), DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(AdjustForGap(releaseLocal, zone), zone);
    }

    /// <summary>An IANA (or Windows) zone id, or the fallback when it is empty or unknown.</summary>
    public static TimeZoneInfo ResolveZone(string? id, TimeZoneInfo fallback)
    {
        if (string.IsNullOrWhiteSpace(id)) return fallback;
        return TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) ? zone : fallback;
    }

    /// <summary>A local time that does not exist (skipped by a DST change) is moved forward an hour.</summary>
    private static DateTime AdjustForGap(DateTime local, TimeZoneInfo zone) =>
        zone.IsInvalidTime(local) ? local.AddHours(1) : local;
}
