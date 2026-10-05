using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Catalogs;

/// <summary>One choice in a picker: the stored value, what people read, and optional help text.</summary>
public sealed record Option(string Value, string Label, string? Description = null);

/// <summary>A numeric range with the value a new form starts at.</summary>
public sealed record NumberRange(int Min, int Max, int Default);

// Every option list and limit the newer features show or enforce lives here, once. The server
// validates against these same values and the UI reads them from GET api/reference/*, so the two
// can never disagree and nothing is typed twice.

/// <summary>A/B tests, follow-ups and retries.</summary>
public static class CampaignFeatureCatalog
{
    public static readonly Option[] EmailAbMetrics =
    [
        new("open", "Open rate", "Share of recipients who opened the email."),
        new("click", "Click rate", "Share of recipients who clicked a link."),
        new("reply", "Reply rate", "Share of recipients who replied.")
    ];

    public static readonly Option[] WhatsAppAbMetrics =
    [
        new("read", "Read rate", "Share of recipients who read the message."),
        new("reply", "Reply rate", "Share of recipients who replied.")
    ];

    public static Option[] AbMetrics(MessageChannel channel) => channel == MessageChannel.Email ? EmailAbMetrics : WhatsAppAbMetrics;

    /// <summary>Variants besides the campaign's own template (A).</summary>
    public const int MaxExtraVariants = 4;
    public static readonly NumberRange AbTestPercent = new(10, 100, 20);
    public static readonly NumberRange AbDecideAfterHours = new(1, 168, 4);

    /// <summary>
    /// An automatic winner is picked only once every variant has had at least this many messages
    /// accepted; otherwise the decision waits <see cref="AbRecheckMinutes"/> and looks again.
    /// Choosing the manual winner is always allowed.
    /// </summary>
    public const int AbMinimumSentPerVariant = 1;
    public const int AbRecheckMinutes = 30;

    /// <summary>
    /// When no variant has a single open (or read, click, reply) by decision time, the test keeps
    /// waiting this many extra hours before keeping variant A — a 0% vs 0% "win" is not a result.
    /// Default is what the server uses; the range bounds a future per-campaign setting.
    /// </summary>
    public static readonly NumberRange AbNoSignalGraceHours = new(0, 168, 24);

    /// <summary>Why a test's winner was chosen, as stored on the campaign and shown on its page.</summary>
    public const string AbReasonBestRate = "best-rate";
    public const string AbReasonNoSignal = "no-signal";
    public const string AbReasonManual = "manual";

    /// <summary>What the campaign page says about how the winner was chosen.</summary>
    public static readonly Option[] AbDecisionReasons =
    [
        new(AbReasonBestRate, "Best rate", "The variant with the highest rate won."),
        new(AbReasonNoSignal, "No signal", "No variant had any engagement in time, so variant A was kept."),
        new(AbReasonManual, "Chosen manually", "Someone picked the winner before the decision time.")
    ];

    public static readonly Option[] EmailFollowUpConditions =
    [
        new("NotOpened", "did not open"),
        new("NotClicked", "did not click"),
        new("Clicked", "clicked a link"),
        new("Replied", "replied"),
        new("NotReplied", "did not reply"),
        new("Failed", "could not be sent to")
    ];

    public static readonly Option[] WhatsAppFollowUpConditions =
    [
        new("NotRead", "did not read"),
        new("Replied", "replied"),
        new("NotReplied", "did not reply"),
        new("Failed", "could not be sent to")
    ];

    public static Option[] FollowUpConditions(MessageChannel channel) =>
        channel == MessageChannel.Email ? EmailFollowUpConditions : WhatsAppFollowUpConditions;

    public static readonly Option[] FollowUpActions =
    [
        new("send", "Send a follow-up message"),
        new("tag", "Add a tag to the contact")
    ];

    public const int MaxFollowUps = 5;

    /// <summary>Addresses a proof send may go to at once.</summary>
    public const int MaxProofAddresses = 5;
    public static readonly NumberRange FollowUpDelayHours = new(1, 720, 48);
}

/// <summary>Scheduled report emails.</summary>
public static class ReportScheduleCatalog
{
    public static readonly Option[] Frequencies =
    [
        new("Daily", "Daily"),
        new("Weekly", "Weekly"),
        new("Monthly", "Monthly")
    ];

    public static readonly Option[] Formats =
    [
        new("xlsx", "Excel (.xlsx)"),
        new("csv", "CSV (.csv)"),
        new("pdf", "PDF (.pdf)")
    ];

    public static readonly NumberRange LookbackDays = new(1, 366, 7);
    public const int MaxRecipients = 20;

