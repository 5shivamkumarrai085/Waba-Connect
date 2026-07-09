namespace WhatsAppCampaignApi.Models.Entities;

public class ContactGroupMember
{
    public int Id { get; set; }
    
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    
    public int GroupId { get; set; }
    public ContactGroup Group { get; set; } = null!;
}
