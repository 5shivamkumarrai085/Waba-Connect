namespace WhatsAppCampaignApi.Models.DTOs.Contacts;

public class CreateContactRequest
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public string? Website { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
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
    public string? Email { get; set; }
    public string? Company { get; set; }
    public string? Website { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
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

    /// <summary>Badge colour chosen in Setup → Groups. Null falls back to the name-hash palette.</summary>
    public string? Color { get; set; }
}
