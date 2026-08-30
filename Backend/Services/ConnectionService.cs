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
    private readonly ITemplateService _templateService;
    private readonly IAuditService _auditService;
    private readonly IMetaGraphService _metaGraphService;
    private readonly IPhoneRepository _phoneRepository;

    public ConnectionService(
        AppDbContext dbContext,
        IPermissionService permissionService,
        ITemplateService templateService,
        IAuditService auditService,
        IMetaGraphService metaGraphService,
        IPhoneRepository phoneRepository)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
        _templateService = templateService;
        _auditService = auditService;
        _metaGraphService = metaGraphService;
        _phoneRepository = phoneRepository;
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
            if (!string.IsNullOrWhiteSpace(request.Nickname))
                existing.Nickname = request.Nickname.Trim();
            existing.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Distinct event from Created: this path revives a previously deactivated
            // connection rather than adding one, and the trail should say which happened.
            await _auditService.LogAsync(
                "Connection.Reactivated", "Data",
                $"Reactivated existing connection \"{existing.Name}\".",
                "Connection", existing.Id.ToString());

            return (await GetByIdAsync(existing.Id))!;
        }

        var connection = new Connection
        {
            Name = trimmedName,
            Nickname = request.Nickname?.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Connections.Add(connection);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Connection.Created", "Data",
            $"Created connection \"{connection.Name}\".",
            "Connection", connection.Id.ToString());

        return MapToResponse(connection, new List<WabaConfiguration>(), new List<WabaPhoneNumber>());
    }

    public async Task<ConnectionResponse> UpdateAsync(int id, UpdateConnectionRequest request)
    {
        var connection = await _dbContext.Connections.FindAsync(id)
            ?? throw new KeyNotFoundException($"Connection with ID {id} not found.");

        if (!string.IsNullOrWhiteSpace(request.Name))
            connection.Name = request.Name.Trim();

        if (request.Nickname != null)
            connection.Nickname = request.Nickname.Trim();

        if (request.Description != null)
            connection.Description = request.Description.Trim();

        if (request.IsActive.HasValue)
            connection.IsActive = request.IsActive.Value;

        connection.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Connection.Updated", "Data",
            $"Updated connection \"{connection.Name}\".",
            "Connection", connection.Id.ToString());

        return (await GetByIdAsync(id))!;
    }

    public async Task<bool> SoftDisconnectAsync(int id)
    {
        var config = await _dbContext.WabaConfigurations
            .FirstOrDefaultAsync(w => w.ConnectionId == id);

        if (config != null)
        {
            config.Connected = false;
            config.FacebookAppId = string.Empty;
            config.FacebookAppSecret = string.Empty;
            config.AccessToken = string.Empty;
            config.WabaId = string.Empty;
            config.VerifyToken = string.Empty;
            config.WebhookUrl = string.Empty;
            config.UpdatedAt = DateTime.UtcNow;
        }

        // Remove phone numbers associated with this connection
        var phones = await _dbContext.WabaPhoneNumbers
            .Where(p => p.ConnectionId == id)
            .ToListAsync();
        if (phones.Count > 0)
        {
            _dbContext.WabaPhoneNumbers.RemoveRange(phones);
        }

        await _dbContext.SaveChangesAsync();

        // Sync templates to remove templates from this now-disconnected connection
        try
        {
            await _templateService.SyncFromWhatsAppAsync();
        }
        catch
        {
            // Don't fail disconnect if template sync fails
        }

        await _auditService.LogAsync(
            "Connection.Disconnected", "Data",
            $"Disconnected connection \"{await GetConnectionNameAsync(id)}\"; credentials cleared and {phones.Count} phone number(s) removed.",
            "Connection", id.ToString());

        return true;
    }

    /// <summary>
    /// Brings a disconnected connection back into service.
    ///
    /// <para>
    /// This used to set <c>Connected = true</c> and return. That is not a reconnection — disconnect
    /// clears the credentials and deletes the sender numbers, so flipping the flag produced a
    /// connection the list called "Connected" and Chat called "Setup pending", because it had
    /// authentication for nothing and no number to send from. Neither screen was wrong; the state
    /// was.
    /// </para>
    /// <para>
    /// So there are two honest outcomes. If the credentials survived, re-fetch the sender numbers
    /// from Meta and the connection genuinely works again. If they did not, refuse and say the
    /// connect wizard has to be run again — a reconnect cannot invent an access token back.
    /// </para>
    /// </summary>
    public async Task<bool> ReconnectAsync(int id)
    {
        var config = await _dbContext.WabaConfigurations
            .FirstOrDefaultAsync(w => w.ConnectionId == id);

        var name = await GetConnectionNameAsync(id);

        if (config == null
            || string.IsNullOrWhiteSpace(config.WabaId)
            || string.IsNullOrWhiteSpace(config.AccessToken))
        {
            // Recorded as a failure, not swallowed: someone looking at why a connection stayed
            // broken should find the attempt in the trail.
            await _auditService.LogAsync(
                "Connection.ReconnectFailed", "Data",
                $"Could not reconnect \"{name}\" — its WhatsApp credentials were cleared when it was disconnected.",
                "Connection", id.ToString());

            throw new InvalidOperationException(
                $"\"{name}\" cannot be reconnected from here: disconnecting it cleared its WhatsApp " +
                "credentials. Use Add Connection to link the WhatsApp Business Account again.");
        }

        var restoredNumbers = await SyncPhoneNumbersAsync(config);

        // Flipped only once Meta has actually answered. Setting it first and syncing after would
        // leave a connection marked Connected behind a token Meta had just rejected — which is the
        // exact state this whole change exists to stop producing.
        config.Connected = true;
        config.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Connection.Reconnected", "Data",
            $"Reconnected connection \"{name}\"; {restoredNumbers} sender number(s) restored from Meta.",
            "Connection", id.ToString());

        return true;
    }

    /// <summary>
    /// Re-reads a connection's sender numbers from Meta and stores them against it.
    ///
    /// <para>
    /// Exists because a connection can end up authenticated with no number attached — after a
    /// disconnect/reconnect, or when the phone fetch failed during the original setup — and in that
    /// state nothing can be sent or received even though everything looks configured. Until now the
    /// only way out was to run the whole connect wizard again, which is a heavy answer to a missing
    /// row.
    /// </para>
    /// <para>
    /// Upserted by phone number id rather than cleared and re-inserted: these rows are referenced by
    /// conversations and campaigns, and deleting them to write the same values back would break
    /// those references for the sake of a refresh.
    /// </para>
    /// </summary>
    public async Task<int> SyncPhoneNumbersAsync(int id)
    {
        var config = await _dbContext.WabaConfigurations
            .FirstOrDefaultAsync(w => w.ConnectionId == id);

        var name = await GetConnectionNameAsync(id);

        if (config == null
            || string.IsNullOrWhiteSpace(config.WabaId)
            || string.IsNullOrWhiteSpace(config.AccessToken))
        {
            throw new InvalidOperationException(
                $"\"{name}\" has no stored WhatsApp credentials to sync with. Use Add Connection to link it again.");
        }

        var count = await SyncPhoneNumbersAsync(config);

        await _auditService.LogAsync(
            "Connection.NumbersSynced", "Data",
            $"Synced sender numbers for \"{name}\" from Meta; {count} number(s) attached.",
            "Connection", id.ToString());

        return count;
    }

    /// <summary>
    /// Fetches and stores a connection's numbers, throwing with Meta's own words when Meta refused.
    ///
    /// <para>
    /// The distinction matters more than it looks. An expired access token and a WABA with no
    /// phone number both used to arrive here as an empty list, so both were reported as "no
    /// numbers found" — which sent people looking for a missing phone number when the real answer
    /// was that their token had expired days earlier and every send was going to fail too.
    /// </para>
    /// </summary>
    private async Task<int> SyncPhoneNumbersAsync(WabaConfiguration config)
    {
        var result = await _metaGraphService.GetPhoneNumbersDetailedAsync(config.WabaId, config.AccessToken);

        if (result.Error is not null)
        {
            throw new InvalidOperationException(
                $"WhatsApp rejected this connection: {result.Error} Reconnect the account with a current access token.");
        }

        var fetched = result.Phones.ToList();
        if (fetched.Count == 0) return 0;

        foreach (var phone in fetched)
        {
            phone.ConnectionId = config.ConnectionId;
        }

        await _phoneRepository.SaveRangeAsync(fetched);

        return fetched.Count;
    }

    public async Task<bool> SoftDeleteAsync(int id)
    {
        var connection = await _dbContext.Connections.FindAsync(id);
        if (connection != null)
        {
            connection.IsActive = false;
            connection.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync(
                "Connection.Deleted", "Data",
                $"Deleted connection \"{connection.Name}\".",
                "Connection", connection.Id.ToString());

            return true;
        }
        return false;
    }

    /// <summary>
    /// Name for the audit description. Disconnect and reconnect work off WabaConfiguration, so
    /// the Connection row isn't already loaded, and an audit entry reading "#7" would be useless
    /// to whoever reads the trail later.
    /// </summary>
    private async Task<string> GetConnectionNameAsync(int id) =>
        await _dbContext.Connections
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => c.Name)
            .FirstOrDefaultAsync() ?? $"#{id}";

    public async Task<ConnectionDashboardResponse> GetDashboardAsync(string? userId = null, string? departmentId = null)
    {
        var connections = await GetAllAsync(userId, departmentId);

        return new ConnectionDashboardResponse
        {
            TotalConnections = connections.Count,
            ConnectedCount = connections.Count(c => c.IsConnected),
            DisconnectedCount = connections.Count(c => !c.IsConnected),
            TotalConnectedNumbers = connections.Count(c => c.IsConnected && !string.IsNullOrEmpty(c.PhoneNumber)),
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

        var isConnected = config?.Connected ?? false;
        var hasCredentials = config != null
            && !string.IsNullOrWhiteSpace(config.WabaId)
            && !string.IsNullOrWhiteSpace(config.AccessToken);
        var hasPhone = phone != null && !string.IsNullOrWhiteSpace(phone.PhoneNumber);

        return new ConnectionResponse
        {
            // Connected means usable: credentials that work and a number to send from. A
            // connection missing either is reported as "Setup pending" rather than as connected,
            // which is what Chat has always shown and what the list used to contradict.
            Status = !isConnected
                ? "Disconnected"
                : hasCredentials && hasPhone
                    ? "Connected"
                    : "Setup pending",
            HasCredentials = hasCredentials,
            HasPhoneNumber = hasPhone,
            IsConnected = isConnected,
            Id = connection.Id,
            Name = connection.Name,
            Nickname = connection.Nickname,
            Description = connection.Description,
            IsActive = connection.IsActive,
            PhoneNumber = phone?.PhoneNumber,
            PhoneNumberId = phone?.PhoneNumberId,
            DisplayName = phone?.DisplayName,
            VerifiedName = phone?.VerifiedName,
            WabaId = config?.WabaId,
            ConnectedOn = config?.CreatedAt,
            CreatedAt = connection.CreatedAt
        };
    }
}
