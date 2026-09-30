using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An outbound webhook: events of the chosen types are POSTed to <see cref="Url"/>, signed with
/// <see cref="Secret"/> (HMAC-SHA256) so the receiver can prove they came from us.
/// </summary>
public class WebhookSubscription
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    /// <summary>The signing secret. Encrypted at rest; shown once, on create and on rotation.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Comma-separated event types; "*" for all, "email.*" for a family.</summary>
    [MaxLength(2000)]
    public string EventTypes { get; set; } = "*";

    /// <summary>Comma-separated connection ids the events must belong to. Null means every connection.</summary>
    [MaxLength(1000)]
    public string? ConnectionIds { get; set; }

    /// <summary>Whether payloads carry recipient addresses and phone numbers. Off by default.</summary>
    public bool IncludePersonalData { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Deliveries that exhausted their retries in a row. Reset by a success.</summary>
    public int FailureStreak { get; set; }

    /// <summary>Why the subscription was switched off automatically, if it was.</summary>
    [MaxLength(300)]
    public string? DisabledReason { get; set; }

    public DateTime? LastDeliveryAt { get; set; }

    [MaxLength(20)]
    public string? LastStatus { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One event sent (or being sent) to one subscription: the log behind replay.</summary>
public class WebhookDelivery
{
    public long Id { get; set; }

    public int SubscriptionId { get; set; }
    public WebhookSubscription? Subscription { get; set; }

    /// <summary>Stable across retries and replays: the receiver's idempotency key (X-Waba-Event-Id).</summary>
    [MaxLength(40)]
    public string EventId { get; set; } = string.Empty;

    [MaxLength(60)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>The exact body that is signed and sent.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Pending, Delivered, Failed or Skipped.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public int Attempts { get; set; }

    /// <summary>Incremented by each replay, so the replay gets its own queue job.</summary>
    public int ReplayCount { get; set; }

    public int? ResponseCode { get; set; }
    public int? DurationMs { get; set; }

    [MaxLength(500)]
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

/// <summary>A saved report emailed on a schedule.</summary>
public class ReportSchedule
{
    public int Id { get; set; }

    public int ReportDefinitionId { get; set; }
    public ReportDefinition? ReportDefinition { get; set; }

    /// <summary>Daily, Weekly or Monthly.</summary>
    [MaxLength(10)]
    public string Frequency { get; set; } = "Weekly";

    /// <summary>Local time of day, "HH:mm", in <see cref="TimeZone"/>.</summary>
    [MaxLength(5)]
    public string TimeOfDay { get; set; } = "09:00";

    /// <summary>Weekly: 0 = Sunday … 6 = Saturday.</summary>
    public int? DayOfWeek { get; set; }

    /// <summary>Monthly: 1–28, so every month has the day.</summary>
    public int? DayOfMonth { get; set; }

    [MaxLength(64)]
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Comma-separated addresses, at most 20.</summary>
    [MaxLength(2000)]
    public string Recipients { get; set; } = string.Empty;

    /// <summary>csv, xlsx or pdf.</summary>
    [MaxLength(5)]
    public string Format { get; set; } = "xlsx";

    /// <summary>How many days back each run covers (the report's own dates are replaced).</summary>
    public int LookbackDays { get; set; } = 7;

    /// <summary>The email sender the report goes out from.</summary>
    public int SenderIdentityId { get; set; }

    /// <summary>The report runs with this user's access: their connections only.</summary>
    public int OwnerUserId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }

    [MaxLength(20)]
    public string? LastStatus { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
