namespace WhatsAppCampaignApi.Models.DTOs.Reporting;

/// <summary>
/// Everything the report builder needs to draw itself: the report types on offer, the data
/// sections and groupings each one supports, and the full column catalogue.
///
/// <para>
/// Served as one document rather than four endpoints because the four are useless apart — the
/// builder cannot render a report type without knowing which sections it allows — and because a
/// page that opens with one request feels like a page rather than a loading sequence.
/// </para>
/// <para>
/// Server-owned on purpose. A report type that the client knew about but the query service did
/// not would silently produce the wrong rows; here the same catalogue that describes a type is
/// the one that scopes its query.
/// </para>
/// </summary>
public class ReportMetadataDto
{
    public List<ReportTypeDto> ReportTypes { get; set; } = new();

    /// <summary>The full column catalogue. Each report type names the subset it offers.</summary>
    public List<ReportColumnDto> Columns { get; set; } = new();

    /// <summary>The full grouping catalogue, likewise subsetted per report type.</summary>
    public List<ReportOptionDto> GroupBys { get; set; } = new();

    /// <summary>Columns of the aggregated table, so the client renders no hardcoded header row.</summary>
    public List<ReportColumnDto> GroupColumns { get; set; } = new();
}

/// <summary>One report type — the shape of the question being asked of the data.</summary>
public class ReportTypeDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>Shown under the selector, so the difference between two types is readable.</summary>
    public string? Description { get; set; }

    /// <summary>Scopes this type offers. Never empty — the first is the default.</summary>
    public List<ReportOptionDto> DataSections { get; set; } = new();

    /// <summary>Keys from <see cref="ReportMetadataDto.GroupBys"/> that make sense here.</summary>
    public List<string> GroupByKeys { get; set; } = new();

    /// <summary>Keys from <see cref="ReportMetadataDto.Columns"/> this type can show.</summary>
    public List<string> ColumnKeys { get; set; } = new();

    /// <summary>The columns selected when this type is chosen, in display order.</summary>
    public List<string> DefaultColumnKeys { get; set; } = new();
}

/// <summary>A keyed option with a display label — data sections and groupings both use it.</summary>
public class ReportOptionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// One aggregated row, produced when the builder is grouping rather than listing.
///
/// <para>
/// Deliberately a fixed set of measures rather than a bag of user-chosen aggregates: these are
/// the numbers a messaging report is actually read for, every one of them is a single SQL
/// aggregate, and a grouped query that stayed cheap regardless of range is worth more here than
/// an open-ended pivot builder that does not.
/// </para>
/// </summary>
public class ReportGroupRowDto
{
    /// <summary>Stable identifier for the group — the campaign id, the date, the status name.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>What the group is called in the table.</summary>
    public string Label { get; set; } = string.Empty;

    public int Total { get; set; }
    public int Outgoing { get; set; }
    public int Incoming { get; set; }
    public int Delivered { get; set; }
    public int Read { get; set; }
    public int Failed { get; set; }

    /// <summary>Outgoing messages that were followed by a reply in the same conversation.</summary>
    public int Responded { get; set; }

    /// <summary>Responded as a percentage of outgoing. Null when the group sent nothing.</summary>
    public double? ResponseRate { get; set; }

    public DateTime? FirstAt { get; set; }
    public DateTime? LastAt { get; set; }
}
