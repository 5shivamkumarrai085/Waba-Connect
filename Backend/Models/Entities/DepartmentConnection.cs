using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class DepartmentConnection
{
    [Key]
    public int Id { get; set; }

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
