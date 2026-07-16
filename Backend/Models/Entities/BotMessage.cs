using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class BotMessage
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Direction { get; set; } = string.Empty; // Incoming, Outgoing

    [Required, MaxLength(4000)]
    public string Text { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Sent"; // Sent, Delivered, Read, Failed

    public int? FlowId { get; set; }
    public BotFlow? Flow { get; set; }

    [MaxLength(100)]
    public string? NodeId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
