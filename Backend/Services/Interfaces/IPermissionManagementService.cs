using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IPermissionManagementService
{
    Task<UserPermissionDashboardResponse> GetUserPermissionsDashboardAsync();
    Task<List<UserPermissionResponse>> GetUserPermissionsAsync(string? department = null, int? connectionId = null, bool? activeOnly = null);
    Task<UserPermissionResponse> AssignUserPermissionAsync(AssignUserPermissionRequest request);
    Task<bool> ToggleUserPermissionStatusAsync(int id);
    Task<bool> DeleteUserPermissionAsync(int id);

    Task<DepartmentPermissionDashboardResponse> GetDepartmentPermissionsDashboardAsync();
    Task<List<DepartmentPermissionResponse>> GetDepartmentPermissionsAsync(int? connectionId = null, bool? activeOnly = null);
    Task<DepartmentPermissionResponse> AssignDepartmentPermissionAsync(AssignDepartmentPermissionRequest request);
    Task<bool> ToggleDepartmentPermissionStatusAsync(int id);
    Task<bool> DeleteDepartmentPermissionAsync(int id);

    /// <summary>The real users and roles an assignment can be made to.</summary>
    Task<PermissionCandidatesResponse> GetCandidatesAsync();
}
