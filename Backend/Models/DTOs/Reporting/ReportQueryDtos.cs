namespace WhatsAppCampaignApi.Models.DTOs.Reporting;

/// <summary>
/// What the report builder asks for. Every field is optional — an empty request is "everything",
/// which is the sensible landing state for a page whose job is to let someone narrow down.
/// </summary>
public class ReportQueryRequest
{
    /// <summary>Inclusive lower bound on the message timestamp.</summary>
    public DateTime? From { get; set; }

    /// <summary>
    /// Inclusive upper bound. Interpreted as the end of that day when it arrives as a bare date,
    /// so "to 13 Aug" includes the 13th rather than stopping at midnight on its first second.
    /// </summary>
    public DateTime? To { get; set; }

    public List<int>? CampaignIds { get; set; }
    public List<int>? ConnectionIds { get; set; }
    public List<int>? ContactIds { get; set; }

    /// <summary>Free-text media types actually present on the rows — "image", "document", …</summary>
    public List<string>? MessageTypes { get; set; }

    /// <summary>"Incoming" / "Outgoing".</summary>
    public List<string>? Directions { get; set; }

    /// <summary>Delivery statuses — Sent, Delivered, Read, Failed, Pending.</summary>
    public List<string>? Statuses { get; set; }

    /// <summary>Template names, resolved through the campaign that sent the message.</summary>
    public List<string>? TemplateNames { get; set; }

    /// <summary>Substring match over the recipient's name, phone or the message text.</summary>
    public string? Search { get; set; }

    /// <summary>True to return only rows that carry a failure reason.</summary>
    public bool? FailedOnly { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>
/// One row of the report.
///
/// Flat on purpose: this is what the table renders, what the CSV writes and what the spreadsheet
/// export lays out, and keeping one shape for all three is what stops the three drifting apart.
/// </summary>
public class ReportRowDto
{
    /// <summary>Message id, or a synthetic negative id for a recipient that produced no message.</summary>
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? CampaignName { get; set; }

    /// <summary>
    /// Resolved through the campaign, since a chat message stores no template of its own. Null for
    /// an ordinary conversation message, which genuinely has no template.
    /// </summary>
    public string? TemplateName { get; set; }

    public string? ConnectionName { get; set; }
    public string? Direction { get; set; }

    /// <summary>
    /// Derived from MediaType, which is free text rather than an enum — "text" when there is no
    /// attachment. Labelled as derived in the UI for that reason.
    /// </summary>
    public string? MessageType { get; set; }

    public string? Status { get; set; }

    /// <summary>The rendered message body, truncated for the table; exports carry the full text.</summary>
    public string? Content { get; set; }

    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>
    /// Whether the recipient replied. Derived: the first incoming message in the same conversation
    /// after this one. Null for an incoming row, where the question does not apply.
    /// </summary>
    public bool? Responded { get; set; }

    /// <summary>Minutes between this outgoing message and that reply. Null when there was none.</summary>
    public double? ResponseMinutes { get; set; }
}

/// <summary>
/// Values actually present in the data, for the filter controls.
///
/// Built from distinct values rather than a hardcoded list, the same way the audit filters are, so
/// a new campaign or a newly used media type appears without a code change — and a filter never
/// offers an option that would return nothing.
/// </summary>
public class ReportFilterOptionsDto
{
    public List<ReportOption> Campaigns { get; set; } = new();
    public List<ReportOption> Connections { get; set; } = new();
    public List<string> Templates { get; set; } = new();
    public List<string> MessageTypes { get; set; } = new();
    public List<string> Directions { get; set; } = new();
    public List<string> Statuses { get; set; } = new();

    /// <summary>Earliest and latest message timestamps, so the date picker can bound itself.</summary>
    public DateTime? EarliestRecord { get; set; }
    public DateTime? LatestRecord { get; set; }
}

public class ReportOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>A column the builder can include, described by the server so the two cannot disagree.</summary>
public class ReportColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>Included when the user has expressed no preference.</summary>
    public bool DefaultVisible { get; set; }

    /// <summary>
    /// Set when the value is inferred rather than stored — Message Type and the response columns.
    /// Surfaced in the UI so nobody reads a derived figure as a recorded one.
    /// </summary>
    public string? DerivedNote { get; set; }
}

/// <summary>A saved report definition, as the list and the editor see it.</summary>
public class SavedReportDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Column keys, in the order the author arranged them.</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>The filter set, replayed when the report is run.</summary>
    public ReportQueryRequest Filters { get; set; } = new();

    public bool IsShared { get; set; }

    /// <summary>True when the signed-in user owns it — only an owner may edit or delete.</summary>
    public bool IsOwner { get; set; }

    public string? OwnerName { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Create/update payload for a saved report.</summary>
public class SaveReportRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Columns { get; set; } = new();
    public ReportQueryRequest Filters { get; set; } = new();
    public bool IsShared { get; set; }
}
