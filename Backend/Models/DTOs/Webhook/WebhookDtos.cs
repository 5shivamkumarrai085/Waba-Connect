using System.Text.Json.Serialization;

namespace WhatsAppCampaignApi.Models.DTOs.Webhook;

public class WhatsAppWebhookPayload
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;
    
    public List<Entry>? Entry { get; set; }
}

public class Entry
{
    public string Id { get; set; } = string.Empty;
    public List<Change>? Changes { get; set; }
}

public class Change
{
    public ChangeValue? Value { get; set; }
    public string Field { get; set; } = string.Empty;
}

public class ChangeValue
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = string.Empty;
    
    public MetadataObj? Metadata { get; set; }
    public List<StatusUpdate>? Statuses { get; set; }
}

public class MetadataObj
{
    [JsonPropertyName("display_phone_number")]
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    
    [JsonPropertyName("phone_number_id")]
    public string PhoneNumberId { get; set; } = string.Empty;
}

public class StatusUpdate
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    
    [JsonPropertyName("recipient_id")]
    public string RecipientId { get; set; } = string.Empty;
    
    public List<ErrorObj>? Errors { get; set; }
}

public class ErrorObj
{
    public int Code { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
