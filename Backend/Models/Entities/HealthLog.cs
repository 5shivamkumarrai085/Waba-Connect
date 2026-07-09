using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppCampaignApi.Models.Entities;

[Table("HealthLogs")]
public class HealthLog
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Status { get; set; } = string.Empty;

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    public string Description { get; set; } = string.Empty;
}