    /// <summary>Monthly runs stop at 28 so every month has the day.</summary>
    public const int MaxDayOfMonth = 28;
    public const string DefaultTimeOfDay = "09:00";
    public const int DefaultDayOfWeek = 1;
}

/// <summary>WhatsApp template authoring, per Meta's rules.</summary>
public static class TemplateAuthoringCatalog
{
    public sealed record ButtonType(string Value, string Label, int MaxCount, string Description);

    public static readonly ButtonType[] ButtonTypes =
    [
        new("QUICK_REPLY", "Quick reply", 10, "A tappable answer the customer sends back."),
        new("URL", "Website link", 2, "Opens a web page. May end in {{1}} for a per-recipient ending."),
        new("PHONE_NUMBER", "Call phone number", 1, "Starts a phone call."),
        new("COPY_CODE", "Copy offer code", 1, "Copies a coupon code for the customer.")
    ];

    /// <summary>
    /// Categories a template can be written in here. Authentication templates follow Meta's fixed
    /// one-time-password layout, so they are created in WhatsApp Manager and synced instead.
    /// </summary>
    public static readonly Option[] Categories =
    [
        new(nameof(TemplateCategory.Marketing), "Marketing", "Offers, announcements and other promotional messages."),
        new(nameof(TemplateCategory.Utility), "Utility", "Updates about something the customer already started, such as an order.")
    ];

    public const int MaxButtons = 10;
    public const int MaxButtonLabelLength = 25;
    public const int MaxBodyLength = 1024;
    public const int MaxHeaderLength = 60;
    public const int MaxFooterLength = 60;
    public const int MaxNameLength = 512;

    /// <summary>Lower-case letters, digits and underscores — Meta's rule for template names.</summary>
    public const string NamePattern = "^[a-z0-9_]{1,512}$";
}

/// <summary>Chat inbox filters, conversation states and the composer's limits.</summary>
public static class ChatCatalog
{
    public static readonly Option[] ConversationStatuses =
    [
        new(nameof(ConversationStatus.Open), "Needs reply", "The customer is waiting for an answer."),
        new(nameof(ConversationStatus.Pending), "Waiting on customer", "An agent answered; the next move is the customer's."),
        new(nameof(ConversationStatus.Resolved), "Resolved", "Done. A new message reopens it."),
        new(nameof(ConversationStatus.Closed), "Closed", "Closed automatically after a quiet period.")
    ];

    /// <summary>The inbox status filter: the statuses plus "active" (open or waiting) and "all".</summary>
    public static readonly Option[] StateFilters =
    [
        new("active", "Active (needs reply or waiting)"),
        .. ConversationStatuses,
        new("all", "All statuses")
    ];

    /// <summary>The inbox read filter. The values are what the conversations endpoint accepts.</summary>
    public static readonly Option[] ReadFilters =
    [
        new("All Chats", "All conversations"),
        new("Unread Chats", "Unread only")
    ];

    public static readonly Option[] AssigneeFilters =
    [
        new("", "Anyone"),
        new("me", "Assigned to me"),
        new("unassigned", "Unassigned")
    ];

    public const string SortNewest = "newest";
    public const string SortOldest = "oldest";

    /// <summary>Inbox order, by last activity. The first is the default.</summary>
    public static readonly Option[] SortOrders =
    [
        new(SortNewest, "Newest", "Latest activity first."),
        new(SortOldest, "Oldest", "Longest-waiting activity first.")
    ];

    /// <summary>A quick view is a named combination of the read and owner filters, shown as a tab with a count.</summary>
    public sealed record QuickView(string Value, string Label, string Description, string ReadFilter, string AssigneeFilter);

    public const string QuickViewAll = "all";
    public const string QuickViewUnread = "unread";
    public const string QuickViewMine = "mine";

    /// <summary>
    /// The inbox's tabs. Declared after the filters they reference (static fields initialise in
    /// order). Counts come from GET api/Chat/conversations/counts.
    /// </summary>
    public static readonly QuickView[] QuickViews =
    [
        new(QuickViewAll, "All", "Every conversation in view.", ReadFilters[0].Value, AssigneeFilters[0].Value),
        new(QuickViewUnread, "Unread", "Conversations with messages nobody has read yet.", ReadFilters[1].Value, AssigneeFilters[0].Value),
        new(QuickViewMine, "Mine", "Conversations assigned to you.", ReadFilters[0].Value, AssigneeFilters[1].Value)
    ];

    public const int MaxReplyButtons = 3;
    public const int MaxReplyButtonLength = 20;
    public const int MaxInteractiveBodyLength = 1024;
}

