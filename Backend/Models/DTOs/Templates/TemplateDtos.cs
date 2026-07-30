namespace WhatsAppCampaignApi.Models.DTOs.Templates;

public class CreateTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string Category { get; set; } = string.Empty;
    public string TemplateType { get; set; } = "Text";
    public string BodyText { get; set; } = string.Empty;
    public string HeaderType { get; set; } = "None";
    public string? HeaderContent { get; set; }
    public string? FooterText { get; set; }
    public List<TemplateVariableRequest>? Variables { get; set; }
}

public class UpdateTemplateRequest : CreateTemplateRequest { }

public class TemplateVariableRequest
{
    public int Position { get; set; }
    public string? SampleValue { get; set; }
    public string? Description { get; set; }
}

public class TemplateResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string HeaderType { get; set; } = string.Empty;
    public string? HeaderContent { get; set; }
    public string? FooterText { get; set; }
    public string? WhatsAppTemplateId { get; set; }
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<TemplateVariableRequest> Variables { get; set; } = [];
}

public class TemplateBriefResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class TemplatePreviewResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PreviewText { get; set; } = string.Empty;
}
