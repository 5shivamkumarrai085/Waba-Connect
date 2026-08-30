using WhatsAppCampaignApi.Models.DTOs.Reporting;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// The report builder's vocabulary: every column, grouping, report type and data section the
/// system offers, in one place.
///
/// <para>
/// Static because it describes the shape of the schema rather than the contents of the database —
/// what a row of a messaging report *can* say does not change between deployments, whereas which
/// campaigns exist does. The values inside a filter (campaigns, templates, agents, failure
/// reasons) are read from the data and never listed here; see
/// <c>ReportQueryService.GetFilterOptionsAsync</c>.
/// </para>
/// <para>
/// Serving this to the client rather than duplicating it there is what stops the two from
/// disagreeing: the same section key the client shows in a dropdown is the one this file resolves
/// into the predicate that scopes the query.
/// </para>
/// </summary>
public static class ReportCatalog
{
    public const string DefaultReportTypeKey = "custom";
    public const string NoGrouping = "none";

    // ── Columns ──────────────────────────────────────────────────────────────

    private static readonly ReportColumnDto[] AllColumns =
    {
        new() { Key = "timestamp", Label = "Date & Time" },
        new() { Key = "contactPhone", Label = "Sender / Contact" },
        new() { Key = "contactName", Label = "Contact Name" },
        new() { Key = "connectionName", Label = "WABA / Connection" },
        new() { Key = "campaignName", Label = "Campaign" },
        new()
        {
            Key = "templateOrContent",
            Label = "Template / Content",
            DerivedNote = "The template name for a campaign message; the message's own text or media otherwise."
        },
        new()
        {
            Key = "templateName",
            Label = "Template",
            DerivedNote = "Resolved through the campaign — a message stores no template of its own."
        },
        new() { Key = "content", Label = "Message Content" },
        new()
        {
            Key = "messageType",
            Label = "Message Type",
            DerivedNote = "Template, Media or Text, classified from the message itself."
        },
        new()
        {
            Key = "mediaType",
            Label = "Media Type",
            DerivedNote = "The attachment's own type as WhatsApp reported it. Blank for a text message."
        },
        new() { Key = "direction", Label = "Direction" },
        new() { Key = "status", Label = "Status" },
        new() { Key = "sentAt", Label = "Sent At" },
        new() { Key = "deliveredAt", Label = "Delivered At" },
        new() { Key = "readAt", Label = "Read At" },
        new() { Key = "failureReason", Label = "Failure Reason" },
        new()
        {
            Key = "agent",
            Label = "Agent",
            DerivedNote = "The contact's assignee — a message carries no agent of its own."
        },
        new()
        {
            Key = "responded",
            Label = "Response",
            DerivedNote = "Whether an incoming message followed this one in the same conversation."
        },
        new()
        {
            Key = "responseMinutes",
            Label = "Response Time",
            DerivedNote = "Gap to that reply. Blank when there was none."
        }
    };

    /// <summary>
    /// The twelve columns a report opens with. Ordered as they are read rather than as they are
    /// stored: when it happened, who it was with, which connection carried it, then what was sent
    /// and what became of it.
    /// </summary>
    private static readonly string[] DefaultColumnKeys =
    {
        "timestamp", "contactPhone", "connectionName", "campaignName", "templateOrContent",
        "messageType", "direction", "status", "deliveredAt", "readAt", "responded", "responseMinutes"
    };

    /// <summary>
    /// Stamps <see cref="ReportColumnDto.DefaultVisible"/> from <see cref="DefaultColumnKeys"/>
    /// rather than repeating the answer on each column.
    ///
    /// The two would otherwise be a pair of lists that have to be kept in step by hand, and the
    /// failure mode of them drifting — a column that reports itself as a default but is not one —
    /// is invisible until someone notices a report opening with the wrong columns.
    /// </summary>
    static ReportCatalog()
    {
        var defaults = new HashSet<string>(DefaultColumnKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var column in AllColumns)
        {
            column.DefaultVisible = defaults.Contains(column.Key);
        }
    }

    public static IReadOnlyList<ReportColumnDto> Columns => AllColumns;

    /// <summary>
    /// The aggregated table's columns. Described the same way as the row columns so the client
    /// renders both from data rather than carrying a hardcoded header row for the grouped case.
    /// </summary>
    public static IReadOnlyList<ReportColumnDto> GroupColumns { get; } = new ReportColumnDto[]
    {
        new() { Key = "label", Label = "Group", DefaultVisible = true },
        new() { Key = "total", Label = "Messages", DefaultVisible = true },
        new() { Key = "outgoing", Label = "Outgoing", DefaultVisible = true },
        new() { Key = "incoming", Label = "Incoming", DefaultVisible = true },
        new() { Key = "delivered", Label = "Delivered", DefaultVisible = true },
        new() { Key = "read", Label = "Read", DefaultVisible = true },
        new() { Key = "failed", Label = "Failed", DefaultVisible = true },
        new() { Key = "responded", Label = "Responded", DefaultVisible = true },
        new() { Key = "responseRate", Label = "Response Rate", DefaultVisible = true },
        new() { Key = "firstAt", Label = "First", DefaultVisible = false },
        new() { Key = "lastAt", Label = "Last", DefaultVisible = false }
    };

