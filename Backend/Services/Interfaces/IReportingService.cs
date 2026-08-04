using WhatsAppCampaignApi.Models.DTOs.Reporting;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IReportingService
{
    Task<List<MetricCardDto>> GetMetricsAsync(string timeFilter);
    Task<List<AccuracyRecordDto>> GetAccuracyAsync();
    Task<List<FreshnessRecordDto>> GetFreshnessAsync();
    Task<List<ExportItemDto>> GetExportsAsync();
    List<string> GetCustomisationFeatures();

    Task<byte[]> BuildMetricsCsvAsync(string timeFilter);
    Task<byte[]> BuildContactsCsvAsync();
    Task<byte[]> BuildChatsCsvAsync();
}
