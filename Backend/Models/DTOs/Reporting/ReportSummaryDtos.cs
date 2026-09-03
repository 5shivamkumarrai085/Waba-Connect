namespace WhatsAppCampaignApi.Models.DTOs.Reporting;

/// <summary>
/// One headline number on the reporting page, with the same figure from the preceding period so
/// the card can show a direction of travel.
/// </summary>
public class ReportKpiDto
{
    /// <summary>Stable identifier the client keys its icon and colour off — never displayed.</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public int Value { get; set; }

    /// <summary>
    /// The same measure over the window immediately before this one, or null when the request had
    /// no date range to shift. Null means "no comparison available", which the card renders as an
    /// absent trend rather than as zero change.
    /// </summary>
    public int? PreviousValue { get; set; }

    /// <summary>
    /// Percentage change against <see cref="PreviousValue"/>, or null when there is nothing to
    /// compare with. Computed server-side so every caller reports the same number.
    /// </summary>
    public double? ChangePercent { get; set; }

    /// <summary>Human-readable description of the comparison window, e.g. "vs Aug 26 - Sep 01".</summary>
    public string? ComparisonLabel { get; set; }
}

/// <summary>One point on the message-activity line.</summary>
public class ReportActivityPointDto
{
    /// <summary>Day this point covers, at midnight UTC.</summary>
    public DateTime Date { get; set; }

    public int Count { get; set; }
}

/// <summary>One slice of the message-type breakdown.</summary>
public class ReportTypeSliceDto
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percent { get; set; }
}

/// <summary>One row of the top-campaigns list.</summary>
public class ReportTopCampaignDto
{
    public int CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Everything the reporting page's summary cards and charts display, for one set of filters.
///
/// <para>
/// Deliberately one response rather than four endpoints. Every part of it is derived from the
/// <em>same</em> filtered query the results table uses, so the headline numbers, the charts and the
/// rows can never disagree — which they would the moment they were fetched separately and one of
/// them was built from a slightly different predicate.
/// </para>
/// <para>
/// Nothing here is precomputed or stored. It is counted from the messages the current filters
/// select, every time.
/// </para>
/// </summary>
public class ReportSummaryDto
{
    public List<ReportKpiDto> Kpis { get; set; } = [];

    /// <summary>Messages per day across the selected window, oldest first.</summary>
    public List<ReportActivityPointDto> Activity { get; set; } = [];

    /// <summary>Share of each message type, largest first.</summary>
    public List<ReportTypeSliceDto> ByType { get; set; } = [];

    /// <summary>The campaigns that sent the most of these messages, largest first.</summary>
    public List<ReportTopCampaignDto> TopCampaigns { get; set; } = [];

    /// <summary>Total messages the filters select — the denominator for every percentage above.</summary>
    public int Total { get; set; }
}
