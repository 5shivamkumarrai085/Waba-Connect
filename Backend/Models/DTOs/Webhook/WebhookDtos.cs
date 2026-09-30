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
    public List<IncomingContact>? Contacts { get; set; }
    public List<IncomingMessage>? Messages { get; set; }
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

    [JsonPropertyName("error_data")]
    public ErrorDataObj? ErrorData { get; set; }
}

public class ErrorDataObj
{
    public string Details { get; set; } = string.Empty;
}

public class IncomingContact
{
    [JsonPropertyName("wa_id")]
    public string WaId { get; set; } = string.Empty;

    public IncomingProfile? Profile { get; set; }
}

public class IncomingProfile
{
    public string Name { get; set; } = string.Empty;
}

public class IncomingMessage
{
    public string Id { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public IncomingText? Text { get; set; }
    public IncomingInteractive? Interactive { get; set; }
    public IncomingButton? Button { get; set; }

    /// <summary>Present when the customer started from a Click-to-WhatsApp ad or post.</summary>
    public IncomingReferral? Referral { get; set; }
}

public class IncomingReferral
{
    [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }
    [JsonPropertyName("source_id")] public string? SourceId { get; set; }
    [JsonPropertyName("source_type")] public string? SourceType { get; set; }
    public string? Headline { get; set; }
    public string? Body { get; set; }
    [JsonPropertyName("ctwa_clid")] public string? CtwaClid { get; set; }
}

public class IncomingInteractive
{
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("button_reply")]
    public InteractiveReply? ButtonReply { get; set; }
    
    [JsonPropertyName("list_reply")]
    public InteractiveReply? ListReply { get; set; }
}

public class InteractiveReply
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class IncomingButton
{
    public string Payload { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class IncomingText
{
    public string Body { get; set; } = string.Empty;
}
