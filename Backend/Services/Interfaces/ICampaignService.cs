using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Service for managing WhatsApp campaigns.
/// </summary>
public interface ICampaignService
{
    Task<PagedResponse<CampaignResponse>> GetAllAsync(PagedRequest request, string? status = null);
    Task<CampaignDetailResponse> GetByIdAsync(int id);
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request);
    Task DeleteAsync(int id);

    /// <summary>
    /// Cancels a scheduled campaign.
    /// </summary>
    Task<CampaignResponse> CancelAsync(int id);

    /// <summary>
    /// Gets per-recipient delivery status for a campaign.
    /// </summary>
    Task<PagedResponse<CampaignRecipientResponse>> GetRecipientsAsync(int campaignId, PagedRequest request);

    /// <summary>
    /// Processes a batch of campaigns that are due for sending (called by scheduler).
    /// </summary>
    Task ProcessScheduledCampaignsAsync(CancellationToken cancellationToken);
}
