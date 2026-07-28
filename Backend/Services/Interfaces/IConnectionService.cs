using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IConnectionService
{
    Task<List<ConnectionResponse>> GetAllAsync(string? userId = null, string? departmentId = null);
    Task<ConnectionResponse?> GetByIdAsync(int id);
    Task<ConnectionResponse> CreateAsync(CreateConnectionRequest request);
    Task<ConnectionResponse> UpdateAsync(int id, UpdateConnectionRequest request);
    Task<bool> SoftDisconnectAsync(int id);
    Task<bool> ReconnectAsync(int id);
    Task<bool> SoftDeleteAsync(int id);
    Task<ConnectionDashboardResponse> GetDashboardAsync(string? userId = null, string? departmentId = null);
    Task<ConnectionResponse> EnsureDefaultConnectionAsync();
}
