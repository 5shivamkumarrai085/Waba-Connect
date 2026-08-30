using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Reporting;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Runs the report builder's queries: the paged row set, the filter option lists, and the saved
/// definitions behind both.
/// </summary>
public interface IReportQueryService
{
    /// <summary>The columns the builder can offer, described by the server.</summary>
    IReadOnlyList<ReportColumnDto> GetColumns();

    /// <summary>
    /// The whole builder vocabulary — report types, their data sections and groupings, and the
    /// column catalogue. Served as one document so the page opens with one request.
    /// </summary>
    ReportMetadataDto GetMetadata();

    /// <summary>One page of report rows for the given filters.</summary>
    Task<PagedResponse<ReportRowDto>> QueryAsync(ReportQueryRequest request);

    /// <summary>
    /// One page of aggregated rows for the request's grouping. Empty when the request is not
    /// grouped — the row query answers that case.
    /// </summary>
    Task<PagedResponse<ReportGroupRowDto>> QueryGroupedAsync(ReportQueryRequest request);

    /// <summary>Every group for these filters, for an export. Capped like the row query.</summary>
    Task<List<ReportGroupRowDto>> QueryAllGroupedAsync(ReportQueryRequest request);

    /// <summary>
    /// Every row matching the filters, for an export. Capped — an unbounded export is a way to
    /// take the server down with one click.
    /// </summary>
    Task<List<ReportRowDto>> QueryAllAsync(ReportQueryRequest request);

    /// <summary>Filter options built from values actually present in the data.</summary>
    Task<ReportFilterOptionsDto> GetFilterOptionsAsync();

    /// <summary>Saved reports the current user may see: their own, plus anything shared.</summary>
    Task<List<SavedReportDto>> GetSavedReportsAsync();

    Task<SavedReportDto> CreateSavedReportAsync(SaveReportRequest request);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when the caller is not the owner.</summary>
    Task<SavedReportDto> UpdateSavedReportAsync(int id, SaveReportRequest request);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when the caller is not the owner.</summary>
    Task DeleteSavedReportAsync(int id);

    /// <summary>Stamps LastRunAt. Silent when the report is gone — running it still worked.</summary>
    Task TouchSavedReportAsync(int id);
}
