using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Segments;

public sealed record SegmentResponse(
    int Id, string Name, string? Description, SegmentRuleGroup Rules, int? CachedCount, DateTime? CountedAt, DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record SaveSegmentRequest(string Name, string? Description, SegmentRuleGroup Rules);

public sealed record SegmentPreviewContact(int Id, string Name, string? Phone, string? Email);

public sealed record SegmentPreviewResponse(int Count, List<SegmentPreviewContact> Sample);

public interface ISegmentService
{
    Task<PagedResponse<SegmentResponse>> GetAllAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task<SegmentResponse> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SegmentResponse> CreateAsync(SaveSegmentRequest request, CancellationToken ct = default);
    Task<SegmentResponse> UpdateAsync(int id, SaveSegmentRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>How many contacts match these rules now, and a sample of them.</summary>
    Task<SegmentPreviewResponse> PreviewAsync(SegmentRuleGroup rules, CancellationToken ct = default);

    /// <summary>The ids of every contact currently in any of these segments.</summary>
    Task<HashSet<int>> ResolveContactIdsAsync(IEnumerable<int> segmentIds, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class SegmentService : ISegmentService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;

    public SegmentService(AppDbContext db, IAuditService audit, ICurrentUserService currentUser)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task<PagedResponse<SegmentResponse>> GetAllAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Segments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(s) || (x.Description != null && x.Description.ToLower().Contains(s)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResponse<SegmentResponse>
        {
            Items = rows.Select(Map).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SegmentResponse> GetByIdAsync(int id, CancellationToken ct = default) =>
        Map(await _db.Segments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Segment not found."));

    public async Task<SegmentResponse> CreateAsync(SaveSegmentRequest request, CancellationToken ct = default)
    {
        var name = ValidateName(request.Name);
        SegmentQueryBuilder.Validate(request.Rules);

        if (await _db.Segments.AnyAsync(x => x.Name.ToLower() == name.ToLower(), ct))
            throw new InvalidOperationException("A segment with this name already exists.");

        var segment = new Segment
        {
            Name = name,
            Description = Clean(request.Description, 1000),
            RulesJson = System.Text.Json.JsonSerializer.Serialize(request.Rules, SegmentQueryBuilder.Json),
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };
        await RecountAsync(segment, request.Rules, ct);

        _db.Segments.Add(segment);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("Segment.Created", "Data", $"Created segment \"{segment.Name}\" ({segment.CachedCount} contacts).", "Segment", segment.Id.ToString());
        return Map(segment);
    }

    public async Task<SegmentResponse> UpdateAsync(int id, SaveSegmentRequest request, CancellationToken ct = default)
    {
        var segment = await _db.Segments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Segment not found.");
        var name = ValidateName(request.Name);
        SegmentQueryBuilder.Validate(request.Rules);

        if (await _db.Segments.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct))
            throw new InvalidOperationException("A segment with this name already exists.");

        segment.Name = name;
        segment.Description = Clean(request.Description, 1000);
        segment.RulesJson = System.Text.Json.JsonSerializer.Serialize(request.Rules, SegmentQueryBuilder.Json);
        segment.UpdatedAt = DateTime.UtcNow;
        await RecountAsync(segment, request.Rules, ct);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("Segment.Updated", "Data", $"Updated segment \"{segment.Name}\".", "Segment", id.ToString());
        return Map(segment);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var segment = await _db.Segments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Segment not found.");

        // A segment still waiting to be resolved by a scheduled or running campaign cannot be removed.
        var inUse = await _db.CampaignSegments.AnyAsync(cs => cs.SegmentId == id
            && (cs.Campaign.Status == CampaignStatus.Scheduled || cs.Campaign.Status == CampaignStatus.Sending
                || cs.Campaign.Status == CampaignStatus.AwaitingApproval || cs.Campaign.Status == CampaignStatus.Paused), ct);
        if (inUse) throw new InvalidOperationException("This segment is used by a scheduled or running campaign.");

        _db.Segments.Remove(segment);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Segment.Deleted", "Data", $"Deleted segment \"{segment.Name}\".", "Segment", id.ToString());
    }

    public async Task<SegmentPreviewResponse> PreviewAsync(SegmentRuleGroup rules, CancellationToken ct = default)
    {
        var query = SegmentQueryBuilder.Apply(_db, _db.Contacts.AsNoTracking(), rules, DateTime.UtcNow);
        var count = await query.CountAsync(ct);
        var sample = await query
            .OrderByDescending(c => c.CreatedAt)
            .Take(20)
            .Select(c => new SegmentPreviewContact(c.Id, c.Name, c.Phone, c.Email))
            .ToListAsync(ct);
        return new SegmentPreviewResponse(count, sample);
    }

    public async Task<HashSet<int>> ResolveContactIdsAsync(IEnumerable<int> segmentIds, CancellationToken ct = default)
    {
        var ids = segmentIds.Distinct().ToArray();
        var segments = await _db.Segments.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (segments.Count != ids.Length) throw new KeyNotFoundException("One or more segments no longer exist.");

        var result = new HashSet<int>();
        var now = DateTime.UtcNow;
        foreach (var segment in segments)
        {
            var rules = SegmentQueryBuilder.Parse(segment.RulesJson);
            var members = await SegmentQueryBuilder.Apply(_db, _db.Contacts.AsNoTracking(), rules, now).Select(c => c.Id).ToListAsync(ct);
            result.UnionWith(members);
        }
        return result;
    }

    private async Task RecountAsync(Segment segment, SegmentRuleGroup rules, CancellationToken ct)
    {
        segment.CachedCount = await SegmentQueryBuilder.Apply(_db, _db.Contacts.AsNoTracking(), rules, DateTime.UtcNow).CountAsync(ct);
        segment.CountedAt = DateTime.UtcNow;
    }

    private static SegmentResponse Map(Segment s) =>
        new(s.Id, s.Name, s.Description, SegmentQueryBuilder.Parse(s.RulesJson), s.CachedCount, s.CountedAt, s.CreatedAt, s.UpdatedAt);

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw new ArgumentException("Give the segment a name.");
        if (trimmed.Length > 200) throw new ArgumentException("The segment name can be at most 200 characters.");
        return trimmed;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}

/// <summary>
/// Keeps a segment-targeted campaign's recipients in step with its segments until it sends:
/// people who joined get added, pending recipients who left get removed. Run by the expansion
/// workers of both channels just before they fan out the sends.
/// </summary>
public static class CampaignAudienceRefresher
{
    public static async Task<int> RefreshAsync(AppDbContext db, ISegmentService segments, Campaign campaign, CancellationToken ct)
    {
        var segmentIds = await db.CampaignSegments.AsNoTracking()
            .Where(cs => cs.CampaignId == campaign.Id)
            .Select(cs => cs.SegmentId)
            .ToListAsync(ct);
        if (segmentIds.Count == 0) return 0;

        var current = await segments.ResolveContactIdsAsync(segmentIds, ct);

        var existing = await db.CampaignContacts.IgnoreQueryFilters().AsNoTracking()
            .Where(cc => cc.CampaignId == campaign.Id)
            .Select(cc => new { cc.Id, cc.ContactId, cc.Status })
            .ToListAsync(ct);
        var existingContactIds = existing.Select(e => e.ContactId).ToHashSet();

        // Pending recipients who left the segment are dropped only when the audience is segments
        // alone; anyone added explicitly (a contact or group pick) stays in.
        var targetsOnlySegments = campaign.AudienceIsSegmentsOnly;

        var toAdd = current.Where(id => !existingContactIds.Contains(id)).ToList();
        foreach (var contactId in toAdd)
        {
            db.CampaignContacts.Add(new CampaignContact { CampaignId = campaign.Id, ContactId = contactId, Status = MessageStatus.Pending });
        }

        var removed = 0;
        if (targetsOnlySegments)
        {
            var leftIds = existing.Where(e => e.Status == MessageStatus.Pending && !current.Contains(e.ContactId)).Select(e => e.Id).ToArray();
            if (leftIds.Length > 0)
                removed = await db.CampaignContacts.IgnoreQueryFilters().Where(cc => leftIds.Contains(cc.Id)).ExecuteDeleteAsync(ct);
        }

        if (toAdd.Count > 0) await db.SaveChangesAsync(ct);

        if (toAdd.Count > 0 || removed > 0)
        {
            var total = await db.CampaignContacts.IgnoreQueryFilters().CountAsync(cc => cc.CampaignId == campaign.Id, ct);
            await db.Campaigns.IgnoreQueryFilters().Where(c => c.Id == campaign.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.TotalRecipients, total), ct);
        }

        return toAdd.Count - removed;
    }
}
