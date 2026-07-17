using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class ClientAiSetting
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string ClientId { get; set; } = "default";

    [Required, MaxLength(50)]
    public string Provider { get; set; } = "Groq";

    [Required, MaxLength(100)]
    public string Model { get; set; } = "llama3-8b-8192";

    [Required]
    public string EncryptedApiKey { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
