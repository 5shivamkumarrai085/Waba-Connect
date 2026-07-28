using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class ConnectionService : IConnectionService
{
    private readonly AppDbContext _dbContext;
    private readonly IPermissionService _permissionService;

    public ConnectionService(AppDbContext dbContext, IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    public async Task<List<ConnectionResponse>> GetAllAsync(string? userId = null, string? departmentId = null)
    {
        var accessibleIds = await _permissionService.GetAccessibleConnectionIdsAsync(userId, departmentId);

        var connections = await _dbContext.Connections
            .AsNoTracking()
            .Where(c => c.IsActive && accessibleIds.Contains(c.Id))
            .OrderBy(c => c.Id)
            .ToListAsync();

        var configs = await _dbContext.WabaConfigurations
            .AsNoTracking()
            .ToListAsync();

        var phones = await _dbContext.WabaPhoneNumbers
            .AsNoTracking()
            .ToListAsync();

        return connections.Select(c => MapToResponse(c, configs, phones)).ToList();
    }

    public async Task<ConnectionResponse?> GetByIdAsync(int id)
    {
        var connection = await _dbContext.Connections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (connection == null) return null;

        var configs = await _dbContext.WabaConfigurations
            .AsNoTracking()
            .Where(w => w.ConnectionId == id)
            .ToListAsync();

        var phones = await _dbContext.WabaPhoneNumbers
            .AsNoTracking()
            .Where(p => p.ConnectionId == id)
            .ToListAsync();

        return MapToResponse(connection, configs, phones);
    }

    public async Task<ConnectionResponse> CreateAsync(CreateConnectionRequest request)
    {
        string trimmedName = request.Name.Trim();

        // Deduplication & Reactivation logic
        var existing = await _dbContext.Connections
            .FirstOrDefaultAsync(c => c.Name.ToLower() == trimmedName.ToLower());

        if (existing != null)
        {
            existing.IsActive = true;
            if (!string.IsNullOrWhiteSpace(request.Description))
                existing.Description = request.Description.Trim();
            existing.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return (await GetByIdAsync(existing.Id))!;
        }

        var connection = new Connection
        {
            Name = trimmedName,
            Description = request.Description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Connections.Add(connection);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(connection, new List<WabaConfiguration>(), new List<WabaPhoneNumber>());
    }

    public async Task<ConnectionResponse> UpdateAsync(int id, UpdateConnectionRequest request)
    {
        var connection = await _dbContext.Connections.FindAsync(id)
            ?? throw new KeyNotFoundException($"Connection with ID {id} not found.");

        if (!string.IsNullOrWhiteSpace(request.Name))
            connection.Name = request.Name.Trim();

        if (request.Description != null)
            connection.Description = request.Description.Trim();

        if (request.IsActive.HasValue)
            connection.IsActive = request.IsActive.Value;

        connection.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return (await GetByIdAsync(id))!;
    }

    public async Task<bool> SoftDisconnectAsync(int id)
    {
        var config = await _dbContext.WabaConfigurations
            .FirstOrDefaultAsync(w => w.ConnectionId == id);

        if (config != null)
        {
            config.Connected = false;
            config.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        return true;
    }

    public async Task<bool> ReconnectAsync(int id)
    {
        var config = await _dbContext.WabaConfigurations
            .FirstOrDefaultAsync(w => w.ConnectionId == id);

        if (config != null)
        {
            config.Connected = true;
            config.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        return true;
    }

    public async Task<bool> SoftDeleteAsync(int id)
    {
        var connection = await _dbContext.Connections.FindAsync(id);
        if (connection != null)
        {
            connection.IsActive = false;
            connection.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return true;
        }
        return false;
    }

    public async Task<ConnectionDashboardResponse> GetDashboardAsync(string? userId = null, string? departmentId = null)
    {
        var connections = await GetAllAsync(userId, departmentId);

        return new ConnectionDashboardResponse
        {
            TotalConnections = connections.Count,
            ConnectedCount = connections.Count(c => c.IsConnected),
            DisconnectedCount = connections.Count(c => !c.IsConnected),
            TotalConnectedNumbers = connections.Count(c => !string.IsNullOrEmpty(c.PhoneNumber)),
            Connections = connections
        };
    }

    public async Task<ConnectionResponse> EnsureDefaultConnectionAsync()
    {
        var defaultConn = await _dbContext.Connections.FirstOrDefaultAsync();
        if (defaultConn == null)
        {
            defaultConn = new Connection
            {
                Name = "Default Connection",
                Description = "Primary WhatsApp Business Connection",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Connections.Add(defaultConn);
            await _dbContext.SaveChangesAsync();
        }

        return (await GetByIdAsync(defaultConn.Id))!;
    }

    private static ConnectionResponse MapToResponse(
        Connection connection,
        List<WabaConfiguration> configs,
        List<WabaPhoneNumber> phones)
    {
        var config = configs.FirstOrDefault(w => w.ConnectionId == connection.Id);
        var phone = phones.FirstOrDefault(p => p.ConnectionId == connection.Id);

        return new ConnectionResponse
        {
            Id = connection.Id,
            Name = connection.Name,
            Description = connection.Description,
            IsActive = connection.IsActive,
            PhoneNumber = phone?.PhoneNumber,
            PhoneNumberId = phone?.PhoneNumberId,
            DisplayName = phone?.DisplayName,
            VerifiedName = phone?.VerifiedName,
            WabaId = config?.WabaId,
            IsConnected = config?.Connected ?? false,
            ConnectedOn = config?.CreatedAt,
            CreatedAt = connection.CreatedAt
        };
    }
}
