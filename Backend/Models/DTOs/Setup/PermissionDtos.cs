using System.Collections.Generic;

namespace WhatsAppCampaignApi.Models.DTOs.Setup;

/// <summary>
/// The permission catalogue as the matrix UI consumes it: features grouped into sections, each
/// carrying only the capabilities that actually exist for it. The frontend renders exactly what
/// this returns, so a new feature is a seed change with no client edit.
/// </summary>
public class PermissionCatalogResponse
{
    public List<PermissionGroupResponse> Groups { get; set; } = new();
}

public class PermissionGroupResponse
{
    public string Name { get; set; } = string.Empty;
    public List<PermissionFeatureResponse> Features { get; set; } = new();
}

public class PermissionFeatureResponse
{
    public string Feature { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<PermissionCapabilityResponse> Capabilities { get; set; } = new();
}

public class PermissionCapabilityResponse
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
