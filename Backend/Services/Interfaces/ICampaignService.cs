using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Service for managing WhatsApp campaigns.
/// </summary>
public interface ICampaignService
{
    Task<PagedResponse<CampaignResponse>> GetAllAsync(PagedRequest request, CampaignListFilter? filter = null);
    Task<CampaignDetailResponse> GetByIdAsync(int id);
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request);
    Task<CsvCampaignCreateResponse> CreateCsvCampaignAsync(CreateCsvCampaignRequest request);
    Task<CampaignResponse> UpdateAsync(int id, CreateCampaignRequest request);
    Task DeleteAsync(int id);
    Task<bool> CheckNameExistsAsync(string name, int? excludeId = null);

    /// <summary>
    /// Cancels a scheduled campaign.
    /// </summary>
    Task<CampaignResponse> CancelAsync(int id);

    /// <summary>A/B tests: choose the winner now (or a given variant) and send it to the held recipients.</summary>
    Task<int?> DecideAbTestAsync(int id, int? variantId);

    /// <summary>Per-link click totals for an email campaign, most clicked first.</summary>
    Task<IReadOnlyList<CampaignLinkClicks>> GetLinkReportAsync(int id, CancellationToken ct = default);

    /// <summary>Re-sends to a finished campaign's failed recipients (never bounced or suppressed ones).</summary>
    Task<CampaignRetryResponse> RetryFailedAsync(int id);

    /// <summary>Maker-checker: campaigns waiting for approval that this caller can see.</summary>
    Task<int> GetPendingApprovalCountAsync();

    /// <summary>Approves a parked campaign and sends it. The approver must not be its maker.</summary>
    Task<CampaignResponse> ApproveAsync(int id, string? comment);

    /// <summary>Rejects a parked campaign; it is cancelled. A reason is required.</summary>
    Task<CampaignResponse> RejectAsync(int id, string? comment);

    /// <summary>
    /// Pauses a campaign that has not executed yet.
    /// </summary>
    Task<CampaignResponse> PauseAsync(int id);

    /// <summary>
    /// Resumes a paused campaign.
    /// </summary>
    Task<CampaignResponse> ResumeAsync(int id);

    /// <summary>
    /// Gets per-recipient delivery status for a campaign.
    /// </summary>
    /// <param name="state">"queue" (still pending), "executed" (everything else), or null for all.</param>
    Task<PagedResponse<CampaignRecipientResponse>> GetRecipientsAsync(int campaignId, PagedRequest request, string? state = null);

    /// <summary>How many recipients are still queued and how many have been processed.</summary>
    Task<(int Queued, int Executed)> GetRecipientCountsAsync(int campaignId);

    /// <summary>Streams every recipient of a campaign as CSV, in batches, without loading them all.</summary>
    Task ExportRecipientsCsvAsync(int campaignId, Stream output, CancellationToken ct);

    /// <summary>
    /// Processes a batch of campaigns that are due for sending (called by scheduler).
    /// </summary>
    Task ProcessScheduledCampaignsAsync(CancellationToken cancellationToken);
}
