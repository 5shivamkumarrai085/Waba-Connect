using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppCampaignApi.Models.Entities;

[Table("Businesses")]
public class Business
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string BusinessId { get; set; } = string.Empty;

    [Required]
    public string BusinessName { get; set; } = string.Empty;

    public string Timezone { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}
