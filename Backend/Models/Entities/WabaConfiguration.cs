using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class WabaConfiguration
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FacebookAppId { get; set; } = string.Empty;

    [Required]
    public string FacebookAppSecret { get; set; } = string.Empty;

    [Required]
    public string AccessToken { get; set; } = string.Empty;

    [Required]
    public string WabaId { get; set; } = string.Empty;

    public string WebhookUrl { get; set; } = string.Empty;

    public string VerifyToken { get; set; } = string.Empty;

    public bool Connected { get; set; }

    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
