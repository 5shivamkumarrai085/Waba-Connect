using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs
{
    public class ConfigureWabaRequest
    {
        [Required]
        public string WabaId { get; set; } = string.Empty;

        [Required]
        public string AccessToken { get; set; } = string.Empty;

        public int? ConnectionId { get; set; }
    }
}
