using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs;

public class CreateConnectionRequest
{
    public string Name { get; set; } = string.Empty;

    // Validation-only requirement (no DB migration/backfill): every new
    // connection created through the API must supply a nickname; existing
    // connections that predate this change keep whatever they already have.
    [Required]
    public string? Nickname { get; set; }
    public string? Description { get; set; }
}

public class UpdateConnectionRequest
{
    public string Name { get; set; } = string.Empty;

    [Required]
    public string? Nickname { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}

public class ConnectionResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? DisplayName { get; set; }
    public string? VerifiedName { get; set; }
    public string? WabaId { get; set; }
    public bool IsConnected { get; set; }

    /// <summary>
    /// Whether the stored credentials are still complete enough to talk to Meta.
    ///
    /// Disconnecting clears them, and reconnecting cannot invent them back — so this is what
    /// separates "flip it back on" from "run the connect wizard again", and the reconnect endpoint
    /// refuses rather than pretending when it is false.
    /// </summary>
    public bool HasCredentials { get; set; }

    /// <summary>
    /// Whether a sender number is attached. A connection without one is authenticated but cannot
    /// send or receive anything, which is why Chat calls it "Setup pending".
    /// </summary>
    public bool HasPhoneNumber { get; set; }

    /// <summary>
    /// The single status the whole app agrees on: Connected, Setup pending, or Disconnected.
    ///
    /// <para>
    /// Derived on the server rather than left to each screen. It was left to each screen, and the
    /// screens disagreed: the connections list read <see cref="IsConnected"/> and said "Connected"
    /// while Chat looked for a sender number and said "Setup pending" — about the same connection,
    /// at the same moment. Both were reporting a real fact; neither was reporting the whole one.
    /// </para>
    /// </summary>
    public string Status { get; set; } = "Disconnected";

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
