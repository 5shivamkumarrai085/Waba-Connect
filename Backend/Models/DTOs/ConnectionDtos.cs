using System;
using System.Collections.Generic;

namespace WhatsAppCampaignApi.Models.DTOs;

public class CreateConnectionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateConnectionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}

public class ConnectionResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? DisplayName { get; set; }
    public string? VerifiedName { get; set; }
    public string? WabaId { get; set; }
    public bool IsConnected { get; set; }
    public DateTime? ConnectedOn { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ConnectionDashboardResponse
{
    public int TotalConnections { get; set; }
    public int ConnectedCount { get; set; }
    public int DisconnectedCount { get; set; }
    public int TotalConnectedNumbers { get; set; }
    public List<ConnectionResponse> Connections { get; set; } = new();
}
