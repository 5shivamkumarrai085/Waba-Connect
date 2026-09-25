namespace WhatsAppCampaignApi.Models.Enums;

public enum ContactType { Lead, Customer, Vendor }
public enum ContactStatus { New, Active, Inactive, InProgress, Contacted, Qualified, Closed }
public enum ContactSource { WhatsApp, Web, Import, Manual, Facebook, Saas, Email }
public enum TemplateCategory { Marketing, Utility, Authentication }
public enum TemplateType { Text, Image, Video, Document }
public enum HeaderType { None, Text, Image, Video, Document }
public enum TemplateStatus { Approved, Rejected, Pending }
// AwaitingApproval is appended, not inserted: these persist as strings
// (HasConversion<string>() in AppDbContext), so the new name only ever appears on new rows and
// no existing row changes meaning. It is set only by the campaign execution gate, which is a
// no-op pass-through in this deployment — see ICampaignExecutionGate.
public enum CampaignStatus { Draft, Sending, Sent, Scheduled, Paused, Failed, PartiallyFailed, Cancelled, AwaitingApproval }
public enum ScheduleType { Immediate, Scheduled }

// Bounced/Complained/Suppressed are email-only terminal states, appended for the same reason as
// above. WhatsApp never writes them, and the monotonic status guards treat them as terminal.
// Opens and clicks deliberately do NOT appear here — engagement is not a delivery state, and
// folding it in would let a later "Open" event regress a recipient out of Delivered. Those live
// in EmailDeliveryEvent instead.
public enum MessageStatus { Pending, Sent, Delivered, Read, Failed, Bounced, Complained, Suppressed }
public enum ChatMessageDirection { Incoming, Outgoing, System }
public enum ChatMessageStatus { Pending, Sent, Delivered, Read, Failed, Received }

// ── Channel dimension ────────────────────────────────────────────────────────────────────────
// Only the channels this build can actually send on. The UI lists SMS/Instagram/Facebook as
// "Coming Soon", which is presentation only — adding a member here means committing to a
// provider, a worker and a template path, so they stay out until that exists.
public enum MessageChannel { WhatsApp, Email }

// ── Email channel ────────────────────────────────────────────────────────────────────────────
public enum EmailProviderType { AmazonSes, Smtp }

/// <summary>How a provider authenticates. IamRole stores no credentials at all and is the
/// recommended production option; AccessKey falls back to the encrypted key columns.</summary>
public enum EmailAuthMode { AccessKey, IamRole }

public enum SmtpSecurityMode { None, StartTls, SslOnConnect }

/// <summary>Mirrors the SES identity/DKIM verification states.</summary>
public enum EmailIdentityStatus { NotStarted, Pending, Verified, Failed, TemporaryFailure }

public enum EmailEventType { Send, Delivery, Bounce, Complaint, Reject, Open, Click, DeliveryDelay, RenderingFailure, Subscription }

/// <summary>
/// Normalized, channel-agnostic email event kinds.
///
/// These are the events the application's business logic understands, regardless of which
/// provider or channel produced them. EmailEventType is SES-specific; EmailEventKind is not.
///
/// OPENED means a tracking pixel was loaded — not a guaranteed human reading.
/// DELIVERED means the receiving server accepted the message — not end-user reading.
/// Do not claim DELIVERED for SMTP sends; SMTP only tells us the next hop accepted the message.
/// </summary>
public enum EmailEventKind
{
    /// <summary>SMTP/provider accepted the message for delivery.</summary>
    Sent,
    /// <summary>Terminal: the message could not be sent (authentication, no-such-user, template error, etc.).</summary>
    Failed,
    /// <summary>Remote server confirmed delivery. Only available from providers that support delivery receipts.</summary>
    Delivered,
    /// <summary>Message was permanently rejected by the recipient's server. Triggers suppression.</summary>
    Bounced,
    /// <summary>Tracking pixel was loaded. Engagement, not delivery — does NOT change delivery state.</summary>
    Opened,
    /// <summary>A tracked link was clicked. Engagement, not delivery.</summary>
    Clicked,
    /// <summary>The recipient replied to the message (detected via IMAP).</summary>
    Replied,
    /// <summary>The recipient clicked the unsubscribe link or endpoint. Triggers suppression.</summary>
    Unsubscribed,
    /// <summary>The recipient marked the message as spam. Triggers suppression.</summary>
    Complained
}

public enum EmailBounceType { Undetermined, Permanent, Transient }

public enum SuppressionReason { Bounce, Complaint, Unsubscribe, Manual, ListImport }

public enum SuppressionScope { Global, Connection }

// ── Queue ────────────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Lifecycle of a row in the JobQueue table. Leased rows whose LeaseExpiresAt has passed are
/// reclaimable by the claim query, which is why no separate reaper process is needed.
/// DeadLettered is terminal until an operator explicitly requeues it.
/// </summary>
public enum JobStatus { Pending, Leased, Completed, Failed, DeadLettered, Cancelled }
