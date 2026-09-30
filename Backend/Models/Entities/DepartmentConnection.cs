using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class DepartmentConnection
{
    [Key]
    public int Id { get; set; }

    /// <summary>The role ("department") whose members get this connection.</summary>
    public int RoleId { get; set; }
    public virtual Role Role { get; set; } = null!;

    /// <summary>The role id as text, kept for API compatibility (same value as <see cref="RoleId"/>).</summary>
    [Required]
    public string DepartmentId { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MemberCount { get; set; } = 0;

    public int ConnectionId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Connection Connection { get; set; } = null!;
}
