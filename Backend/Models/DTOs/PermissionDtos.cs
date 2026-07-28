using System;
using System.Collections.Generic;

namespace WhatsAppCampaignApi.Models.DTOs;

public class UserPermissionResponse
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public List<int> ConnectionIds { get; set; } = new();
    public List<string> ConnectionNames { get; set; } = new();
    public string PermissionScopeText { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DepartmentPermissionResponse
{
    public int Id { get; set; }
    public string DepartmentId { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public List<int> ConnectionIds { get; set; } = new();
    public List<string> ConnectionNames { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AssignUserPermissionRequest
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public List<int> ConnectionIds { get; set; } = new();
}

public class AssignDepartmentPermissionRequest
{
    public string DepartmentId { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MemberCount { get; set; } = 1;
    public List<int> ConnectionIds { get; set; } = new();
}

public class UserPermissionDashboardResponse
{
    public int TotalUsers { get; set; }
    public int UsersWithAccess { get; set; }
    public int TotalConnections { get; set; }
    public int ActivePermissions { get; set; }
    public List<UserPermissionResponse> UserPermissions { get; set; } = new();
}

public class DepartmentPermissionDashboardResponse
{
    public int TotalDepartments { get; set; }
    public int DepartmentsWithAccess { get; set; }
    public int TotalConnections { get; set; }
    public int ActivePermissions { get; set; }
    public List<DepartmentPermissionResponse> DepartmentPermissions { get; set; } = new();
}
