using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs
{
    public class ConnectAppRequest
    {
        [Required]
        public string FacebookAppId { get; set; } = string.Empty;

        [Required]
        public string FacebookAppSecret { get; set; } = string.Empty;
    }
}