/// <summary>Outbound webhook delivery log.</summary>
public static class WebhookCatalog
{
    public static readonly Option[] DeliveryStatuses =
    [
        new("Delivered", "Delivered"),
        new("Failed", "Failed"),
        new("Pending", "Pending"),
        new("Skipped", "Skipped")
    ];
}

/// <summary>Consent: channels, states and where a record came from.</summary>
public static class ConsentCatalog
{
    public static class Sources
    {
        public const string UnsubscribeLink = "unsubscribe-link";
        public const string PreferenceCentre = "preference-centre";
        public const string Keyword = "keyword";
        public const string Agent = "agent";
        public const string Import = "import";
        public const string Api = "api";
    }

    public static readonly Option[] Channels =
    [
        new(nameof(MessageChannel.Email), "Email"),
        new(nameof(MessageChannel.WhatsApp), "WhatsApp")
    ];

    public static readonly Option[] Statuses =
    [
        new(nameof(ConsentStatus.OptedIn), "Opted in"),
        new(nameof(ConsentStatus.OptedOut), "Opted out")
    ];

    public static readonly Option[] SourceLabels =
    [
        new(Sources.UnsubscribeLink, "Unsubscribe link"),
        new(Sources.PreferenceCentre, "Preference centre"),
        new(Sources.Keyword, "WhatsApp keyword"),
        new(Sources.Agent, "Recorded by an agent"),
        new(Sources.Import, "Import"),
        new(Sources.Api, "API")
    ];
}

/// <summary>What a segment rule can test, and how.</summary>
public static class SegmentFieldCatalog
{
    /// <param name="Kind">text, tag, group, date, consent or activity — decides the value editor.</param>
    /// <param name="Lookup">For text fields with a managed list: statuses, types or sources.</param>
    public sealed record Field(string Value, string Label, string Kind, string? Lookup = null);

    public static readonly Field[] Fields =
    [
        new("type", "Contact type", "text", "types"),
        new("status", "Status", "text", "statuses"),
        new("source", "Source", "text", "sources"),
        new("assignedTo", "Assigned to", "text"),
        new("country", "Country", "text"),
        new("state", "State", "text"),
        new("city", "City", "text"),
        new("company", "Company", "text"),
        new("timeZone", "Time zone", "text"),
        new("email", "Email", "text"),
        new("phone", "Phone", "text"),
        new("adSourceId", "Came from ad (ad id)", "text"),
        new("adHeadline", "Came from ad (headline)", "text"),
        new("tag", "Tag", "tag"),
        new("group", "Group", "group"),
        new("createdAt", "Created", "date"),
        new("age", "Age", "number"),
        new("consent", "Consent", "consent"),
        new("opened", "Opened an email", "activity"),
        new("clicked", "Clicked a link", "activity"),
        new("replied", "Replied", "activity"),
        new("received", "Received a campaign", "activity")
    ];

    public static readonly IReadOnlyDictionary<string, Option[]> Operators = new Dictionary<string, Option[]>
    {
        ["text"] = [new("eq", "is"), new("neq", "is not"), new("contains", "contains"), new("empty", "is empty"), new("notEmpty", "is not empty")],
        ["tag"] = [new("has", "has"), new("notHas", "does not have")],
        ["group"] = [new("in", "is in"), new("notIn", "is not in")],
        ["date"] = [new("withinDays", "in the last"), new("olderThanDays", "more than")],
        ["number"] = [new("gte", "is at least"), new("lte", "is at most"), new("empty", "is unknown")],
        ["consent"] = [new("eq", "is"), new("neq", "is not")],
        ["activity"] = [new("withinDays", "in the last"), new("notWithinDays", "not in the last")]
    };

    public static readonly NumberRange Days = new(1, 3650, 30);
}

/// <summary>
/// The fields of a contact, what kind of value each holds, and which must be filled in. Name,
/// phone, type, status and source are always required (a contact cannot be messaged or filed
/// without them); administrators choose which of the others are required too, under
/// OmniConnect Settings › Contacts. Contacts created automatically from an incoming message are
/// exempt, because they arrive with nothing but a phone number.
/// </summary>
public static class ContactFieldCatalog
{
    /// <summary>The OmniSettings key holding the administrator's choice (a JSON array of keys).</summary>
    public const string RequiredFieldsSettingKey = "contacts.requiredFields";

    /// <param name="Kind">text, phone, email, url, date or lookup — decides the input and its checks.</param>
    /// <param name="AlwaysRequired">Required whatever the setting says.</param>
    /// <param name="RequiredByDefault">Required until an administrator changes the setting.</param>
    public sealed record Field(string Key, string Label, string Kind, int? MaxLength, bool AlwaysRequired = false, bool RequiredByDefault = false);

