namespace WhatsAppCampaignApi.Models.DTOs.Activity;

/// <summary>
/// One row of the Audit Events table, carrying enough for both the list and the details panel.
///
/// The detail fields ride along with the list rather than sitting behind a per-row endpoint:
/// the payload is small, and a request per eye-click would make the panel feel slow for no gain.
/// </summary>
public class AuditLogResponse
{
    /// <summary>Database id. Used as a React key only — never displayed.</summary>
    public int Id { get; set; }

    /// <summary>The number shown to the user, from its own sequence rather than the primary key.</summary>
    public long EventNumber { get; set; }

    public DateTime Time { get; set; }
    public string Event { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Module { get; set; }
    public string? Action { get; set; }
    public string? Status { get; set; }
    public string User { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }

    /// <summary>
    /// Raw JSON from the change capture. Deserialised by the client so the API stays agnostic
    /// about how many entities a single operation touched.
    /// </summary>
    public string? ChangesJson { get; set; }

    /// <summary>
    /// Raw JSON of the event's structured payload — see <see cref="AuditMetadata"/>. Null for
    /// almost every event; carried on the list row for the same reason as ChangesJson, so the
    /// details panel opens without a second request.
    /// </summary>
    public string? MetadataJson { get; set; }
}

/// <summary>
/// One sign-in attempt. Shared by the success and failure tabs — the only difference is that a
/// failure carries a reason, which is null on a success.
/// </summary>
public class LoginAttemptResponse
{
    public string Id { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public string Email { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    /// <summary>Why the attempt failed. Null for a successful sign-in.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Distinct values actually present in the audit table, so the filter dropdowns are built from
/// real data instead of a hardcoded list that drifts as modules are added.
/// </summary>
public class AuditFilterOptionsResponse
{
    public List<string> Modules { get; set; } = new();
    public List<string> Actions { get; set; } = new();
    public List<string> Statuses { get; set; } = new();
    public List<AuditUserOption> Users { get; set; } = new();
}

public class AuditUserOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