    // ── Groupings ────────────────────────────────────────────────────────────

    private static readonly ReportOptionDto[] AllGroupBys =
    {
        new() { Key = NoGrouping, Label = "None", Description = "List every matching message." },
        new() { Key = "day", Label = "Day" },
        new() { Key = "week", Label = "Week" },
        new() { Key = "month", Label = "Month" },
        new() { Key = "campaign", Label = "Campaign" },
        new() { Key = "template", Label = "Template" },
        new() { Key = "connection", Label = "WABA / Connection" },
        new() { Key = "contact", Label = "Sender / Contact" },
        new() { Key = "agent", Label = "Agent" },
        new() { Key = "direction", Label = "Direction" },
        new() { Key = "status", Label = "Status" },
        new() { Key = "messageType", Label = "Message Type" },
        new() { Key = "failureReason", Label = "Failure Reason" }
    };

    public static IReadOnlyList<ReportOptionDto> GroupBys => AllGroupBys;

    // ── Data sections ────────────────────────────────────────────────────────

    /// <summary>
    /// Every section key the product knows, with its label. A report type names the subset it
    /// offers; the predicate for each lives in <c>ReportQueryService.ApplyDataSection</c>, keyed
    /// by the same string.
    /// </summary>
    private static readonly Dictionary<string, ReportOptionDto> Sections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = new() { Key = "all", Label = "All Data", Description = "Every message, in and out." },
        ["messages"] = new() { Key = "messages", Label = "All Messages" },
        ["outgoing"] = new() { Key = "outgoing", Label = "Outgoing Only" },
        ["incoming"] = new() { Key = "incoming", Label = "Incoming Only" },
        ["campaigns"] = new() { Key = "campaigns", Label = "All Campaigns", Description = "Messages a campaign sent." },
        ["campaignDelivered"] = new() { Key = "campaignDelivered", Label = "Delivered Only" },
        ["campaignFailed"] = new() { Key = "campaignFailed", Label = "Failures Only" },
        ["templates"] = new() { Key = "templates", Label = "All Templates", Description = "Messages sent from a template." },
        ["templateFailures"] = new() { Key = "templateFailures", Label = "Template Failures" },
        ["conversations"] = new() { Key = "conversations", Label = "All Conversations", Description = "Ordinary chat, excluding campaign sends." },
        ["responded"] = new() { Key = "responded", Label = "Responded" },
        ["unanswered"] = new() { Key = "unanswered", Label = "Unanswered" },
        ["failures"] = new() { Key = "failures", Label = "Failures Only" },
        ["undelivered"] = new() { Key = "undelivered", Label = "Not Delivered" }
    };

    // ── Report types ─────────────────────────────────────────────────────────

    private static readonly ReportTypeDto[] AllReportTypes =
    {
        Type("custom", "Custom Report",
            "Start from everything and narrow it yourself.",
            new[] { "all", "messages", "campaigns", "templates", "conversations", "failures" },
            AllGroupBys.Select(g => g.Key).ToArray(),
            AllColumns.Select(c => c.Key).ToArray(),
            DefaultColumnKeys),

        Type("messages", "Messages Report",
            "Every send and reply, message by message.",
            new[] { "messages", "outgoing", "incoming" },
            new[] { NoGrouping, "day", "week", "month", "direction", "status", "messageType", "connection", "contact", "agent" },
            new[]
            {
                "timestamp", "contactPhone", "contactName", "connectionName", "templateOrContent",
                "content", "messageType", "mediaType", "direction", "status", "sentAt",
                "deliveredAt", "readAt", "agent", "responded", "responseMinutes"
            },
            new[] { "timestamp", "contactPhone", "connectionName", "templateOrContent", "messageType", "direction", "status", "deliveredAt", "readAt" }),

        Type("campaign", "Campaign Report",
            "Campaign sends and what became of them.",
            new[] { "campaigns", "campaignDelivered", "campaignFailed" },
            new[] { NoGrouping, "campaign", "template", "status", "day", "week", "month", "connection" },
            new[]
            {
                "timestamp", "campaignName", "templateName", "contactPhone", "contactName",
                "connectionName", "status", "sentAt", "deliveredAt", "readAt", "failureReason",
                "responded", "responseMinutes"
            },
            new[] { "timestamp", "campaignName", "templateName", "contactPhone", "status", "deliveredAt", "readAt", "responded" }),

        Type("template", "Template Report",
            "How each approved template performs in the field.",
            new[] { "templates", "templateFailures" },
            new[] { NoGrouping, "template", "status", "campaign", "day", "week", "month" },
            new[]
            {
                "timestamp", "templateName", "campaignName", "contactPhone", "connectionName",
                "status", "deliveredAt", "readAt", "failureReason", "responded", "responseMinutes"
            },
            new[] { "timestamp", "templateName", "campaignName", "contactPhone", "status", "deliveredAt", "readAt" }),

        Type("delivery", "Delivery & Failure Report",
            "What did not arrive, and why.",
            new[] { "all", "failures", "undelivered" },
            new[] { NoGrouping, "failureReason", "status", "campaign", "template", "connection", "day", "week", "month" },
            new[]
            {
                "timestamp", "contactPhone", "contactName", "campaignName", "templateName",
                "connectionName", "status", "sentAt", "deliveredAt", "readAt", "failureReason", "agent"
            },
            new[] { "timestamp", "contactPhone", "campaignName", "templateName", "connectionName", "status", "failureReason" }),

        Type("conversation", "Conversation Report",
            "Two-way chat, and how quickly it was answered.",
            new[] { "conversations", "responded", "unanswered" },
            new[] { NoGrouping, "contact", "agent", "day", "week", "month", "connection", "status" },
            new[]
            {
                "timestamp", "contactPhone", "contactName", "connectionName", "content",
                "messageType", "direction", "status", "agent", "responded", "responseMinutes"
            },
            new[] { "timestamp", "contactPhone", "contactName", "connectionName", "content", "direction", "status", "agent", "responded", "responseMinutes" })
    };

    public static IReadOnlyList<ReportTypeDto> ReportTypes => AllReportTypes;

    /// <summary>The whole document the builder is drawn from.</summary>
    public static ReportMetadataDto BuildMetadata() => new()
    {
        ReportTypes = AllReportTypes.ToList(),
        Columns = AllColumns.ToList(),
        GroupBys = AllGroupBys.ToList(),
        GroupColumns = GroupColumns.ToList()
    };

    // ── Resolution ───────────────────────────────────────────────────────────

    /// <summary>
    /// The report type for a key, falling back to the custom report.
    ///
    /// Falling back rather than throwing is deliberate: an unknown key means a saved report from
    /// an older catalogue, and opening it against everything is a recoverable answer where a 400
    /// is a dead end.
    /// </summary>
    public static ReportTypeDto ResolveType(string? key)
    {
        var match = AllReportTypes.FirstOrDefault(t =>
            string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

        return match ?? AllReportTypes.First(t => t.Key == DefaultReportTypeKey);
    }

    /// <summary>
    /// The section key to actually scope by: the requested one if this type offers it, otherwise
    /// the type's own default. This is the check that stops "Failures Only" from a delivery report
    /// silently applying to a conversation report that never offered it.
    /// </summary>
    public static string ResolveSectionKey(ReportTypeDto type, string? requested)
    {
        var match = type.DataSections.FirstOrDefault(s =>
            string.Equals(s.Key, requested, StringComparison.OrdinalIgnoreCase));

        return match?.Key ?? type.DataSections.First().Key;
    }

    /// <summary>The grouping key if this type offers it, otherwise no grouping.</summary>
    public static string ResolveGroupByKey(ReportTypeDto type, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return NoGrouping;

        var match = type.GroupByKeys.FirstOrDefault(k =>
            string.Equals(k, requested, StringComparison.OrdinalIgnoreCase));

        return match ?? NoGrouping;
    }

    /// <summary>
    /// Columns to render, resolved against what the type offers and never empty.
    ///
    /// Unknown or disallowed keys are dropped rather than rejected — a saved report written when a
    /// column existed should open with the rest of its columns intact.
    /// </summary>
    public static IReadOnlyList<ReportColumnDto> ResolveColumns(ReportTypeDto type, IReadOnlyList<string>? requested)
    {
        var allowed = new HashSet<string>(type.ColumnKeys, StringComparer.OrdinalIgnoreCase);

        if (requested is { Count: > 0 })
        {
            var selected = requested
                .Where(allowed.Contains)
                .Select(key => AllColumns.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)))
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();

            if (selected.Count > 0) return selected;
        }

        return type.DefaultColumnKeys
            .Select(key => AllColumns.First(c => c.Key == key))
            .ToList();
    }

    public static string LabelForType(string? key) => ResolveType(key).Label;

    public static string LabelForSection(string? typeKey, string? sectionKey)
    {
        var type = ResolveType(typeKey);
        var resolved = ResolveSectionKey(type, sectionKey);
        return Sections.TryGetValue(resolved, out var section) ? section.Label : resolved;
    }

    public static string LabelForGroupBy(string? typeKey, string? groupByKey)
    {
        var resolved = ResolveGroupByKey(ResolveType(typeKey), groupByKey);
        return AllGroupBys.First(g => g.Key == resolved).Label;
    }

    private static ReportTypeDto Type(
        string key,
        string label,
        string description,
        IEnumerable<string> sectionKeys,
        IEnumerable<string> groupByKeys,
        IEnumerable<string> columnKeys,
        IEnumerable<string> defaultColumnKeys) => new()
    {
        Key = key,
        Label = label,
        Description = description,
        DataSections = sectionKeys.Select(k => Sections[k]).ToList(),
        GroupByKeys = groupByKeys.ToList(),
        ColumnKeys = columnKeys.ToList(),
        DefaultColumnKeys = defaultColumnKeys.ToList()
    };
}