    public static readonly Field[] Fields =
    [
        new("name", "Name", "text", 100, AlwaysRequired: true),
        new("phone", "Phone", "phone", 20, AlwaysRequired: true),
        new("type", "Type", "lookup", 50, AlwaysRequired: true),
        new("status", "Status", "lookup", 50, AlwaysRequired: true),
        new("source", "Source", "lookup", 50, AlwaysRequired: true),
        new("email", "Email", "email", 200, RequiredByDefault: true),
        new("dateOfBirth", "Date of birth", "date", null, RequiredByDefault: true),
        new("company", "Company", "text", 200),
        new("website", "Website", "url", 500),
        new("assignedTo", "Assigned to", "text", 100),
        new("city", "City", "text", 100),
        new("state", "State", "text", 100),
        new("country", "Country", "text", 100),
        new("timeZone", "Time zone", "text", 64),
        new("zipCode", "Zip code", "text", 20),
        new("address", "Address", "text", 500),
        new("description", "Description", "text", 2000)
    ];

    /// <summary>Fields an administrator may make required (the rest are required already).</summary>
    public static IEnumerable<Field> Configurable => Fields.Where(f => !f.AlwaysRequired);

    public static readonly string[] DefaultRequired = Fields.Where(f => f.AlwaysRequired || f.RequiredByDefault).Select(f => f.Key).ToArray();

    /// <summary>Accepted ages, from the date of birth. Default is the age the date picker opens at.</summary>
    public static readonly NumberRange AgeYears = new(0, 120, 30);

    /// <summary>The name is first and last name joined, so a single word is refused below this length.</summary>
    public const int NameMinLength = 2;

    /// <summary>E.164: a plus, a country code that does not start with 0, then 6 to 14 digits.</summary>
    public const string PhonePattern = @"^\+[1-9]\d{6,14}$";

