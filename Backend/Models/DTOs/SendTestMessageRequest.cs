using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs
{
    public class SendTestMessageRequest
    {
        public int? ConnectionId { get; set; }

        [Required]
        public string RecipientNumber { get; set; } = string.Empty;

        public string TemplateName { get; set; } = "hello_world";

        public string LanguageCode { get; set; } = "en_US";
    }
}
