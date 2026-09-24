using System.Text.Json.Serialization;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// The SES event published inside an SNS notification's Message field.
///
/// <para>
/// Only the fields this product acts on are modelled. SES sends considerably more, and binding
/// all of it would mean a model that has to be revised whenever AWS adds a field — the raw
/// payload is stored on the event row anyway, so nothing is lost by ignoring the rest.
/// </para>
/// </summary>
public class SesEventNotification
{
    /// <summary>
    /// "Send", "Delivery", "Bounce", "Complaint", "Reject", "Open", "Click", "DeliveryDelay",
    /// "Rendering Failure" or "Subscription".
    /// </summary>
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    /// <summary>
    /// The older configuration-set notifications use this instead of <see cref="EventType"/>.
    /// Both are read, because which one arrives depends on how the configuration set was set up
    /// and getting it wrong silently drops every event.
    /// </summary>
    [JsonPropertyName("notificationType")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("mail")]
    public SesMail? Mail { get; set; }

    [JsonPropertyName("bounce")]
    public SesBounce? Bounce { get; set; }

    [JsonPropertyName("complaint")]
    public SesComplaint? Complaint { get; set; }

    [JsonPropertyName("delivery")]
    public SesDelivery? Delivery { get; set; }

    [JsonPropertyName("open")]
    public SesOpen? Open { get; set; }

    [JsonPropertyName("click")]
    public SesClick? Click { get; set; }

    [JsonPropertyName("reject")]
    public SesReject? Reject { get; set; }

    [JsonPropertyName("deliveryDelay")]
    public SesDeliveryDelay? DeliveryDelay { get; set; }

    /// <summary>The event name, from whichever field SES populated.</summary>
    public string? ResolvedEventType => EventType ?? NotificationType;
}

public class SesMail
{
    /// <summary>The SES MessageId — the key every event correlates on.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("destination")]
    public List<string>? Destination { get; set; }

    /// <summary>
    /// The tags set when sending, echoed back. Carries our campaign id, which lets an event be
    /// attributed even when the recipient row cannot be found by message id.
    /// </summary>
    [JsonPropertyName("tags")]
    public Dictionary<string, List<string>>? Tags { get; set; }

    /// <summary>Headers, when the configuration set is set to include them.</summary>
    [JsonPropertyName("headers")]
    public List<SesHeader>? Headers { get; set; }
}

public class SesHeader
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public class SesBounce
{
    /// <summary>"Permanent", "Transient" or "Undetermined" — the field that decides suppression.</summary>
    [JsonPropertyName("bounceType")]
    public string? BounceType { get; set; }

    [JsonPropertyName("bounceSubType")]
    public string? BounceSubType { get; set; }

    [JsonPropertyName("bouncedRecipients")]
    public List<SesBouncedRecipient>? BouncedRecipients { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}

public class SesBouncedRecipient
{
    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; set; }

    /// <summary>The remote server's verbatim rejection — usually the only thing that explains it.</summary>
    [JsonPropertyName("diagnosticCode")]
    public string? DiagnosticCode { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class SesComplaint
{
    [JsonPropertyName("complainedRecipients")]
    public List<SesComplainedRecipient>? ComplainedRecipients { get; set; }

    [JsonPropertyName("complaintFeedbackType")]
    public string? ComplaintFeedbackType { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}

public class SesComplainedRecipient
{
    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; set; }
}

public class SesDelivery
{
    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("recipients")]
    public List<string>? Recipients { get; set; }

    [JsonPropertyName("smtpResponse")]
    public string? SmtpResponse { get; set; }
}

public class SesOpen
{
    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("ipAddress")]
    public string? IpAddress { get; set; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }
}

public class SesClick : SesOpen
{
    [JsonPropertyName("link")]
    public string? Link { get; set; }
}

public class SesReject
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class SesDeliveryDelay
{
    [JsonPropertyName("delayType")]
    public string? DelayType { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}
