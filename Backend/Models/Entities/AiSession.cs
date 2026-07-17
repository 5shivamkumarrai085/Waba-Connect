using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class AiSession
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string PhoneNumber { get; set; } = string.Empty;

    public int MessageBotId { get; set; }
    public MessageBot MessageBot { get; set; } = null!;

    [Required, MaxLength(50)]
    public string AssistantName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
