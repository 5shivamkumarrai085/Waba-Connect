using System;
using System.Collections.Generic;

namespace WhatsAppCampaignApi.Models.DTOs.Setup;

public class RoleListItemResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsAdministrator { get; set; }
    public int UserCount { get; set; }
    public int PermissionCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RoleDetailResponse : RoleListItemResponse
{
    public List<string> PermissionKeys { get; set; } = new();

    /// <summary>Populates the "List of users using this role" panel on the role form.</summary>
    public List<RoleUserResponse> Users { get; set; } = new();
}

public class RoleUserResponse
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Grants everything and ignores <see cref="PermissionKeys"/>. Kept assignable so an
    /// organisation can define a second administrator tier.
    /// </summary>
    public bool IsAdministrator { get; set; }

    public List<string> PermissionKeys { get; set; } = new();
}

public class UpdateRoleRequest : CreateRoleRequest
{
}
