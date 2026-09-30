using System.Globalization;
using System.Linq.Expressions;
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
    private readonly Security.IAccessScope _accessScope;

    /// <summary>
    /// Upper bound on an export. Large enough for any real reporting need, small enough that one
    /// click cannot pull the whole message table into memory and hold it there while a PDF is
    /// laid out. Past this the caller is told to narrow the date range, which is the honest
    /// answer — a 200,000-row spreadsheet is not a report anyone reads.
    /// </summary>
    public const int MaxExportRows = 20000;

    /// <summary>
    /// Upper bound on any one filter's option list.
    ///
    /// These come from DISTINCT over the message table, which in a busy tenant has no natural
    /// ceiling — every failure reason Meta has ever returned, every contact ever messaged. A
    /// dropdown of ten thousand entries is unusable and the query that builds it is not free, so
    /// the list is capped and the free-text search stays the way to reach anything beyond it.
    /// </summary>
    private const int MaxFilterOptionValues = 500;

    /// <summary>
    /// Upper bound on the number of groups one aggregated report may return.
    ///
    /// Grouping by contact over a year is a legitimate request that produces an illegitimate
    /// answer — a table nobody scrolls and a response nobody should have to buffer. Past this the
    /// page reports the true group count and shows the largest groups, which is the answer the
    /// question was actually after.
    /// </summary>
    private const int MaxGroups = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ReportQueryService(
        AppDbContext dbContext,
        ICurrentUserService currentUser,
        ILogger<ReportQueryService> logger,
        Security.IAccessScope accessScope)
    {
        _accessScope = accessScope;
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── Catalogue ────────────────────────────────────────────────────────────

    /// <summary>
    /// The column catalogue, the report types and the groupings all come from
    /// <see cref="ReportCatalog"/> rather than being restated here, so the description the client
    /// renders and the predicate this service applies can never be two different lists.
    /// </summary>
    public IReadOnlyList<ReportColumnDto> GetColumns() => ReportCatalog.Columns;

    public ReportMetadataDto GetMetadata() => ReportCatalog.BuildMetadata();

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

    // ── Grouped query ────────────────────────────────────────────────────────

    public async Task<PagedResponse<ReportGroupRowDto>> QueryGroupedAsync(ReportQueryRequest request)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var type = ReportCatalog.ResolveType(request.ReportType);
        var groupBy = ReportCatalog.ResolveGroupByKey(type, request.GroupBy);

        // "None" is the row listing, which QueryAsync already answers. Returning an empty page
        // rather than throwing keeps the client's one code path honest: it asks for groups when
        // the user chose a grouping, and gets nothing back if that grouping turned out invalid.
        if (groupBy == ReportCatalog.NoGrouping)
        {
            return new PagedResponse<ReportGroupRowDto>
            {
                Items = new List<ReportGroupRowDto>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize
            };
        }

        var query = BuildQuery(request);
        var groups = await AggregateByAsync(query, groupBy);

        return new PagedResponse<ReportGroupRowDto>
        {
            Items = groups.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = groups.Count,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>Every group for these filters, ordered largest first. Used by the export too.</summary>
    public Task<List<ReportGroupRowDto>> QueryAllGroupedAsync(ReportQueryRequest request)
    {
        var type = ReportCatalog.ResolveType(request.ReportType);
        var groupBy = ReportCatalog.ResolveGroupByKey(type, request.GroupBy);

        return groupBy == ReportCatalog.NoGrouping
            ? Task.FromResult(new List<ReportGroupRowDto>())
            : AggregateByAsync(BuildQuery(request), groupBy);
    }

    /// <summary>
    /// Dispatches one grouping key to its aggregate.
    ///
    /// <para>
    /// Entity groupings key on the foreign key and resolve names afterwards rather than grouping
    /// on the joined name. Grouping on a name merges two campaigns that happen to share one, and
    /// on this provider it also forces the join into the GROUP BY — the id is both the correct
    /// identity and the cheaper one.
    /// </para>
    /// <para>
    /// Date buckets group on extracted parts of the timestamp — every one a plain EXTRACT this
    /// provider translates. Deriving the boundary in C# instead would mean loading every matching
    /// message just to decide which bucket it falls in.
    /// </para>
    /// </summary>
    private async Task<List<ReportGroupRowDto>> AggregateByAsync(IQueryable<ChatMessage> query, string groupBy)
    {
        switch (groupBy)
        {
            // Date buckets group on extracted parts rather than on a truncated timestamp: every
            // part is a plain EXTRACT this provider translates, where date_trunc is not exposed by
            // its EF integration at all and a C#-side truncation would mean loading every message
            // to bucket it. Sorted chronologically afterwards — for a time series, "largest first"
            // is the wrong reading order even though it is the right cap.
            case "day":
            {
                var buckets = await AggregateAsync(
                    query,
                    m => new { m.CreatedAt.Year, m.CreatedAt.Month, m.CreatedAt.Day },
                    k => $"{k.Year:D4}-{k.Month:D2}-{k.Day:D2}");

                return Chronological(Label(buckets, k =>
                    new DateTime(k.Year, k.Month, k.Day).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
            }

            case "week":
            {
                // Seven-day buckets counted from 1 January, not ISO weeks: the ISO week number has
                // no translatable accessor here, and a bucket that is honestly labelled by the day
                // it starts on is more useful than one labelled "W32" either way.
                var buckets = await AggregateAsync(
                    query,
                    m => new { m.CreatedAt.Year, Week = (m.CreatedAt.DayOfYear - 1) / 7 },
                    k => $"{k.Year:D4}-W{k.Week:D2}");

                return Chronological(Label(buckets, k =>
                    "Week of " + new DateTime(k.Year, 1, 1).AddDays(k.Week * 7)
                        .ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
            }

            case "month":
            {
                var buckets = await AggregateAsync(
                    query,
                    m => new { m.CreatedAt.Year, m.CreatedAt.Month },
                    k => $"{k.Year:D4}-{k.Month:D2}");

                return Chronological(Label(buckets, k =>
                    new DateTime(k.Year, k.Month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture)));
            }

            case "campaign":
            {
                var buckets = await AggregateAsync(query, m => m.CampaignId, k => k?.ToString() ?? "none");
                var ids = IdsIn(buckets);
                var names = await _dbContext.Campaigns.AsNoTracking()
                    .Where(c => ids.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.Name);

                return Label(buckets, k => Lookup(names, k, "Not from a campaign", "campaign"));
            }

            case "template":
            {
                var buckets = await AggregateAsync(
                    query,
                    m => m.Campaign != null ? m.Campaign.TemplateId : (int?)null,
                    k => k?.ToString() ?? "none");

                var ids = IdsIn(buckets);
                var names = await _dbContext.Templates.AsNoTracking()
                    .Where(t => ids.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, t => t.Name);

                return Label(buckets, k => Lookup(names, k, "No template", "template"));
            }

            case "connection":
            {
                var buckets = await AggregateAsync(query, m => m.ConnectionId, k => k?.ToString() ?? "none");
                var ids = IdsIn(buckets);
                var names = await _dbContext.Connections.AsNoTracking()
                    .Where(c => ids.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.Name);

                return Label(buckets, k => Lookup(names, k, "No connection", "connection"));
            }

            case "contact":
            {
                var buckets = await AggregateAsync(query, m => m.ContactId, k => k?.ToString() ?? "none");
                var ids = IdsIn(buckets);
                var names = await _dbContext.Contacts.AsNoTracking()
                    .Where(c => ids.Contains(c.Id))
                    .ToDictionaryAsync(
                        c => c.Id,
                        c => c.Name != null && c.Name != "" ? c.Name + " (" + c.Phone + ")" : c.Phone);

                return Label(buckets, k => Lookup(names, k, "No contact", "contact"));
            }

            case "agent":
            {
                var buckets = await AggregateAsync(
                    query,
                    m => m.Contact != null ? m.Contact.AssignedTo : null,
                    k => k ?? "unassigned");

                return Label(buckets, k => string.IsNullOrWhiteSpace(k) ? "Unassigned" : k);
            }

            case "direction":
            {
                var buckets = await AggregateAsync(query, m => m.Direction, k => k.ToString());
                return Label(buckets, k => k.ToString());
            }

            case "status":
            {
                var buckets = await AggregateAsync(query, m => m.Status, k => k.ToString());
                return Label(buckets, k => k.ToString());
            }

            case "messageType":
            {
                // The same classification the row projection reports, expressed once more here
                // because a CASE in the GROUP BY is what keeps this a single aggregate query.
                var buckets = await AggregateAsync(
                    query,
                    m => m.IsTemplate ? "Template" : m.MediaType != null ? "Media" : "Text",
                    k => k);

                return Label(buckets, k => k);
            }

            case "failureReason":
            {
                var buckets = await AggregateAsync(query, m => m.ErrorMessage, k => k ?? "none");
                return Label(buckets, k => string.IsNullOrWhiteSpace(k) ? "No failure" : k);
            }

            default:
                return new List<ReportGroupRowDto>();
        }
    }

    /// <summary>
    /// The counting half of a grouped report: one aggregate query for the delivery measures, and
    /// a second for the reply count.
    ///
    /// <para>
    /// Two queries rather than one because "was this answered" is an EXISTS over the message table
    /// correlated to each row, and a provider that will happily put that in a WHERE will not
    /// reliably put it inside a COUNT within a GROUP BY. Filtering first and grouping the survivors
    /// asks the same question in a form every provider translates.
    /// </para>
    /// </summary>
    private async Task<List<GroupBucket<TKey>>> AggregateAsync<TKey>(
        IQueryable<ChatMessage> query,
        Expression<Func<ChatMessage, TKey>> keySelector,
        Func<TKey, string> keyText)
    {
        var measures = await query
            .GroupBy(keySelector)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
                Outgoing = g.Count(m => m.Direction == ChatMessageDirection.Outgoing),
                Incoming = g.Count(m => m.Direction == ChatMessageDirection.Incoming),
                // Read implies delivered: a message the recipient opened plainly arrived, and a
                // "delivered" count that excluded read messages would fall as engagement rose.
                Delivered = g.Count(m => m.Status == ChatMessageStatus.Delivered || m.Status == ChatMessageStatus.Read),
                Read = g.Count(m => m.Status == ChatMessageStatus.Read),
                Failed = g.Count(m => m.Status == ChatMessageStatus.Failed),
                FirstAt = g.Min(m => m.CreatedAt),
                LastAt = g.Max(m => m.CreatedAt)
            })
            .OrderByDescending(x => x.Total)
            .Take(MaxGroups)
            .ToListAsync();

        var replied = await query
            .Where(m => m.Direction == ChatMessageDirection.Outgoing
                        && _dbContext.ChatMessages.Any(r => r.ConversationId == m.ConversationId
                            && r.Direction == ChatMessageDirection.Incoming
                            && !r.IsDeleted
                            && r.CreatedAt > m.CreatedAt))
            .GroupBy(keySelector)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();

        var repliedByKey = new Dictionary<string, int>();
        foreach (var entry in replied)
        {
            repliedByKey[keyText(entry.Key)] = entry.Count;
        }

        return measures.Select(m =>
        {
            var text = keyText(m.Key);
            repliedByKey.TryGetValue(text, out var responded);

            return new GroupBucket<TKey>(m.Key, new ReportGroupRowDto
            {
                Key = text,
                Total = m.Total,
                Outgoing = m.Outgoing,
                Incoming = m.Incoming,
                Delivered = m.Delivered,
                Read = m.Read,
                Failed = m.Failed,
                Responded = responded,
                // Against outgoing, not against everything: a reply rate measured over a total
                // that already includes the replies would rise on its own.
                ResponseRate = m.Outgoing > 0 ? Math.Round(responded * 100.0 / m.Outgoing, 1) : null,
                FirstAt = m.FirstAt,
                LastAt = m.LastAt
            });
        }).ToList();
    }

    /// <summary>An aggregated group, still carrying its typed key so the caller can name it.</summary>
    private sealed record GroupBucket<TKey>(TKey Key, ReportGroupRowDto Row);

    /// <summary>
    /// Puts a set of date buckets back into time order.
    ///
    /// The aggregate deliberately takes the largest groups when it has to cap, which is right for
    /// a campaign or an agent breakdown and wrong for a time series — a chart of days ordered by
    /// volume is not a chart of anything. The keys are zero-padded for exactly this reason, so
    /// ordinary string ordering is chronological ordering.
    /// </summary>
    private static List<ReportGroupRowDto> Chronological(List<ReportGroupRowDto> groups) =>
        groups.OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

    /// <summary>Applies display labels and drops the typed keys.</summary>
    private static List<ReportGroupRowDto> Label<TKey>(
        IEnumerable<GroupBucket<TKey>> buckets,
        Func<TKey, string> label)
    {
        return buckets.Select(b =>
        {
            b.Row.Label = label(b.Key);
            return b.Row;
        }).ToList();
    }

    /// <summary>
    /// The ids a set of groups produced, ready to look names up with. One query for the whole page
    /// rather than a join carried through the aggregate — the aggregate groups on the foreign key,
    /// which is both the correct identity and the cheaper one to group on.
    /// </summary>
    private static List<int> IdsIn(IReadOnlyList<GroupBucket<int?>> buckets) =>
        buckets.Where(b => b.Key.HasValue).Select(b => b.Key!.Value).Distinct().ToList();

    /// <summary>
    /// Names one entity group.
    ///
    /// "The row had no campaign" and "the campaign has since been deleted" are different facts and
    /// get different labels. Collapsing both into one — which they did — produced two rows in the
    /// same table reading "Not from a campaign", which looks like a bug in the grouping rather than
    /// like history.
    /// </summary>
    private static string Lookup(
        IReadOnlyDictionary<int, string> names,
        int? key,
        string noneLabel,
        string deletedNoun)
    {
        if (!key.HasValue) return noneLabel;

        return names.TryGetValue(key.Value, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"Deleted {deletedNoun} #{key.Value}";
    }

    /// <summary>
    /// The filtered message query, before ordering or paging.
    ///
    /// Soft-deleted messages are excluded throughout: they have been removed from the product, and
    /// a report that still counted them would disagree with every other screen.
    /// </summary>
    /// <inheritdoc />
    public async Task<ReportSummaryDto> GetSummaryAsync(ReportQueryRequest request)
    {
        request ??= new ReportQueryRequest();

        // The same predicate the table is built from. Anything else and the cards would describe a
        // different set of messages than the rows underneath them.
        var query = BuildQuery(request);

        // One pass over the filtered set, counted in SQL. Grouping by status and direction together
        // means every KPI comes from a single scan rather than five separate COUNT queries.
        var byStatus = await query
            .GroupBy(m => new { m.Status, m.Direction })
            .Select(g => new { g.Key.Status, g.Key.Direction, Count = g.Count() })
            .ToListAsync();

        var total = byStatus.Sum(x => x.Count);

        int CountWhere(Func<ChatMessageStatus, ChatMessageDirection, bool> predicate) =>
            byStatus.Where(x => predicate(x.Status, x.Direction)).Sum(x => x.Count);

        // "Delivered" counts anything that reached the handset, which includes messages since read.
        // Reporting them as separate, non-overlapping buckets would show a delivered count that
        // falls as customers open their messages.
        var delivered = CountWhere((s, _) => s is ChatMessageStatus.Delivered or ChatMessageStatus.Read);
        var read = CountWhere((s, _) => s == ChatMessageStatus.Read);
        var failed = CountWhere((s, _) => s == ChatMessageStatus.Failed);
        var responses = CountWhere((_, d) => d == ChatMessageDirection.Incoming);

        // ── Per-KPI sparklines ──────────────────────────────────────────────────────────────────
        // One extra grouped scan — by day, status and direction together — gives every card's
        // trend line from the same filtered rows as its headline number, so a card's sparkline
        // can never depict a different set of messages than the figure above it.
        var dailyByStatus = await query
            .GroupBy(m => new { m.CreatedAt.Year, m.CreatedAt.Month, m.CreatedAt.Day, m.Status, m.Direction })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Status, g.Key.Direction, Count = g.Count() })
            .ToListAsync();

        var trendDates = dailyByStatus
            .Select(d => new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // A single point is a number, not a trend — the card shows the figure alone rather than a
        // line with nowhere to go.
        List<int> TrendFor(Func<ChatMessageStatus, ChatMessageDirection, bool> predicate) =>
            trendDates.Count < 2
                ? []
                : trendDates
                    .Select(date => dailyByStatus
                        .Where(d => d.Year == date.Year && d.Month == date.Month && d.Day == date.Day
                                    && predicate(d.Status, d.Direction))
                        .Sum(d => d.Count))
                    .ToList();

        var summary = new ReportSummaryDto { Total = total };

        // ── Comparison with the preceding window ────────────────────────────────────────────────
        // Only possible when the request actually bounded a period. Without one there is no
        // "previous" to speak of, and inventing a window would put a trend on the card that the
        // user never asked to measure.
        Dictionary<string, int>? previous = null;
        string? comparisonLabel = null;

        if (request.From.HasValue && request.To.HasValue)
        {
            var from = request.From.Value.Date;
            var to = request.To.Value.Date;
            var span = to - from;

            var previousTo = from.AddDays(-1);
            var previousFrom = previousTo - span;

            var previousRequest = ClonePeriod(request, previousFrom, previousTo);
            var previousQuery = BuildQuery(previousRequest);

            var previousByStatus = await previousQuery
                .GroupBy(m => new { m.Status, m.Direction })
                .Select(g => new { g.Key.Status, g.Key.Direction, Count = g.Count() })
                .ToListAsync();

            int PreviousWhere(Func<ChatMessageStatus, ChatMessageDirection, bool> predicate) =>
                previousByStatus.Where(x => predicate(x.Status, x.Direction)).Sum(x => x.Count);

            previous = new Dictionary<string, int>
            {
                ["total"] = previousByStatus.Sum(x => x.Count),
                ["delivered"] = PreviousWhere((st, _) => st is ChatMessageStatus.Delivered or ChatMessageStatus.Read),
                ["read"] = PreviousWhere((st, _) => st == ChatMessageStatus.Read),
                ["responses"] = PreviousWhere((_, d) => d == ChatMessageDirection.Incoming),
                ["failed"] = PreviousWhere((st, _) => st == ChatMessageStatus.Failed)
            };

            comparisonLabel = $"vs {previousFrom:MMM dd} - {previousTo:MMM dd}";
        }

        ReportKpiDto Kpi(string key, string label, int value, List<int> trend) => new()
        {
            Key = key,
            Label = label,
            Value = value,
            PreviousValue = previous?.GetValueOrDefault(key),
            ChangePercent = PercentChange(previous?.GetValueOrDefault(key), value),
            ComparisonLabel = previous is null ? null : comparisonLabel,
            Trend = trend
        };

        summary.Kpis =
        [
            Kpi("total", "Total Messages", total,
                TrendFor((_, _) => true)),
            Kpi("delivered", "Delivered", delivered,
                TrendFor((st, _) => st is ChatMessageStatus.Delivered or ChatMessageStatus.Read)),
            Kpi("read", "Read", read,
                TrendFor((st, _) => st == ChatMessageStatus.Read)),
            Kpi("responses", "Responses", responses,
                TrendFor((_, d) => d == ChatMessageDirection.Incoming)),
            Kpi("failed", "Failed", failed,
                TrendFor((st, _) => st == ChatMessageStatus.Failed))
        ];

        // ── Activity over time ──────────────────────────────────────────────────────────────────
        // Grouped by date parts rather than by DateTrunc, which Npgsql cannot translate.
        var activity = await query
            .GroupBy(m => new { m.CreatedAt.Year, m.CreatedAt.Month, m.CreatedAt.Day })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Day,
                Count = g.Count()
            })
            .ToListAsync();

        summary.Activity = activity
            .Select(a => new ReportActivityPointDto
            {
                Date = new DateTime(a.Year, a.Month, a.Day, 0, 0, 0, DateTimeKind.Utc),
                Count = a.Count
            })
            .OrderBy(a => a.Date)
            .ToList();

        // ── Message type breakdown ──────────────────────────────────────────────────────────────
        // MediaType is null for a plain text message, which is the commonest kind, so the null case
        // is named rather than dropped.
        var byType = await query
            .GroupBy(m => m.MediaType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        summary.ByType = byType
            .Select(t => new ReportTypeSliceDto
            {
                Type = string.IsNullOrWhiteSpace(t.Type) ? "Text" : t.Type,
                Count = t.Count,
                Percent = total == 0 ? 0 : Math.Round((double)t.Count / total * 100, 1)
            })
            .OrderByDescending(t => t.Count)
            .ToList();

        // ── Top campaigns ───────────────────────────────────────────────────────────────────────
        var topCampaigns = await query
            .Where(m => m.CampaignId != null && m.Campaign != null)
            .GroupBy(m => new { Id = m.CampaignId!.Value, m.Campaign!.Name })
            .Select(g => new ReportTopCampaignDto
            {
                CampaignId = g.Key.Id,
                Name = g.Key.Name,
                Count = g.Count()
            })
            .OrderByDescending(c => c.Count)
            .Take(5)
            .ToListAsync();

        summary.TopCampaigns = topCampaigns;

        return summary;
    }

    /// <summary>
    /// The same request over a different period. Every other filter is carried across unchanged, so
    /// the comparison measures the passage of time and nothing else.
    /// </summary>
    private static ReportQueryRequest ClonePeriod(ReportQueryRequest source, DateTime from, DateTime to) => new()
    {
        ReportType = source.ReportType,
        DataSection = source.DataSection,
        GroupBy = source.GroupBy,
        From = from,
        To = to,
        CampaignIds = source.CampaignIds,
        ConnectionIds = source.ConnectionIds,
        ContactIds = source.ContactIds,
        MessageTypes = source.MessageTypes,
        Directions = source.Directions,
        Statuses = source.Statuses,
        TemplateNames = source.TemplateNames,
        Search = source.Search
    };

    /// <summary>
    /// Change from one figure to the next, as a percentage.
    ///
    /// Null when there is no previous figure to compare against, and null when the previous figure
    /// was zero — "up from nothing" is not a percentage, and rendering it as +100% would understate
    /// what actually happened.
    /// </summary>
    private static double? PercentChange(int? previous, int current)
    {
        if (previous is null or 0) return null;
        return Math.Round((double)(current - previous.Value) / previous.Value * 100, 1);
    }

    private IQueryable<ChatMessage> BuildQuery(ReportQueryRequest request)
    {
        var query = _dbContext.ChatMessages.AsNoTracking().Where(m => !m.IsDeleted);

        // Connection scoping: a restricted user reports only on the connections assigned to them.
        if (!_accessScope.IsUnrestricted)
        {
            var allowed = _accessScope.AllowedConnectionIdsQuery();
            query = query.Where(m => m.ConnectionId != null && allowed.Contains(m.ConnectionId.Value));
        }

        // The report type's own scope comes first, so everything below narrows within it rather
        // than alongside it. Resolved through the catalogue rather than taken from the request:
        // a section a type does not offer must not apply just because it was posted.
        query = ApplyDataSection(query, request);

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
            // Three kinds, classified from the message rather than stored on it: a template send,
            // an attachment, or plain text. The raw WhatsApp media type ("image", "document") is
            // still filterable — it just arrives as a value alongside these, and any value that is
            // not one of the three is matched against MediaType directly. That is what lets one
            // control answer both "show me media" and "show me the PDFs".
            var wantsTemplate = request.MessageTypes.Any(t => string.Equals(t, "Template", StringComparison.OrdinalIgnoreCase));
            var wantsMedia = request.MessageTypes.Any(t => string.Equals(t, "Media", StringComparison.OrdinalIgnoreCase));
            var wantsText = request.MessageTypes.Any(t => string.Equals(t, "Text", StringComparison.OrdinalIgnoreCase));

            var mediaTypes = request.MessageTypes
                .Where(t => !string.Equals(t, "Template", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(t, "Media", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(t, "Text", StringComparison.OrdinalIgnoreCase))
                .ToList();

            query = query.Where(m =>
                (wantsTemplate && m.IsTemplate) ||
                (wantsMedia && m.MediaType != null) ||
                (wantsText && !m.IsTemplate && m.MediaType == null) ||
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

        if (request.FailureReasons is { Count: > 0 })
        {
            var reasons = request.FailureReasons;
            query = query.Where(m => m.ErrorMessage != null && reasons.Contains(m.ErrorMessage));
        }

        if (request.Agents is { Count: > 0 })
        {
            // Through the contact: ownership in this product lives there, not on the message.
            var agents = request.Agents;
            query = query.Where(m => m.Contact != null && m.Contact.AssignedTo != null
                                     && agents.Contains(m.Contact.AssignedTo));
        }

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
    /// Narrows the message set to the chosen report type's data section.
    ///
    /// <para>
    /// Every branch is a predicate over the same message table rather than a different query per
    /// report type. One source means one place where "deleted messages are excluded" and one place
    /// where a date range is interpreted — six parallel queries would mean six chances for a
    /// report type to quietly disagree with the others about what a message is.
    /// </para>
    /// <para>
    /// The section key is resolved against the type that was actually asked for, so a request
    /// carrying a section from a different type falls back to that type's default rather than
    /// applying a scope the user could not have chosen.
    /// </para>
    /// </summary>
    private IQueryable<ChatMessage> ApplyDataSection(IQueryable<ChatMessage> query, ReportQueryRequest request)
    {
        var type = ReportCatalog.ResolveType(request.ReportType);
        var section = ReportCatalog.ResolveSectionKey(type, request.DataSection);

        return section switch
        {
            "outgoing" => query.Where(m => m.Direction == ChatMessageDirection.Outgoing),
            "incoming" => query.Where(m => m.Direction == ChatMessageDirection.Incoming),

            "campaigns" => query.Where(m => m.CampaignId != null),
            "campaignDelivered" => query.Where(m => m.CampaignId != null
                && (m.Status == ChatMessageStatus.Delivered || m.Status == ChatMessageStatus.Read)),
            "campaignFailed" => query.Where(m => m.CampaignId != null
                && (m.Status == ChatMessageStatus.Failed || m.ErrorMessage != null)),

            "templates" => query.Where(m => m.IsTemplate),
            "templateFailures" => query.Where(m => m.IsTemplate
                && (m.Status == ChatMessageStatus.Failed || m.ErrorMessage != null)),

            // Ordinary chat is defined as "not sent by a campaign". A conversation report that
            // included campaign blasts would be measuring marketing reach, not conversations.
            "conversations" => query.Where(m => m.CampaignId == null),

            "responded" => query.Where(m => m.Direction == ChatMessageDirection.Outgoing
                && _dbContext.ChatMessages.Any(r => r.ConversationId == m.ConversationId
                    && r.Direction == ChatMessageDirection.Incoming
                    && !r.IsDeleted
                    && r.CreatedAt > m.CreatedAt)),
            "unanswered" => query.Where(m => m.Direction == ChatMessageDirection.Outgoing
                && !_dbContext.ChatMessages.Any(r => r.ConversationId == m.ConversationId
                    && r.Direction == ChatMessageDirection.Incoming
                    && !r.IsDeleted
                    && r.CreatedAt > m.CreatedAt)),

            "failures" => query.Where(m => m.Status == ChatMessageStatus.Failed || m.ErrorMessage != null),

            // "Not delivered" is about outgoing messages only — an incoming message has no
            // delivery of ours to report on, and counting them would inflate the failure picture.
            "undelivered" => query.Where(m => m.Direction == ChatMessageDirection.Outgoing
                && m.DeliveredAt == null
                && m.Status != ChatMessageStatus.Delivered
                && m.Status != ChatMessageStatus.Read),

            // "all" and "messages" are the unscoped case; they differ in label, not in predicate.
            _ => query
        };
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
                AdSource = m.Contact != null ? (m.Contact.AdHeadline ?? m.Contact.AdSourceId) : null,
                CampaignName = m.Campaign != null ? m.Campaign.Name : null,
                TemplateName = m.Campaign != null
                    ? _dbContext.Templates.Where(t => t.Id == m.Campaign.TemplateId).Select(t => t.Name).FirstOrDefault()
                    : null,
                ConnectionName = m.Connection != null ? m.Connection.Name : null,
                Direction = m.Direction.ToString(),
                // Classified rather than reported raw: "image"/"document"/null is how the row is
                // stored, but Template/Media/Text is the distinction a reader is looking for. The
                // raw value is still carried, as MediaType, for anyone who needs it.
                MessageType = m.IsTemplate ? "Template" : m.MediaType != null ? "Media" : "Text",
                MediaType = m.MediaType,
                Status = m.Status.ToString(),
                Content = m.Text,
                // One column answering "what was sent": the template for a campaign message, the
                // body for a typed one, and the attachment when there is no body to show.
                TemplateOrContent = m.Campaign != null
                    ? _dbContext.Templates.Where(t => t.Id == m.Campaign.TemplateId).Select(t => t.Name).FirstOrDefault()
                    : (m.Text != "" ? m.Text : (m.MediaFileName ?? m.MediaType)),
                Agent = m.Contact != null ? m.Contact.AssignedTo : null,
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
                AdSource = cc.Contact.AdHeadline ?? cc.Contact.AdSourceId,
                CampaignName = cc.Campaign.Name,
                TemplateName = _dbContext.Templates
                    .Where(t => t.Id == cc.Campaign.TemplateId).Select(t => t.Name).FirstOrDefault(),
                ConnectionName = null,
                Direction = nameof(ChatMessageDirection.Outgoing),
                // A campaign recipient is always a template send by definition — that is what a
                // campaign is — even though no message row was ever written for it.
                MessageType = "Template",
                MediaType = null,
                Status = cc.Status.ToString(),
                Content = null,
                TemplateOrContent = _dbContext.Templates
                    .Where(t => t.Id == cc.Campaign.TemplateId).Select(t => t.Name).FirstOrDefault(),
                Agent = cc.Contact.AssignedTo,
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

        // Only contacts that actually appear in the message table, most recently active first and
        // capped. The contact book runs to tens of thousands of rows in a real tenant; a dropdown
        // is not a place to render one, and a contact with no messages can only ever return an
        // empty report.
        var contacts = await messages
            .Where(m => m.ContactId != null)
            .GroupBy(m => m.ContactId!.Value)
            .Select(g => new { ContactId = g.Key, LastAt = g.Max(m => m.CreatedAt) })
            .OrderByDescending(x => x.LastAt)
            .Take(MaxFilterOptionValues)
            .Join(_dbContext.Contacts.AsNoTracking(),
                x => x.ContactId,
                c => c.Id,
                (x, c) => new ReportOption
                {
                    Id = c.Id,
                    // Phone included in the label because that is what the Sender / Contact column
                    // shows, and a list of bare names is ambiguous the moment two contacts share one.
                    Name = c.Name != null && c.Name != "" ? c.Name + " (" + c.Phone + ")" : c.Phone
                })
            .ToListAsync();

        var failureReasons = await messages
            .Where(m => m.ErrorMessage != null && m.ErrorMessage != "")
            .Select(m => m.ErrorMessage!)
            .Distinct()
            .OrderBy(r => r)
            .Take(MaxFilterOptionValues)
            .ToListAsync();

        // Agents come from the contacts those messages belong to, not from the user table: the
        // question a report asks is "whose contacts were these", and listing every user would
        // offer options that cannot match a row.
        var agents = await messages
            .Where(m => m.Contact != null && m.Contact.AssignedTo != null && m.Contact.AssignedTo != "")
            .Select(m => m.Contact!.AssignedTo!)
            .Distinct()
            .OrderBy(a => a)
            .Take(MaxFilterOptionValues)
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
            // The three classifications first — they are what the Message Type column reports and
            // neither is a stored value, so neither can come from a DISTINCT — then the raw media
            // types that are present, for narrowing further.
            MessageTypes = new[] { "Template", "Text", "Media" }.Concat(mediaTypes).ToList(),
            Directions = Enum.GetNames<ChatMessageDirection>().ToList(),
            Statuses = Enum.GetNames<ChatMessageStatus>().ToList(),
            Contacts = contacts,
            FailureReasons = failureReasons,
            Agents = agents,
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

    /// <summary>
    /// Maps a stored definition to what the list renders.
    ///
    /// The report type, section and grouping live inside the stored filter set rather than in
    /// columns of their own: they are part of the query the report *is*, and keeping them there
    /// means a saved report is one JSON document that replays exactly, with no schema change and
    /// nothing to migrate when the catalogue gains a type.
    /// </summary>
    private static SavedReportDto ToDto(ReportDefinition definition, int? currentUserId)
    {
        var filters = Deserialize<ReportQueryRequest>(definition.FiltersJson) ?? new ReportQueryRequest();

        return new SavedReportDto
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Columns = Deserialize<List<string>>(definition.ColumnsJson) ?? new List<string>(),
            Filters = filters,
            ReportTypeLabel = ReportCatalog.LabelForType(filters.ReportType),
            DataSectionLabel = ReportCatalog.LabelForSection(filters.ReportType, filters.DataSection),
            GroupByLabel = ReportCatalog.LabelForGroupBy(filters.ReportType, filters.GroupBy),
            IsShared = definition.IsShared,
            IsOwner = currentUserId != null && definition.OwnerUserId == currentUserId,
            OwnerName = definition.OwnerName,
            LastRunAt = definition.LastRunAt,
            CreatedAt = definition.CreatedAt,
            UpdatedAt = definition.UpdatedAt
        };
    }

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
