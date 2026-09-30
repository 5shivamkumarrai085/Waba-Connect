using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A contact's current consent for one channel and topic ("marketing", "offers", …, or "all").
/// </summary>
/// <remarks>
/// One row per (contact, channel, topic): the current answer. Every change is also appended to
/// <see cref="ConsentEvent"/>, which is the audit trail regulators ask for — who, when, how, and the
/// proof (the message, the form, the IP).
/// </remarks>
public class ContactConsent
{
    public int Id { get; set; }

    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    public MessageChannel Channel { get; set; }

    /// <summary>Lower-case topic key. "all" applies to every topic on the channel.</summary>
    [Required, MaxLength(64)]
    public string Topic { get; set; } = ConsentTopics.Marketing;

    public ConsentStatus Status { get; set; }

    /// <summary>How it was captured: import, form, keyword, preference-centre, unsubscribe-link, agent, api.</summary>
    [Required, MaxLength(40)]
    public string Source { get; set; } = "agent";

    /// <summary>Evidence as JSON — the inbound message id and text, the form URL, the request IP.</summary>
    public string? ProofJson { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public int? CapturedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Append-only history of consent changes. Never updated or deleted by the application.</summary>
public class ConsentEvent
{
    public long Id { get; set; }

    public int ContactId { get; set; }
    public MessageChannel Channel { get; set; }

    [Required, MaxLength(64)]
    public string Topic { get; set; } = ConsentTopics.Marketing;

    public ConsentStatus Status { get; set; }

    [Required, MaxLength(40)]
    public string Source { get; set; } = "agent";

    public string? ProofJson { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public int? ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Well-known consent topics.</summary>
public static class ConsentTopics
{
    /// <summary>The default topic of a campaign that names none.</summary>
    public const string Marketing = "marketing";

    /// <summary>A consent on this topic applies to every topic of the channel.</summary>
    public const string All = "all";

    public static string Normalize(string? topic) =>
        string.IsNullOrWhiteSpace(topic) ? Marketing : topic.Trim().ToLowerInvariant();
}
