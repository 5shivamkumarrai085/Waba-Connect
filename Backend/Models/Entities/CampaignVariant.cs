using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One version of an A/B-tested campaign. Variant "A" is the campaign's own template; the others
/// swap the template (WhatsApp or email) and, for email, may override the subject.
/// </summary>
public class CampaignVariant
{
    public int Id { get; set; }

    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    [Required, MaxLength(8)]
    public string Label { get; set; } = "A";

    public int SortOrder { get; set; }

    /// <summary>WhatsApp: the Meta template this variant sends.</summary>
    public int? TemplateId { get; set; }
    public Template? Template { get; set; }

    /// <summary>Email: the template this variant sends.</summary>
    public int? EmailTemplateId { get; set; }
    public EmailTemplate? EmailTemplate { get; set; }

    [MaxLength(998)]
    public string? SubjectOverride { get; set; }
}
