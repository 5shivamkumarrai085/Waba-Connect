using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.DTOs.TemplateBot;

public class TemplateBotVariableDto
{
    [Required, MaxLength(50)]
    public string VariableName { get; set; } = string.Empty;
    [MaxLength(500)]
    public string? VariableValue { get; set; }
    [MaxLength(50)]
    public string? MergeField { get; set; }
}

public class CreateTemplateBotRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required, MaxLength(50)]
    public string RelationType { get; set; } = "Lead";
    
    public int TemplateId { get; set; }
    
    [Required, MaxLength(100)]
    public string ReplyType { get; set; } = "On Exact Match";
    
    [Required, MaxLength(256)]
    public string TriggerKeyword { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;

    public List<TemplateBotVariableDto> Variables { get; set; } = [];

    // Null = fires on all connections. Set = scoped to one connection only.
    public int? ConnectionId { get; set; }
}

public class UpdateTemplateBotRequest : CreateTemplateBotRequest
{
}

public class TemplateBotResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string ReplyType { get; set; } = string.Empty;
    public string TriggerKeyword { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<TemplateBotVariableDto> Variables { get; set; } = [];

    // Null = fires on all connections. Set = scoped to one connection only.
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
}
