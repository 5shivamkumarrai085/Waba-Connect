namespace WhatsAppCampaignApi.Models.DTOs.Groups;

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateGroupRequest : CreateGroupRequest { }

public class GroupResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GroupMembersRequest
{
    public List<int> ContactIds { get; set; } = [];
}
