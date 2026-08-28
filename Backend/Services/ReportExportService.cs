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

        var columns = ResolveColumns(columnKeys);
        var rows = await _reportQueryService.QueryAllAsync(filters);

        if (normalisedFormat == "pdf" && rows.Count > MaxPdfRows)
        {
            throw new InvalidOperationException(
                $"That range produces {rows.Count:N0} rows, which is too many for a PDF " +
                $"(limit {MaxPdfRows:N0}). Narrow the date range, or export to Excel instead.");
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        return normalisedFormat switch
        {
            "csv" => new ReportExportResult(BuildCsv(columns, rows), "text/csv", $"report_{stamp}.csv"),
            "xlsx" => new ReportExportResult(
                BuildXlsx(columns, rows, filters),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"report_{stamp}.xlsx"),
            _ => new ReportExportResult(BuildPdf(columns, rows, filters), "application/pdf", $"report_{stamp}.pdf")
        };
    }

    /// <summary>
    /// Resolves the requested keys against the catalogue, dropping anything unrecognised and
    /// falling back to the default set when nothing valid remains — an export with no columns is
    /// a blank file, which reads as a broken feature rather than as an empty request.
    /// </summary>
    private IReadOnlyList<ReportColumnDto> ResolveColumns(IReadOnlyList<string>? columnKeys)
    {
        var catalogue = _reportQueryService.GetColumns();

        if (columnKeys is { Count: > 0 })
        {
            var selected = columnKeys
                .Select(key => catalogue.FirstOrDefault(c =>
                    string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)))
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();

            if (selected.Count > 0) return selected;
        }

        return catalogue.Where(c => c.DefaultVisible).ToList();
    }

    private static byte[] BuildCsv(IReadOnlyList<ReportColumnDto> columns, IReadOnlyList<ReportRowDto> rows)
    {
        var csv = new CsvWriter();
        csv.WriteHeader(columns.Select(c => c.Label).ToArray());

        foreach (var row in rows)
        {
            csv.WriteRow(columns.Select(c => ValueFor(row, c.Key)).ToArray());
        }

        return csv.ToBytes();
    }

    private static byte[] BuildXlsx(
        IReadOnlyList<ReportColumnDto> columns,
        IReadOnlyList<ReportRowDto> rows,
        ReportQueryRequest filters)
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
                var value = ValueFor(rows[r], columns[c].Key);

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
        foreach (var (label, value) in DescribeFilters(filters))
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

    private static byte[] BuildPdf(
        IReadOnlyList<ReportColumnDto> columns,
        IReadOnlyList<ReportRowDto> rows,
        ReportQueryRequest filters)
    {
        // QuestPDF's Community licence is set once at startup (Program.cs) — not here, since this
        // runs on every export and the setting is a static process-wide flag.
        var filterSummary = DescribeFilters(filters).ToList();

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
                    header.Item().Text("Report").FontSize(16).Bold();
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
                                .Text(FormatForDocument(ValueFor(rows[i], column.Key))).FontSize(7);
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
        "contactPhone" => row.ContactPhone,
        "campaignName" => row.CampaignName,
        "templateName" => row.TemplateName,
        "connectionName" => row.ConnectionName,
        "direction" => row.Direction,
        "messageType" => row.MessageType,
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
    private static IEnumerable<(string Label, string Value)> DescribeFilters(ReportQueryRequest filters)
    {
        if (filters.From.HasValue) yield return ("From", filters.From.Value.ToString("yyyy-MM-dd"));
        if (filters.To.HasValue) yield return ("To", filters.To.Value.ToString("yyyy-MM-dd"));
        if (filters.CampaignIds is { Count: > 0 }) yield return ("Campaigns", $"{filters.CampaignIds.Count} selected");
        if (filters.ConnectionIds is { Count: > 0 }) yield return ("Connections", $"{filters.ConnectionIds.Count} selected");
        if (filters.TemplateNames is { Count: > 0 }) yield return ("Templates", string.Join(", ", filters.TemplateNames));
        if (filters.Directions is { Count: > 0 }) yield return ("Direction", string.Join(", ", filters.Directions));
        if (filters.Statuses is { Count: > 0 }) yield return ("Status", string.Join(", ", filters.Statuses));
        if (filters.MessageTypes is { Count: > 0 }) yield return ("Message type", string.Join(", ", filters.MessageTypes));
        if (filters.FailedOnly == true) yield return ("Scope", "Failures only");
        if (!string.IsNullOrWhiteSpace(filters.Search)) yield return ("Search", filters.Search);
    }
}
