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
    public int? ConnectionId { get; set; }
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
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsBulkCampaign { get; set; }
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public string? ConnectionNickname { get; set; }
}

public class CampaignDetailResponse : CampaignResponse
{
    public List<CampaignRecipientResponse> Recipients { get; set; } = [];
    public List<CampaignVariableResponse>? Variables { get; set; } = [];
}

/// <summary>
/// One rejected row from a bulk-campaign CSV upload, identifying exactly which row/column
/// failed and why — shared between the csv-validate preview response and the csv-create
/// actual-creation response so both report errors the same way.
/// </summary>
public class CsvRowError
{
    public int RowNumber { get; set; }
    public string? Column { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class CsvCampaignCreateResponse : CampaignResponse
{
    public List<CsvRowError> SkippedRows { get; set; } = new();
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
    public int? ConnectionId { get; set; }
}
