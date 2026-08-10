using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs.Setup;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IRoleService
{
    Task<List<RoleListItemResponse>> GetAllAsync();
    Task<RoleDetailResponse> GetByIdAsync(int id);
    Task<RoleDetailResponse> CreateAsync(CreateRoleRequest request);
    Task<RoleDetailResponse> UpdateAsync(int id, UpdateRoleRequest request);

    /// <summary>
    /// Deletes a role. Refuses on system roles, and on any role still assigned to users —
    /// silently orphaning people into a no-permissions state is worse than an error.
    /// </summary>
    Task DeleteAsync(int id);
}
