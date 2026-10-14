using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs.BotFlow;

public class CreateBotFlowRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;

    public string? FlowData { get; set; } // Serialized canvas JSON

    // Null = fires on all connections. Set = scoped to one connection only.
    public int? ConnectionId { get; set; }
}

/// <summary>
/// A partial update: <see cref="IsActive"/> and <see cref="ConnectionId"/> change only when the
/// request includes them. Saving from the designer used to omit both, which silently made every
/// saved flow global (unbound from its connection) and renaming a flow switched it on.
/// </summary>
public class UpdateBotFlowRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Null keeps the current value.</summary>
    public bool? IsActive { get; set; }

    public string? FlowData { get; set; }

    private int? _connectionId;

    /// <summary>
    /// The connection to scope the flow to. Sending <c>null</c> explicitly makes it global; leaving
    /// the field out keeps the current binding.
    /// </summary>
    public int? ConnectionId
    {
        get => _connectionId;
        set { _connectionId = value; ConnectionIdSpecified = true; }
    }

    /// <summary>Whether the request carried <c>connectionId</c> at all (the JSON reader only calls the setter when it is present).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ConnectionIdSpecified { get; private set; }
}

public class BotFlowResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string FlowData { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Null = fires on all connections. Set = scoped to one connection only.
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
}
