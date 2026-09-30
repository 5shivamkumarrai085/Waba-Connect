using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Reporting;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class ReportExportService : IReportExportService
{
    private readonly IReportQueryService _reportQueryService;

    /// <summary>
    /// Beyond this the PDF is refused and the caller is told to narrow the range or use a
    /// spreadsheet. A thousand-page PDF is not a document anyone opens, and laying one out holds
    /// the whole row set plus its rendered pages in memory at once — the sort of request that
    /// takes a server down while looking like an ordinary click.
    /// </summary>
    private const int MaxPdfRows = 5000;

    public ReportExportService(IReportQueryService reportQueryService)
    {
        _reportQueryService = reportQueryService;
    }

    public async Task<ReportExportResult> ExportAsync(
        ReportQueryRequest filters,
        IReadOnlyList<string>? columnKeys,
        string format)
    {
        var normalisedFormat = (format ?? "csv").Trim().ToLowerInvariant();
        if (normalisedFormat is not ("csv" or "xlsx" or "pdf"))
        {
            throw new ArgumentException($"Unsupported export format \"{format}\". Use csv, xlsx or pdf.");
        }

        var type = ReportCatalog.ResolveType(filters.ReportType);
        var isGrouped = ReportCatalog.ResolveGroupByKey(type, filters.GroupBy) != ReportCatalog.NoGrouping;

        // The file has to be the report that is on screen. A grouped report exported as its
        // underlying messages would be a different document under the same name — and it is the
        // one someone forwards as a record of what they were looking at.
        return isGrouped
            ? await ExportGroupedAsync(filters, type, normalisedFormat)
            : await ExportRowsAsync(filters, type, columnKeys, normalisedFormat);
    }

    private async Task<ReportExportResult> ExportRowsAsync(
        ReportQueryRequest filters,
        ReportTypeDto type,
        IReadOnlyList<string>? columnKeys,
        string format)
    {
        var columns = ReportCatalog.ResolveColumns(type, columnKeys);
        var rows = await _reportQueryService.QueryAllAsync(filters);

        GuardPdfSize(format, rows.Count);

        return Render(format, columns, rows, ValueFor, filters, type);
    }

    private async Task<ReportExportResult> ExportGroupedAsync(
        ReportQueryRequest filters,
        ReportTypeDto type,
        string format)
    {
        var rows = await _reportQueryService.QueryAllGroupedAsync(filters);

        GuardPdfSize(format, rows.Count);

        return Render(format, ReportCatalog.GroupColumns, rows, GroupValueFor, filters, type);
    }

    private static void GuardPdfSize(string format, int rowCount)
    {
        if (format != "pdf" || rowCount <= MaxPdfRows) return;

        throw new InvalidOperationException(
            $"That range produces {rowCount:N0} rows, which is too many for a PDF " +
            $"(limit {MaxPdfRows:N0}). Narrow the date range, or export to Excel instead.");
    }

    /// <summary>
    /// Renders whichever row shape it is handed.
    ///
    /// Generic over the row rather than duplicated per shape: the three writers care about the
    /// columns and a way to read a value, not about what a row is, and a second copy of the
    /// spreadsheet formatting rules would immediately be the copy that is out of date.
    /// </summary>
    private static ReportExportResult Render<TRow>(
        string format,
        IReadOnlyList<ReportColumnDto> columns,
        IReadOnlyList<TRow> rows,
        Func<TRow, string, object?> valueFor,
        ReportQueryRequest filters,
        ReportTypeDto type)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var slug = type.Key;

        return format switch
        {
            "csv" => new ReportExportResult(
                BuildCsv(columns, rows, valueFor), "text/csv", $"{slug}_report_{stamp}.csv"),
            "xlsx" => new ReportExportResult(
                BuildXlsx(columns, rows, valueFor, filters, type),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{slug}_report_{stamp}.xlsx"),
            _ => new ReportExportResult(
                BuildPdf(columns, rows, valueFor, filters, type), "application/pdf", $"{slug}_report_{stamp}.pdf")
        };
    }

    private static byte[] BuildCsv<TRow>(
        IReadOnlyList<ReportColumnDto> columns,
        IReadOnlyList<TRow> rows,
        Func<TRow, string, object?> valueFor)
    {
        var csv = new CsvWriter();
        csv.WriteHeader(columns.Select(c => c.Label).ToArray());

        foreach (var row in rows)
        {
            csv.WriteRow(columns.Select(c => valueFor(row, c.Key)).ToArray());
        }

        return csv.ToBytes();
    }

    private static byte[] BuildXlsx<TRow>(
        IReadOnlyList<ReportColumnDto> columns,
        IReadOnlyList<TRow> rows,
        Func<TRow, string, object?> valueFor,
        ReportQueryRequest filters,
        ReportTypeDto type)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");

        for (var i = 0; i < columns.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = columns[i].Label;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                var value = valueFor(rows[r], columns[c].Key);

                // Typed, not stringified. This is the reason for using a real spreadsheet library:
                // a date written as text cannot be sorted chronologically and a number written as
                // text cannot be summed, which is most of what anyone opens the file to do.
                switch (value)
                {
                    case null:
                        break;
                    case DateTime dt:
                        cell.Value = dt;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                        break;
                    case double d:
                        cell.Value = d;
                        break;
                    // Grouped reports are counts. Written as numbers so the column sums, which is
                    // the first thing anyone does to a column of counts.
                    case int i:
                        cell.Value = i;
                        break;
                    case bool b:
                        cell.Value = b ? "Yes" : "No";
                        break;
                    default:
                        // SetText, not Value: a contact named "=cmd|…" or a phone number written
                        // "+91…" would otherwise be parsed as a formula or a number.
                        cell.SetValue(value.ToString());
                        break;
                }
            }
        }

        // Freeze the header and turn on autofilter — the first two things anyone does by hand to
        // a sheet of this shape.
        sheet.SheetView.FreezeRows(1);
        if (rows.Count > 0)
        {
            sheet.Range(1, 1, rows.Count + 1, columns.Count).SetAutoFilter();
        }

        sheet.Columns().AdjustToContents(1, 200, 10d, 60d);

        // A second sheet recording what was asked for, so a file forwarded on its own still says
        // which slice of the data it is.
        var meta = workbook.Worksheets.Add("Filters");
        meta.Cell(1, 1).Value = "Generated (UTC)";
        meta.Cell(1, 2).Value = DateTime.UtcNow;
        meta.Cell(1, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        meta.Cell(2, 1).Value = "Rows";
        meta.Cell(2, 2).Value = rows.Count;

        var metaRow = 3;
        foreach (var (label, value) in DescribeFilters(filters, type))
        {
            meta.Cell(metaRow, 1).Value = label;
            meta.Cell(metaRow, 2).SetValue(value);
            metaRow++;
        }

        meta.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildPdf<TRow>(
        IReadOnlyList<ReportColumnDto> columns,
        IReadOnlyList<TRow> rows,
        Func<TRow, string, object?> valueFor,
        ReportQueryRequest filters,
        ReportTypeDto type)
    {
        // QuestPDF's Community licence is set once at startup (Program.cs) — not here, since this
        // runs on every export and the setting is a static process-wide flag.
        var filterSummary = DescribeFilters(filters, type).ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                // Landscape: these reports are wide, and portrait would squeeze ten columns into
                // an unreadable strip.
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(8).FontColor("#1f2937"));

                page.Header().Column(header =>
                {
                    header.Item().Text(type.Label).FontSize(16).Bold();
                    header.Item().Text(
                        $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC · {rows.Count:N0} row(s)")
                        .FontSize(8).FontColor("#6b7280");

                    if (filterSummary.Count > 0)
                    {
                        header.Item().PaddingTop(2).Text(
                            string.Join("  ·  ", filterSummary.Select(f => $"{f.Label}: {f.Value}")))
                            .FontSize(8).FontColor("#6b7280");
                    }

                    header.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor("#e5e7eb");
                });

                page.Content().PaddingVertical(6).Table(table =>
                {
                    table.ColumnsDefinition(definition =>
                    {
                        foreach (var _ in columns) definition.RelativeColumn();
                    });

                    table.Header(h =>
                    {
                        foreach (var column in columns)
                        {
                            h.Cell().Background("#eef2ff").Padding(4)
                                .Text(column.Label).Bold().FontSize(8);
                        }
                    });

                    for (var i = 0; i < rows.Count; i++)
                    {
                        // Banding, because a wide landscape table without it is genuinely hard to
                        // read a single row across.
                        var background = i % 2 == 0 ? "#ffffff" : "#f9fafb";

                        foreach (var column in columns)
                        {
                            table.Cell().Background(background).Padding(3)
                                .Text(FormatForDocument(valueFor(rows[i], column.Key))).FontSize(7);
                        }
                    }
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.CurrentPageNumber().FontSize(8);
                    text.Span(" of ").FontSize(8);
                    text.TotalPages().FontSize(8);
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// Pulls one column's value off a row.
    ///
    /// A switch rather than reflection: reflection would silently return null for a mistyped key,
    /// where this makes the column catalogue and the row shape have to agree.
    /// </summary>
    private static object? ValueFor(ReportRowDto row, string key) => key switch
    {
        "timestamp" => row.Timestamp,
        "contactName" => row.ContactName,
        "adSource" => row.AdSource,
        "contactPhone" => row.ContactPhone,
        "campaignName" => row.CampaignName,
        "templateName" => row.TemplateName,
        "connectionName" => row.ConnectionName,
        "direction" => row.Direction,
        "messageType" => row.MessageType,
        "mediaType" => row.MediaType,
        "templateOrContent" => row.TemplateOrContent,
        "agent" => row.Agent,
        "status" => row.Status,
        "content" => row.Content,
        "sentAt" => row.SentAt,
        "deliveredAt" => row.DeliveredAt,
        "readAt" => row.ReadAt,
        "failureReason" => row.FailureReason,
        "responded" => row.Responded,
        "responseMinutes" => row.ResponseMinutes,
        _ => null
    };

    /// <summary>
    /// The same accessor for an aggregated row. Kept beside <see cref="ValueFor"/> and written the
    /// same way, so the grouped export cannot quietly grow its own formatting rules.
    /// </summary>
    private static object? GroupValueFor(ReportGroupRowDto row, string key) => key switch
    {
        "label" => row.Label,
        "total" => row.Total,
        "outgoing" => row.Outgoing,
        "incoming" => row.Incoming,
        "delivered" => row.Delivered,
        "read" => row.Read,
        "failed" => row.Failed,
        "responded" => row.Responded,
        "responseRate" => row.ResponseRate,
        "firstAt" => row.FirstAt,
        "lastAt" => row.LastAt,
        _ => null
    };

    private static string FormatForDocument(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// Renders the filter set as label/value pairs for the export's own header.
    ///
    /// Only what was actually set: listing every unused filter as "All" is noise, and the reader
    /// needs to know what was narrowed, not what was not.
    /// </summary>
    private static IEnumerable<(string Label, string Value)> DescribeFilters(
        ReportQueryRequest filters,
        ReportTypeDto type)
    {
        // The type, section and grouping come first: they decide what the rows below even are, so
        // a file read on its own says which question it answers before it says how it was narrowed.
        yield return ("Report type", type.Label);
        yield return ("Data section", ReportCatalog.LabelForSection(type.Key, filters.DataSection));

        var groupBy = ReportCatalog.ResolveGroupByKey(type, filters.GroupBy);
        if (groupBy != ReportCatalog.NoGrouping)
            yield return ("Grouped by", ReportCatalog.LabelForGroupBy(type.Key, groupBy));

        if (filters.From.HasValue) yield return ("From", filters.From.Value.ToString("yyyy-MM-dd"));
        if (filters.To.HasValue) yield return ("To", filters.To.Value.ToString("yyyy-MM-dd"));
        if (filters.CampaignIds is { Count: > 0 }) yield return ("Campaigns", $"{filters.CampaignIds.Count} selected");
        if (filters.ConnectionIds is { Count: > 0 }) yield return ("Connections", $"{filters.ConnectionIds.Count} selected");
        if (filters.TemplateNames is { Count: > 0 }) yield return ("Templates", string.Join(", ", filters.TemplateNames));
        if (filters.Directions is { Count: > 0 }) yield return ("Direction", string.Join(", ", filters.Directions));
        if (filters.Statuses is { Count: > 0 }) yield return ("Status", string.Join(", ", filters.Statuses));
        if (filters.MessageTypes is { Count: > 0 }) yield return ("Message type", string.Join(", ", filters.MessageTypes));
        if (filters.FailureReasons is { Count: > 0 }) yield return ("Failure reason", string.Join(", ", filters.FailureReasons));
        if (filters.Agents is { Count: > 0 }) yield return ("Agent", string.Join(", ", filters.Agents));
        if (filters.ContactIds is { Count: > 0 }) yield return ("Contacts", $"{filters.ContactIds.Count} selected");
        if (filters.FailedOnly == true) yield return ("Scope", "Failures only");
        if (!string.IsNullOrWhiteSpace(filters.Search)) yield return ("Search", filters.Search);
    }
}
