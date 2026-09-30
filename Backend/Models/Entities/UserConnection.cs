using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class UserConnection
{
    [Key]
    public int Id { get; set; }

    /// <summary>The user this grants access to.</summary>
    public int AppUserId { get; set; }
    public virtual AppUser AppUser { get; set; } = null!;

    /// <summary>The user id as text, kept for API compatibility (same value as <see cref="AppUserId"/>).</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;

    public int ConnectionId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Connection Connection { get; set; } = null!;
}
