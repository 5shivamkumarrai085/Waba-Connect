using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One outbound template send and what Meta said about it.
///
/// <para>
/// Distinct from <see cref="AuditLog"/>: that records configuration changes ("who edited this
/// role"), this records message traffic ("what did we send, and did Meta accept it").
/// </para>
/// </summary>
public class MessageActivityLog
{
    [Key]
    public int Id { get; set; }

    /// <summary>Campaign | TemplateBot | InitiateChat.</summary>
    [Required, MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Campaign or bot name. Denormalized so the log survives the source being deleted.</summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(200)]
    public string? TemplateName { get; set; }

    /// <summary>HTTP status from Meta — 200 on success, 4xx/5xx on rejection.</summary>
    public int? ResponseCode { get; set; }

    [MaxLength(50)]
    public string? RelationType { get; set; }

    /// <summary>No foreign key: the trail must outlive a deleted contact.</summary>
    public int? ContactId { get; set; }

    [MaxLength(20)]
    public string? ContactPhone { get; set; }

    public int? ConnectionId { get; set; }

    [MaxLength(200)]
    public string? WhatsAppMessageId { get; set; }

    public bool IsSuccess { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Null for scheduler and webhook traffic, which runs with no signed-in user.</summary>
    public int? PerformedByUserId { get; set; }

    /// <summary>
    /// Where the action came from. Null for the scheduler, which has no request behind it —
    /// that absence is itself informative, so it is not defaulted to a placeholder.
    /// </summary>
    [MaxLength(64)]
    public string? IpAddress { get; set; }

    /// <summary>Scheduler | Webhook | User.</summary>
    [MaxLength(50)]
    public string? TriggeredBy { get; set; }

    /// <summary>
    /// The JSON body sent to Meta, redacted and capped. Deliberately unbounded in the schema
    /// (nvarchar(max)) with the cap applied in code — a MaxLength here would truncate mid-token
    /// and produce unparseable JSON in the viewer.
    /// </summary>
    public string? RequestPayload { get; set; }

    /// <summary>Meta's raw response body, same redaction and cap as <see cref="RequestPayload"/>.</summary>
    public string? ResponsePayload { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
