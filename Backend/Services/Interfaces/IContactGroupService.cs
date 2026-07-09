using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Groups;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Service for managing contact groups.
/// </summary>
public interface IContactGroupService
{
    Task<PagedResponse<GroupResponse>> GetAllAsync(PagedRequest request);
    Task<GroupResponse> GetByIdAsync(int id);
    Task<GroupResponse> CreateAsync(CreateGroupRequest request);
    Task<GroupResponse> UpdateAsync(int id, UpdateGroupRequest request);
    Task DeleteAsync(int id);
    Task AddMembersAsync(int groupId, GroupMembersRequest request);
    Task RemoveMembersAsync(int groupId, GroupMembersRequest request);
}
