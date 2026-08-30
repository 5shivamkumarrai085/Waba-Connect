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
    /// <summary>
    /// Brings a disconnected connection back into service, re-fetching its sender numbers.
    /// Throws <see cref="InvalidOperationException"/> when disconnecting cleared the credentials —
    /// there is nothing to reconnect with, and saying so beats flipping a flag over an empty account.
    /// </summary>
    Task<bool> ReconnectAsync(int id);

    /// <summary>
    /// Re-reads a connection's sender numbers from Meta. The repair for a connection that is
    /// authenticated but has no number attached, and so can neither send nor receive.
    /// </summary>
    Task<int> SyncPhoneNumbersAsync(int id);
    Task<bool> SoftDeleteAsync(int id);
    Task<ConnectionDashboardResponse> GetDashboardAsync(string? userId = null, string? departmentId = null);
    Task<ConnectionResponse> EnsureDefaultConnectionAsync();
}
