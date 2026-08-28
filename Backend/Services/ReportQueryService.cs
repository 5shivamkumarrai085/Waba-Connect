using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Reporting;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class ReportQueryService : IReportQueryService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ReportQueryService> _logger;

    /// <summary>
    /// Upper bound on an export. Large enough for any real reporting need, small enough that one
    /// click cannot pull the whole message table into memory and hold it there while a PDF is
    /// laid out. Past this the caller is told to narrow the date range, which is the honest
    /// answer — a 200,000-row spreadsheet is not a report anyone reads.
    /// </summary>
    public const int MaxExportRows = 20000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ReportQueryService(
        AppDbContext dbContext,
        ICurrentUserService currentUser,
        ILogger<ReportQueryService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── Columns ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The column catalogue, served by the server so the table, the CSV and the spreadsheet all
    /// agree on what exists and what each one is called.
    /// </summary>
    private static readonly ReportColumnDto[] Columns =
    {
        new() { Key = "timestamp", Label = "Date & Time", DefaultVisible = true },
        new() { Key = "contactName", Label = "Contact", DefaultVisible = true },
        new() { Key = "contactPhone", Label = "Phone", DefaultVisible = true },
        new() { Key = "campaignName", Label = "Campaign", DefaultVisible = true },
        new()
        {
            Key = "templateName",
            Label = "Template",
            DefaultVisible = true,
            DerivedNote = "Resolved through the campaign — a message stores no template of its own."
        },
        new() { Key = "connectionName", Label = "Connection", DefaultVisible = false },
        new() { Key = "direction", Label = "Direction", DefaultVisible = true },
        new()
        {
            Key = "messageType",
            Label = "Message Type",
            DefaultVisible = false,
            DerivedNote = "Taken from the attachment's media type; 'text' when there is none."
        },
        new() { Key = "status", Label = "Status", DefaultVisible = true },
        new() { Key = "content", Label = "Content", DefaultVisible = false },
        new() { Key = "sentAt", Label = "Sent", DefaultVisible = false },
        new() { Key = "deliveredAt", Label = "Delivered", DefaultVisible = false },
        new() { Key = "readAt", Label = "Read", DefaultVisible = false },
        new() { Key = "failureReason", Label = "Failure Reason", DefaultVisible = false },
        new()
        {
            Key = "responded",
            Label = "Responded",
            DefaultVisible = true,
            DerivedNote = "Whether an incoming message followed this one in the same conversation."
        },
        new()
        {
            Key = "responseMinutes",
            Label = "Response Time (min)",
            DefaultVisible = true,
            DerivedNote = "Gap to that reply. Blank when there was none."
        }
    };

    public IReadOnlyList<ReportColumnDto> GetColumns() => Columns;

    // ── Row query ────────────────────────────────────────────────────────────

    public async Task<PagedResponse<ReportRowDto>> QueryAsync(ReportQueryRequest request)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var query = BuildQuery(request);
        var totalCount = await query.CountAsync();

        var rows = await ProjectAsync(
            query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                 .Skip((page - 1) * pageSize)
                 .Take(pageSize));

        await AttachResponseTimesAsync(rows);

        // Recipients a campaign never produced a message row for. Appended after the message rows
        // and only on the first page: they have no timestamp to sort by, so interleaving them into
        // a chronological list would put them in an arbitrary position and make paging unstable.
        if (page == 1)
        {
            var unsent = await QueryUnsentRecipientsAsync(request, pageSize);
            rows.AddRange(unsent);
        }

        return new PagedResponse<ReportRowDto>
        {
            Items = rows,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<ReportRowDto>> QueryAllAsync(ReportQueryRequest request)
    {
        var query = BuildQuery(request);

        var rows = await ProjectAsync(
            query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(MaxExportRows));

        await AttachResponseTimesAsync(rows);

        rows.AddRange(await QueryUnsentRecipientsAsync(request, MaxExportRows - rows.Count));

        return rows;
    }

    /// <summary>
    /// The filtered message query, before ordering or paging.
    ///
    /// Soft-deleted messages are excluded throughout: they have been removed from the product, and
    /// a report that still counted them would disagree with every other screen.
    /// </summary>
    private IQueryable<ChatMessage> BuildQuery(ReportQueryRequest request)
    {
        var query = _dbContext.ChatMessages.AsNoTracking().Where(m => !m.IsDeleted);

        if (request.From.HasValue)
        {
            var from = DateTime.SpecifyKind(request.From.Value.Date, DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt >= from);
        }

        if (request.To.HasValue)
        {
            // The end of the chosen day, not its first second — otherwise "to 13 August" silently
            // excludes almost all of the 13th, which is the classic off-by-one in a date filter.
            var to = DateTime.SpecifyKind(request.To.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt < to);
        }

        if (request.CampaignIds is { Count: > 0 })
            query = query.Where(m => m.CampaignId != null && request.CampaignIds.Contains(m.CampaignId.Value));

        if (request.ConnectionIds is { Count: > 0 })
            query = query.Where(m => m.ConnectionId != null && request.ConnectionIds.Contains(m.ConnectionId.Value));

        if (request.ContactIds is { Count: > 0 })
            query = query.Where(m => m.ContactId != null && request.ContactIds.Contains(m.ContactId.Value));

        if (request.MessageTypes is { Count: > 0 })
        {
            // "text" is the absence of a media type rather than a value, so it is matched on null.
            var wantsText = request.MessageTypes.Any(t => string.Equals(t, "text", StringComparison.OrdinalIgnoreCase));
            var mediaTypes = request.MessageTypes
                .Where(t => !string.Equals(t, "text", StringComparison.OrdinalIgnoreCase))
                .ToList();

            query = query.Where(m =>
                (wantsText && m.MediaType == null) ||
                (m.MediaType != null && mediaTypes.Contains(m.MediaType)));
        }

        if (request.Directions is { Count: > 0 })
        {
            var directions = request.Directions
                .Select(d => Enum.TryParse<ChatMessageDirection>(d, true, out var parsed) ? parsed : (ChatMessageDirection?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            if (directions.Count > 0) query = query.Where(m => directions.Contains(m.Direction));
        }

        if (request.Statuses is { Count: > 0 })
        {
            var statuses = request.Statuses
                .Select(s => Enum.TryParse<ChatMessageStatus>(s, true, out var parsed) ? parsed : (ChatMessageStatus?)null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList();

            if (statuses.Count > 0) query = query.Where(m => statuses.Contains(m.Status));
        }

        if (request.TemplateNames is { Count: > 0 })
        {
            // Through the campaign: a chat message carries no template reference of its own.
            var templateNames = request.TemplateNames;
            query = query.Where(m => m.Campaign != null &&
                _dbContext.Templates.Any(t => t.Id == m.Campaign.TemplateId && templateNames.Contains(t.Name)));
        }

        if (request.FailedOnly == true)
            query = query.Where(m => m.Status == ChatMessageStatus.Failed || m.ErrorMessage != null);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(m =>
                m.Text.ToLower().Contains(term) ||
                (m.Contact != null && m.Contact.Name != null && m.Contact.Name.ToLower().Contains(term)) ||
                (m.Contact != null && m.Contact.Phone.Contains(term)));
        }

        return query;
    }

    /// <summary>
    /// Projects to the report row. The template name resolves through the campaign in the same
    /// query rather than in a second pass, so a page costs one round trip to a database that is
    /// ~250 ms away.
    /// </summary>
    private async Task<List<ReportRowDto>> ProjectAsync(IQueryable<ChatMessage> query)
    {
        return await query
            .Select(m => new ReportRowDto
            {
                Id = m.Id,
                Timestamp = m.CreatedAt,
                ContactName = m.Contact != null ? m.Contact.Name : null,
                ContactPhone = m.Contact != null ? m.Contact.Phone : null,
                CampaignName = m.Campaign != null ? m.Campaign.Name : null,
                TemplateName = m.Campaign != null
                    ? _dbContext.Templates.Where(t => t.Id == m.Campaign.TemplateId).Select(t => t.Name).FirstOrDefault()
                    : null,
                ConnectionName = m.Connection != null ? m.Connection.Name : null,
                Direction = m.Direction.ToString(),
                MessageType = m.MediaType ?? "text",
                Status = m.Status.ToString(),
                Content = m.Text,
                SentAt = m.SentAt,
                DeliveredAt = m.DeliveredAt,
                ReadAt = m.ReadAt,
                FailureReason = m.ErrorMessage,
                // Filled in by AttachResponseTimesAsync — deriving it here would mean a correlated
                // subquery per row.
                Responded = null,
                ResponseMinutes = null
            })
            .ToListAsync();
    }

    /// <summary>
    /// Fills in Responded and Response Time for the outgoing rows on this page.
    ///
    /// <para>
    /// Done per page, not per row. The natural expression — "the first incoming message in this
    /// conversation after this one" — is a correlated subquery, which is one round trip per row
    /// against a database this far away; at 25 rows that is six seconds of pure latency. Instead
    /// one query pulls the candidate incoming messages for the conversations on this page, and the
    /// matching happens in memory.
    /// </para>
    /// <para>
    /// Incoming rows are left null rather than false: "did they respond" is not a question about a
    /// message they sent, and answering "No" would read as a real finding.
    /// </para>
    /// </summary>
    private async Task AttachResponseTimesAsync(List<ReportRowDto> rows)
    {
        var outgoing = rows.Where(r => r.Direction == nameof(ChatMessageDirection.Outgoing)).ToList();
        if (outgoing.Count == 0) return;

        var messageIds = outgoing.Select(r => (int)r.Id).ToList();

        // Conversation and timestamp for each outgoing row on this page.
        var anchors = await _dbContext.ChatMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ConversationId, m.CreatedAt })
            .ToListAsync();

        if (anchors.Count == 0) return;

        var conversationIds = anchors.Select(a => a.ConversationId).Distinct().ToList();
        var earliest = anchors.Min(a => a.CreatedAt);

        // Only replies that could belong to one of these anchors: same conversations, and no
        // earlier than the oldest anchor on the page. Served by the
        // (ConversationId, Direction, CreatedAt) index.
        var replies = await _dbContext.ChatMessages.AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId)
                        && m.Direction == ChatMessageDirection.Incoming
                        && !m.IsDeleted
                        && m.CreatedAt >= earliest)
            .Select(m => new { m.ConversationId, m.CreatedAt })
            .ToListAsync();

        var repliesByConversation = replies
            .GroupBy(r => r.ConversationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.CreatedAt).OrderBy(t => t).ToList());

        var anchorById = anchors.ToDictionary(a => a.Id);

        foreach (var row in outgoing)
        {
            if (!anchorById.TryGetValue((int)row.Id, out var anchor)) continue;

            if (!repliesByConversation.TryGetValue(anchor.ConversationId, out var times))
            {
                row.Responded = false;
                continue;
            }

            // First reply strictly after this message. Strictly, so a message and a reply written
            // in the same instant — which happens with imported history — is not counted as an
            // instant response.
            var reply = times.FirstOrDefault(t => t > anchor.CreatedAt);
            if (reply == default)
            {
                row.Responded = false;
                continue;
            }

            row.Responded = true;
            row.ResponseMinutes = Math.Round((reply - anchor.CreatedAt).TotalMinutes, 2);
        }
    }

    /// <summary>
    /// Campaign recipients that never produced a chat message.
    ///
    /// <para>
    /// Without these a campaign report silently under-reports: a recipient whose send failed
    /// before a message row was written simply would not appear, and the report would look like
    /// the campaign reached fewer people than it tried to. They carry a negative synthetic id, so
    /// nothing downstream mistakes one for a message.
    /// </para>
    /// </summary>
    private async Task<List<ReportRowDto>> QueryUnsentRecipientsAsync(ReportQueryRequest request, int take)
    {
        if (take <= 0) return new List<ReportRowDto>();

        // Only meaningful when the report is scoped to campaigns; over the whole message table
        // this would attach every never-messaged recipient of every campaign ever run.
        if (request.CampaignIds is not { Count: > 0 }) return new List<ReportRowDto>();

        // An outgoing-only or incoming-only report is asking about messages; a recipient with none
        // does not belong in either answer.
        if (request.Directions is { Count: > 0 }) return new List<ReportRowDto>();

        var campaignIds = request.CampaignIds;

        var query = _dbContext.CampaignContacts.AsNoTracking()
            .Where(cc => campaignIds.Contains(cc.CampaignId))
            .Where(cc => !_dbContext.ChatMessages.Any(m => m.CampaignContactId == cc.Id && !m.IsDeleted));

        if (request.FailedOnly == true)
            query = query.Where(cc => cc.Status == MessageStatus.Failed || cc.ErrorMessage != null);

        return await query
            .OrderBy(cc => cc.Id)
            .Take(take)
            .Select(cc => new ReportRowDto
            {
                // Negative, so it can never collide with a real message id.
                Id = -cc.Id,
                Timestamp = cc.SentAt ?? cc.Campaign.CreatedAt,
                ContactName = cc.Contact.Name,
                ContactPhone = cc.Contact.Phone,
                CampaignName = cc.Campaign.Name,
                TemplateName = _dbContext.Templates
                    .Where(t => t.Id == cc.Campaign.TemplateId).Select(t => t.Name).FirstOrDefault(),
                ConnectionName = null,
                Direction = nameof(ChatMessageDirection.Outgoing),
                MessageType = "text",
                Status = cc.Status.ToString(),
                Content = null,
                SentAt = cc.SentAt,
                DeliveredAt = cc.DeliveredAt,
                ReadAt = cc.ReadAt,
                FailureReason = cc.ErrorMessage,
                Responded = false,
                ResponseMinutes = null
            })
            .ToListAsync();
    }

    // ── Filter options ───────────────────────────────────────────────────────

    public async Task<ReportFilterOptionsDto> GetFilterOptionsAsync()
    {
        // Distinct values actually present, the same approach as the audit filters: a dropdown
        // should never offer an option that returns nothing, and a new campaign should appear
        // without anyone editing a list.
        var messages = _dbContext.ChatMessages.AsNoTracking().Where(m => !m.IsDeleted);

        var campaigns = await _dbContext.Campaigns.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ReportOption { Id = c.Id, Name = c.Name })
            .ToListAsync();

        var connections = await _dbContext.Connections.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ReportOption { Id = c.Id, Name = c.Name })
            .ToListAsync();

        var templates = await _dbContext.Templates.AsNoTracking()
            .Where(t => _dbContext.Campaigns.Any(c => c.TemplateId == t.Id))
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .Distinct()
            .ToListAsync();

        var mediaTypes = await messages
            .Where(m => m.MediaType != null)
            .Select(m => m.MediaType!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        var bounds = await messages
            .GroupBy(_ => 1)
            .Select(g => new { Earliest = (DateTime?)g.Min(m => m.CreatedAt), Latest = (DateTime?)g.Max(m => m.CreatedAt) })
            .FirstOrDefaultAsync();

        return new ReportFilterOptionsDto
        {
            Campaigns = campaigns,
            Connections = connections,
            Templates = templates,
            // "text" is prepended because it is the absence of a media type, so it never appears
            // in the distinct list even though most rows are text.
            MessageTypes = new[] { "text" }.Concat(mediaTypes).ToList(),
            Directions = Enum.GetNames<ChatMessageDirection>().ToList(),
            Statuses = Enum.GetNames<ChatMessageStatus>().ToList(),
            EarliestRecord = bounds?.Earliest,
            LatestRecord = bounds?.Latest
        };
    }

    // ── Saved reports ────────────────────────────────────────────────────────

    public async Task<List<SavedReportDto>> GetSavedReportsAsync()
    {
        var userId = _currentUser.UserId;

        var definitions = await _dbContext.ReportDefinitions.AsNoTracking()
            .Where(r => r.IsShared || (userId != null && r.OwnerUserId == userId))
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync();

        return definitions.Select(d => ToDto(d, userId)).ToList();
    }

    public async Task<SavedReportDto> CreateSavedReportAsync(SaveReportRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A report needs a name.");

        var userId = _currentUser.UserId;

        // Per owner, not globally: two people may each keep their own "Weekly failures" without
        // one of them being told the name is taken by a report they cannot even see.
        var duplicate = await _dbContext.ReportDefinitions
            .AnyAsync(r => r.OwnerUserId == userId && r.Name.ToLower() == name.ToLower());

        if (duplicate)
            throw new InvalidOperationException($"You already have a saved report called \"{name}\".");

        var definition = new ReportDefinition
        {
            Name = name,
            Description = request.Description?.Trim(),
            ColumnsJson = JsonSerializer.Serialize(request.Columns ?? new List<string>(), JsonOptions),
            FiltersJson = JsonSerializer.Serialize(request.Filters ?? new ReportQueryRequest(), JsonOptions),
            OwnerUserId = userId,
            OwnerName = _currentUser.UserName,
            IsShared = request.IsShared,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.ReportDefinitions.Add(definition);
        await _dbContext.SaveChangesAsync();

        return ToDto(definition, userId);
    }

    public async Task<SavedReportDto> UpdateSavedReportAsync(int id, SaveReportRequest request)
    {
        var definition = await _dbContext.ReportDefinitions.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Saved report {id} was not found.");

        EnsureOwner(definition);

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A report needs a name.");

        definition.Name = name;
        definition.Description = request.Description?.Trim();
        definition.ColumnsJson = JsonSerializer.Serialize(request.Columns ?? new List<string>(), JsonOptions);
        definition.FiltersJson = JsonSerializer.Serialize(request.Filters ?? new ReportQueryRequest(), JsonOptions);
        definition.IsShared = request.IsShared;
        definition.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return ToDto(definition, _currentUser.UserId);
    }

    public async Task DeleteSavedReportAsync(int id)
    {
        var definition = await _dbContext.ReportDefinitions.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Saved report {id} was not found.");

        EnsureOwner(definition);

        _dbContext.ReportDefinitions.Remove(definition);
        await _dbContext.SaveChangesAsync();
    }

    public async Task TouchSavedReportAsync(int id)
    {
        try
        {
            var definition = await _dbContext.ReportDefinitions.FirstOrDefaultAsync(r => r.Id == id);
            if (definition is null) return;

            definition.LastRunAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Bookkeeping. Failing the run because the "last run" stamp did not save would be a
            // poor trade.
            _logger.LogWarning(ex, "Could not stamp LastRunAt on saved report {ReportId}.", id);
        }
    }

    /// <summary>
    /// Sharing makes a report readable, never writable. An administrator is not exempted here:
    /// the point of the flag is that the author decides what the report says.
    /// </summary>
    private void EnsureOwner(ReportDefinition definition)
    {
        var userId = _currentUser.UserId;
        if (userId is null || definition.OwnerUserId != userId)
        {
            throw new UnauthorizedAccessException("Only the report's owner can change or delete it.");
        }
    }

    private static SavedReportDto ToDto(ReportDefinition definition, int? currentUserId) => new()
    {
        Id = definition.Id,
        Name = definition.Name,
        Description = definition.Description,
        Columns = Deserialize<List<string>>(definition.ColumnsJson) ?? new List<string>(),
        Filters = Deserialize<ReportQueryRequest>(definition.FiltersJson) ?? new ReportQueryRequest(),
        IsShared = definition.IsShared,
        IsOwner = currentUserId != null && definition.OwnerUserId == currentUserId,
        OwnerName = definition.OwnerName,
        LastRunAt = definition.LastRunAt,
        CreatedAt = definition.CreatedAt,
        UpdatedAt = definition.UpdatedAt
    };

    /// <summary>
    /// Tolerant of a malformed stored value. These columns hold JSON written by an earlier version
    /// of this code; a report that lost its filters should still open with empty ones rather than
    /// take the whole list down.
    /// </summary>
    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch { return null; }
    }
}
