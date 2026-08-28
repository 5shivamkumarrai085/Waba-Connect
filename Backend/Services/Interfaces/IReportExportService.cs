using WhatsAppCampaignApi.Models.DTOs.Reporting;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>A rendered export, ready to hand back as a file response.</summary>
public record ReportExportResult(byte[] Content, string ContentType, string FileName);

/// <summary>Renders a report's rows as CSV, Excel or PDF.</summary>
public interface IReportExportService
{
    /// <param name="format">csv | xlsx | pdf. Anything else is rejected.</param>
    /// <param name="columnKeys">
    /// Column keys in the author's order. Null uses the default visible set, so an export always
    /// produces something sensible even if the client sends nothing.
    /// </param>
    Task<ReportExportResult> ExportAsync(
        ReportQueryRequest filters,
        IReadOnlyList<string>? columnKeys,
        string format);
}
