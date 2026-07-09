using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs
{
    public class VerifyWebhookRequest
    {
        [Required]
        public string VerifyToken { get; set; } = string.Empty;
    }
}
