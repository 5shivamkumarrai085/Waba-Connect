using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

public class CampaignContact
{
    public int Id { get; set; }
    
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;
    
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    
    [MaxLength(200)]
    public string? WhatsAppMessageId { get; set; }
    
    public MessageStatus Status { get; set; } = MessageStatus.Pending;
    
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
    
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
