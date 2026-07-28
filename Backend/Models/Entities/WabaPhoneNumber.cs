using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppCampaignApi.Models.Entities;

[Table("PhoneNumbers")]
public class WabaPhoneNumber
{
    [Key]
    public int Id { get; set; }

    [Required]
    [Column("PhoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    public string PhoneNumberId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string VerifiedName { get; set; } = string.Empty;

    public string Quality { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string MessageLimit { get; set; } = string.Empty;

    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }
}
