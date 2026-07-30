using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs
{
    public class SetMessageLimitRequest
    {
        public int? ConnectionId { get; set; }
        public string? PhoneNumberId { get; set; }

        [Required]
        [Range(1, 1000000, ErrorMessage = "Message limit must be between 1 and 1,000,000.")]
        public int MessageLimit { get; set; }
    }
}
