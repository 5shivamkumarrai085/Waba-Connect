using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IUserService
{
    Task<PagedResponse<UserListItemResponse>> GetAllAsync(PagedRequest request, bool? isActive = null, int? roleId = null);
    Task<UserDetailResponse> GetByIdAsync(int id);
    Task<UserDashboardResponse> GetDashboardAsync();
    Task<UserDetailResponse> CreateAsync(CreateUserRequest request);
    Task<UserDetailResponse> UpdateAsync(int id, UpdateUserRequest request);

    /// <summary>Soft-deletes. Refuses to remove the last remaining administrator.</summary>
    Task DeleteAsync(int id);

    Task<UserListItemResponse> ToggleActiveAsync(int id);

    /// <summary>Lightweight list for the Contacts "assigned to" picker.</summary>
    Task<List<AssignableUserResponse>> GetAssignableAsync();
}

public class AssignableUserResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