    public static Field? Find(string key) => Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The effective required set: the always-required fields plus the chosen ones.</summary>
    public static HashSet<string> Resolve(IEnumerable<string>? chosen)
    {
        var set = new HashSet<string>(Fields.Where(f => f.AlwaysRequired).Select(f => f.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var key in chosen ?? DefaultRequired)
        {
            if (Find(key) is { } field) set.Add(field.Key);
        }
        return set;
    }

    /// <summary>The stored setting (a JSON array of keys), or null when it is absent or unreadable.</summary>
    public static List<string>? ParseStored(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Whole years between a date of birth and a day.</summary>
    public static int AgeOn(DateOnly dateOfBirth, DateOnly on)
    {
        var age = on.Year - dateOfBirth.Year;
        if (on < dateOfBirth.AddYears(age)) age--;
        return age;
    }
}

/// <summary>
/// What the email connection form offers: the transport security modes, the well-known ports and
/// the security each one implies, and the accepted send-rate range. The server enforces the same
/// port-to-security pairing when it connects (a wrong pairing hangs rather than failing).
/// </summary>
public static class EmailConnectionCatalog
{
    public static readonly Option[] SecurityModes =
    [
        new(nameof(SmtpSecurityMode.SslOnConnect), "SSL/TLS", "Encrypted from the first byte. Usually port 465 (sending) or 993 (receiving)."),
        new(nameof(SmtpSecurityMode.StartTls), "STARTTLS", "Connects, then upgrades to TLS; the upgrade is required. Usually port 587 (sending) or 143 (receiving)."),
        new(nameof(SmtpSecurityMode.None), "None (not encrypted)", "Only for a relay inside your own network: the password and the mail travel in clear text.")
    ];

    /// <param name="Security">The <see cref="SmtpSecurityMode"/> the port requires.</param>
    public sealed record PortPreset(int Port, string Security, string Label);

    public static readonly PortPreset[] SmtpPorts =
    [
        new(465, nameof(SmtpSecurityMode.SslOnConnect), "465 — SSL/TLS"),
        new(587, nameof(SmtpSecurityMode.StartTls), "587 — STARTTLS (submission)"),
        new(2525, nameof(SmtpSecurityMode.StartTls), "2525 — STARTTLS (alternative)")
    ];

    public static readonly PortPreset[] ImapPorts =
    [
        new(993, nameof(SmtpSecurityMode.SslOnConnect), "993 — SSL/TLS"),
        new(143, nameof(SmtpSecurityMode.StartTls), "143 — STARTTLS")
    ];

    /// <summary>Messages per second a connection may send; mirrors the request DTO's range.</summary>
    public const double MinSendRatePerSecond = 0.1;
    public const double MaxSendRatePerSecond = 1000;
}

/// <summary>
/// Whether the public address tracking pixels, click links and unsubscribe links are built on can
/// actually be reached by recipients. Each reason has the sentence the pre-flight check and the
/// health report show.
/// </summary>
public static class PublicEndpointCatalog
{
    public const string Empty = "empty";
    public const string Localhost = "localhost";
    public const string PrivateNetwork = "private-network";
    public const string NotHttps = "not-https";
    public const string Unreachable = "unreachable";
    public const string WrongInstance = "wrong-instance";
    public const string Ok = "ok";

    /// <summary>How long the reachability probe waits for an answer.</summary>
    public const int ProbeTimeoutSeconds = 5;

    /// <summary>How long a probe result is reused before the address is checked again.</summary>
    public const int ProbeCacheMinutes = 5;

    public static readonly IReadOnlyDictionary<string, string> Reasons = new Dictionary<string, string>
    {
        [Empty] = "no public address is configured (App:PublicBaseUrl)",
        [Localhost] = "it points at this computer (localhost), which recipients' mail apps cannot reach",
        [PrivateNetwork] = "it is a private network address, which the internet cannot reach",
        [NotHttps] = "it is not https; mail apps block plain-http images and Gmail requires https for one-click unsubscribe",
        [Unreachable] = "it did not answer from the internet (is the server or tunnel running?)",
        [WrongInstance] = "it answered, but from a different server than this one",
        [Ok] = "it is reachable"
    };

    public static string Describe(string reason) => Reasons.TryGetValue(reason, out var text) ? text : reason;
}

/// <summary>
/// How a plain-text delivery report (the kind Exim, cPanel, qmail and older Postfix send instead
/// of an RFC 3464 multipart/report) is recognised and read. Matching is on the report's sender,
/// its subject, and the text above the returned copy of the original message.
/// </summary>
public static class BounceCatalog
{
    /// <summary>Local parts of the addresses mail servers send delivery reports from.</summary>
    public static readonly string[] SenderLocalParts = ["mailer-daemon", "postmaster", "mail-daemon", "mailerdaemon"];

    /// <summary>Subjects delivery reports use (matched as case-insensitive substrings).</summary>
    public static readonly string[] SubjectPatterns =
    [
        "mail delivery failed", "undelivered mail returned to sender", "delivery status notification",
        "undeliverable", "returned mail", "delivery failure", "failure notice", "message delayed",
        "delivery has failed", "could not be delivered", "warning: message"
    ];

    /// <summary>Lines that introduce the returned copy of the original message; the report is the text above them.</summary>
    public static readonly string[] ReturnedCopyMarkers =
    [
        "this is a copy of the message", "below this line is a copy of the message", "original message follows",
        "----- original message -----", "------- original message", "the original message was received",
        "original message headers"
    ];

    /// <summary>Wording that marks a failure as permanent when the report carries no status code.</summary>
    public static readonly string[] PermanentPhrases =
    [
        "permanent error", "no such user", "user unknown", "unknown user", "does not exist", "mailbox unavailable",
        "address rejected", "no mailbox", "account has been disabled", "recipient not found", "invalid recipient"
    ];

    /// <summary>A bounce without the original Message-ID is matched to the latest send to that address within this many days.</summary>
    public const int MatchWindowDays = 7;
}

/// <summary>
/// The dashboard's Recent Activity: which audit modules count as business activity (sign-ins,
/// access denials and system logs do not), and which icon family each one shows with.
/// </summary>
public static class RecentActivityCatalog
{
    /// <summary>Audit module → the card's item type (its icon and colour).</summary>
    public static readonly IReadOnlyDictionary<string, string> ModuleTypes = new Dictionary<string, string>
    {
        ["Campaign"] = "campaign", ["BulkCampaign"] = "campaign",
        ["Contact"] = "contact", ["ContactGroup"] = "contact", ["ContactNote"] = "contact", ["Consent"] = "contact",
        ["Template"] = "template", ["EmailTemplate"] = "template",
        ["BotFlow"] = "botflow", ["MessageBot"] = "botflow", ["TemplateBot"] = "botflow",
        ["Chat"] = "chat", ["EmailReply"] = "chat",
        ["Segment"] = "segment",
        ["Connection"] = "connection", ["EmailConnection"] = "connection", ["EmailSender"] = "connection"
    };

    /// <summary>How many items the card shows.</summary>
    public const int Take = 8;

    /// <summary>Who an event is attributed to when no person did it (workers, webhooks, the scheduler).</summary>
    public const string SystemActor = "System";
}
