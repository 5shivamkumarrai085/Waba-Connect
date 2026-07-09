namespace WhatsAppCampaignApi.Models.DTOs.Contacts;

public class CreateContactRequest
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public List<int>? GroupIds { get; set; }
}

public class UpdateContactRequest : CreateContactRequest { }

public class ContactResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ContactGroupBriefResponse> Groups { get; set; } = [];
}

public class ContactBriefResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class ContactGroupBriefResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
