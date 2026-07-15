namespace WhatsAppCampaignApi.Models.DTOs.Campaigns;

public class CreateCampaignRequest
{
    public string Name { get; set; } = string.Empty;
    public int TemplateId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "Immediate";
    public DateTime? ScheduledAt { get; set; }
    public List<int>? ContactIds { get; set; }
    public List<int>? GroupIds { get; set; }
    public List<CampaignVariableRequest>? Variables { get; set; }
}

public class CampaignVariableRequest
{
    public string VariableName { get; set; } = string.Empty;
    public string? VariableValue { get; set; }
    public string? MergeField { get; set; }
}

public class CampaignResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalRecipients { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public int FailedCount { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CampaignDetailResponse : CampaignResponse
{
    public List<CampaignRecipientResponse> Recipients { get; set; } = [];
    public List<CampaignVariableResponse>? Variables { get; set; } = [];
}

public class CampaignVariableResponse
{
    public string VariableName { get; set; } = string.Empty;
    public string? VariableValue { get; set; }
    public string? MergeField { get; set; }
}

public class CampaignRecipientResponse
{
    public int Id { get; set; }
    public int ContactId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CreateCsvCampaignRequest
{
    public string Name { get; set; } = string.Empty;
    public string CsvFileUrl { get; set; } = string.Empty;
    public int TemplateId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "Immediate";
    public DateTime? ScheduledAt { get; set; }
    public List<CampaignVariableRequest>? Variables { get; set; }
}
