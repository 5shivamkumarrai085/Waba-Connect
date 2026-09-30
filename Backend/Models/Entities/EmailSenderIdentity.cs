using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One selectable "from" address for a connection — what the campaign wizard's Sender Email
/// dropdown and its "Add New Sender" action operate on.
/// </summary>
public class EmailSenderIdentity
{
    public int Id { get; set; }

    public int EmailConfigurationId { get; set; }
    public EmailConfiguration EmailConfiguration { get; set; } = null!;

    /// <summary>Shown to recipients as the sender name.</summary>
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string EmailAddress { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? ReplyTo { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
