using WhatsAppCampaignApi.Models.DTOs.Setup;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Every OmniConnect setting the product has, declared once.
///
/// <para>
/// This is the schema, not the data. It says a field exists, what type it is, what it is called,
/// whether it is required and where its options come from; the chosen values live in the
/// <c>AppSettings</c> table. Serving it to the client is what keeps the page honest — a control
/// the UI renders is one this file declared, and therefore one the service knows how to validate
/// and store. A field added here appears on the page with no frontend change.
/// </para>
/// <para>
/// Option lists are named, never inlined: a select says "leadStatuses" and the service resolves
/// that against the lookup tables at request time. Statuses, sources, agents and models are all
/// things an operator edits elsewhere in the app, and a copy of them here would be wrong the first
/// time someone adds a status.
/// </para>
/// </summary>
public static class OmniSettingsCatalog
{
    // Option source names. Referenced by fields and resolved in OmniSettingsService.
    public const string LeadStatuses = "leadStatuses";
    public const string LeadSources = "leadSources";
    public const string Users = "users";
    public const string AiModels = "aiModels";
    public const string WebhookEvents = "webhookEvents";
    public const string HttpMethods = "httpMethods";
    public const string TimeZones = "timeZones";
    public const string ContactFields = "contactFields";

    /// <summary>
    /// The time zones this server knows, as IANA ids ("Asia/Kolkata"), labelled with their current
    /// UTC offset. Read from the operating system rather than typed out, so it matches exactly what
    /// the compliance rules can resolve.
    /// </summary>
    public static IReadOnlyList<(string Value, string Label)> TimeZoneCatalogue => LazyTimeZones.Value;

    /// <summary>
    /// Windows-to-IANA conversion returns some zones under their older IANA names; these are the
    /// current ones (IANA "backward" links — fixed reference data). Both names resolve.
    /// </summary>
    public static IReadOnlyDictionary<string, string> IanaAliases => CurrentIanaName;

    private static readonly Dictionary<string, string> CurrentIanaName = new(StringComparer.Ordinal)
    {
        ["Asia/Calcutta"] = "Asia/Kolkata",
        ["Asia/Katmandu"] = "Asia/Kathmandu",
        ["Asia/Rangoon"] = "Asia/Yangon",
        ["Asia/Saigon"] = "Asia/Ho_Chi_Minh",
        ["Europe/Kiev"] = "Europe/Kyiv",
        ["America/Godthab"] = "America/Nuuk",
        ["America/Buenos_Aires"] = "America/Argentina/Buenos_Aires",
        ["America/Indianapolis"] = "America/Indiana/Indianapolis",
        ["Atlantic/Faeroe"] = "Atlantic/Faroe",
        ["Pacific/Enderbury"] = "Pacific/Kanton",
    };

    private static readonly Lazy<IReadOnlyList<(string Value, string Label)>> LazyTimeZones = new(() =>
        TimeZoneInfo.GetSystemTimeZones()
            .Select(z =>
            {
                var id = z.HasIanaId ? z.Id
                    : TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out var iana) ? iana : null;
                if (id is null) return default((string Value, string Label, TimeSpan Offset)?);
                id = CurrentIanaName.GetValueOrDefault(id, id);
                var offset = z.GetUtcOffset(DateTime.UtcNow);
                var sign = offset < TimeSpan.Zero ? "-" : "+";
                return (id, $"(UTC{sign}{offset.Duration().Hours:00}:{offset.Duration().Minutes:00}) {id}", offset);
            })
            .Where(z => z is not null)
            .Select(z => z!.Value)
            .DistinctBy(z => z.Item1)
            .OrderBy(z => z.Item3).ThenBy(z => z.Item1)
            .Select(z => (z.Item1, z.Item2))
            .ToList());

    /// <summary>
    /// The WhatsApp Cloud API webhook fields an app can subscribe to.
    ///
    /// Hardcoded deliberately and uniquely: this is Meta's published field list, not our data.
    /// There is no table to read it from and no endpoint that enumerates it, so the honest place
    /// for it is beside the setting that uses it — clearly marked as external vocabulary.
    /// </summary>
    private static readonly (string Value, string Label)[] MetaWebhookFields =
    {
        ("account_alerts", "Account Alerts"),
        ("account_review_update", "Account Review Update"),
        ("account_settings_update", "Account Settings Update"),
        ("account_update", "Account Update"),
        ("automatic_events", "Automatic Events"),
        ("business_capability_update", "Business Capability Update"),
        ("business_status_update", "Business Status Update"),
        ("calls", "Calls"),
        ("flows", "Flows"),
        ("group_lifecycle_update", "Group Lifecycle Update"),
        ("group_participants_update", "Group Participants Update"),
        ("group_settings_update", "Group Settings Update"),
        ("group_status_update", "Group Status Update"),
        ("history", "History"),
        ("message_echoes", "Message Echoes"),
        ("message_template_components_update", "Message Template Components Update"),
        ("message_template_quality_update", "Message Template Quality Update"),
        ("message_template_status_update", "Message Template Status Update"),
        ("messages", "Messages"),
        ("messaging_handovers", "Messaging Handovers"),
        ("partner_solutions", "Partner Solutions"),
        ("payment_configuration_update", "Payment Configuration Update"),
        ("phone_number_name_update", "Phone Number Name Update"),
        ("phone_number_quality_update", "Phone Number Quality Update"),
        ("security", "Security"),
        ("web_app_data_sync", "Web App Data Sync"),
        ("web_message_echoes", "Web Message Echoes"),
        ("template_category_update", "Template Category Update"),
        ("tracking_events", "Tracking Events"),
        ("user_preferences", "User Preferences")
    };

    public static IReadOnlyList<(string Value, string Label)> WebhookFieldCatalogue => MetaWebhookFields;

    /// <summary>
    /// OpenAI chat models offered for the assistant.
    ///
    /// Also external vocabulary. Kept here rather than fetched from OpenAI's /models endpoint
    /// because that endpoint lists every model the key can reach — embeddings, audio, deprecated
    /// snapshots — and a chat-model picker offering "whisper-1" is worse than a short list that is
    /// occasionally a release behind.
    /// </summary>
    private static readonly (string Value, string Label)[] OpenAiChatModels =
    {
        ("gpt-3.5-turbo", "GPT-3.5 Turbo"),
        ("gpt-3.5-turbo-16k", "GPT-3.5 Turbo (16k Context)"),
        ("gpt-4", "GPT-4"),
        ("gpt-4-turbo", "GPT-4 Turbo"),
        ("gpt-4-turbo-preview", "GPT-4 Turbo Preview"),
        ("gpt-4-0125-preview", "GPT-4 (0125 Preview)"),
        ("gpt-4o", "GPT-4o"),
        ("gpt-4o-mini", "GPT-4o Mini")
    };

    public static IReadOnlyList<(string Value, string Label)> AiModelCatalogue => OpenAiChatModels;

    private static readonly (string Value, string Label)[] ResendMethods =
    {
        ("GET", "GET"),
        ("POST", "POST")
    };

    public static IReadOnlyList<(string Value, string Label)> HttpMethodCatalogue => ResendMethods;

    // ── Sections ─────────────────────────────────────────────────────────────

    private static OmniSettingsSectionDto[] AllSections =
    {
        new()
        {
            Key = "whatsapp-auto-lead",
            Label = "Whatsapp Auto Lead",
            Description = "Automate lead generation and management through WhatsApp integration.",
            Icon = "MessageSquare",
            Fields =
            {
                new()
                {
                    Key = "autoLead.enabled",
                    Label = "Acquire New Lead Automatically (convert new WhatsApp messages to lead)",
                    Type = "toggle"
                },
                new()
                {
                    Key = "autoLead.status",
                    Label = "Lead Status",
                    Type = "select",
                    OptionSource = LeadStatuses,
                    // Required only once the automation is on: an operator should be able to leave
                    // this page without choosing a status for a feature they never enabled.
                    RequiredWhenKey = "autoLead.enabled"
                },
                new()
                {
                    Key = "autoLead.source",
                    Label = "Lead Source",
                    Type = "select",
                    OptionSource = LeadSources,
                    RequiredWhenKey = "autoLead.enabled"
                },
                new()
                {
                    Key = "autoLead.assignedUserId",
                    Label = "Lead Assigned",
                    Type = "select",
                    OptionSource = Users,
                    RequiredWhenKey = "autoLead.enabled"
                }
            }
        },

        new()
        {
            Key = "stop-bot",
            Label = "Stop Bot",
            Description = "Configure settings to prevent unwanted bot activities in the system.",
            Icon = "ShieldCheck",
            Fields =
            {
                new()
                {
                    Key = "stopBot.keywords",
                    Label = "Stop Bots Keyword",
                    Type = "tags",
                    Placeholder = "Type and press Enter..",
                    Required = true
                },
                new()
                {
                    Key = "stopBot.restartAfterHours",
                    Label = "Restart Bots After",
                    Type = "number",
                    Unit = "Hours",
                    Min = 0,
                    // A year. Past this the setting is indistinguishable from "never restart", and
                    // an unbounded number here becomes a scheduler computing a date far outside
                    // any range the rest of the system handles.
                    Max = 8760
                }
            }
        },

        new()
        {
            Key = "whatsapp-webhook",
            Label = "Whatsapp Webhook",
            Description = "Manage webhooks to enable seamless integration with external services.",
            Icon = "Webhook",
            Fields =
            {
                new()
                {
                    Key = "webhook.resendEnabled",
                    Label = "Enable WebHooks Re-send",
                    Type = "toggle"
                },
                new()
                {
                    Key = "webhook.resendMethod",
                    Label = "Webhook Resend Method",
                    Type = "select",
                    OptionSource = HttpMethods
                },
                new()
                {
                    Key = "webhook.resendUrl",
                    Label = "WhatsApp received data will be resent to",
                    Type = "text",
                    Placeholder = "https://",
                    RequiredWhenKey = "webhook.resendEnabled"
                },
                new()
                {
                    Key = "webhook.events",
                    Label = "Webhook Event Fields",
                    Helper = "Select the events to subscribe to.",
                    Type = "multiselect",
                    OptionSource = WebhookEvents
                }
            }
        },

        new()
        {
            Key = "support-agent",
            Label = "Support Agent",
            Description = "Configure support agent settings for streamlined customer service.",
            Icon = "Megaphone",
            Fields =
            {
                new()
                {
                    Key = "supportAgent.restrictChatAccess",
                    Label = "Restrict chat access to assigned support agents only.",
                    Type = "toggle"
                }
            },
            Notes =
            {
                new()
                {
                    Tone = "warning",
                    Text = "When you enable the support agent feature, the staff will automatically be "
                         + "assigned to the chat. Admins can also assign a new agent from the chat page."
                }
            }
        },

        new()
        {
            Key = "notification-sound",
            Label = "Notification Sound",
            Description = "Customize notification sounds for better user experience.",
            Icon = "Bell",
            Fields =
            {
                new()
                {
                    Key = "notifications.chatSoundEnabled",
                    Label = "Enable WhatsApp chat notification sound",
                    Type = "toggle"
                }
            }
        },

        new()
        {
            Key = "ai-integration",
            Label = "AI Integration",
            Description = "Integrate AI-powered tools to enhance automation and decision-making.",
            Icon = "Cpu",
            Fields =
            {
                new()
                {
                    Key = "ai.openAiEnabled",
                    Label = "Activate OpenAI in the chat.",
                    Type = "toggle"
                },
                new()
                {
                    Key = "ai.chatModel",
                    Label = "Chat Model",
                    Type = "select",
                    OptionSource = AiModels,
                    RequiredWhenKey = "ai.openAiEnabled"
                },
                new()
                {
                    Key = "ai.openAiSecretKey",
                    Label = "OpenAI Secret Key",
                    Type = "password",
                    // Write-only. Stored encrypted and never returned — the reference UI rendered
                    // the live key in a readable input, which puts a working credential in front of
                    // anyone who can open the page, screenshot it, or read the response body.
                    IsSecret = true,
                    Placeholder = "sk-…",
                    RequiredWhenKey = "ai.openAiEnabled",
                    Helper = "Stored encrypted. Leave blank to keep the current key."
                }
            }
        },

        new()
        {
            Key = "personal-assistant",
            Label = "Personal Assistant",
            Description = "Control how the AI assistant replies in chat, and how customers can stop it.",
            Icon = "Bot",
            Fields =
            {
                new()
                {
                    Key = "assistant.stopKeywords",
                    Label = "Stop Assistant Keyword",
                    Type = "tags",
                    Placeholder = "Type and press Enter..",
                    Required = true
                },
                new()
                {
                    Key = "assistant.footerMessage",
                    Label = "Assistant Footer Message",
                    Type = "text",
                    Placeholder = "Send 'stop' to stop AI messages",
                    Required = true
                },
                new()
                {
                    Key = "assistant.messageDelaySeconds",
                    Label = "Assistant Message Delay",
                    Type = "number",
                    Unit = "seconds",
                    Required = true,
                    Min = 0,
                    // Five minutes. A reply the customer waits longer than this for is not a reply
                    // to the message they sent.
                    Max = 300
                }
            }
        },

        new()
        {
            Key = "auto-clear-chat-history",
            Label = "Auto Clear Chat History",
            Description = "Set up automated clearing of chat histories to maintain system performance and privacy.",
            Icon = "Trash2",
            Fields =
            {
                new()
                {
                    Key = "autoClear.enabled",
                    Label = "Activate Auto Clear Chat History",
                    Type = "toggle"
                },
                new()
                {
                    Key = "autoClear.retentionDays",
                    Label = "Auto Clear History Time",
                    Type = "number",
                    Unit = "Days",
                    RequiredWhenKey = "autoClear.enabled",
                    // At least a day: a retention of 0 would delete conversations as fast as they
                    // arrive, which is not a retention policy but data loss with a scheduler.
                    Min = 1,
                    Max = 3650
                }
            },
            Notes =
            {
                new()
                {
                    Tone = "warning",
                    Text = "Enabling Auto Clear Chat History will permanently delete chats older than "
                         + "the specified number of days each time the cleanup job runs."
                },
                new()
                {
                    Tone = "info",
                    Text = "This feature requires the background cleanup job to be running. "
                         + "Confirm it is scheduled before activating."
                }
            }
        }
    };

    public static IReadOnlyList<OmniSettingsSectionDto> Sections => AllSections;

    // Compliance is declared separately and appended, so the long list above stays in page order.
    static OmniSettingsCatalog()
    {
        AllSections = [.. AllSections, ChatOperationsSection, ComplianceSection, ContactsSection];
    }

    private static OmniSettingsSectionDto ChatOperationsSection => new()
    {
        Key = "chat-operations",
        Label = "Chat Routing & SLA",
        Description = "Who new conversations go to, how fast they must be answered, and when quiet ones close.",
        Icon = "MessageSquare",
        Fields =
        {
            new()
            {
                Key = "chatRouting.strategy", Label = "Assign new conversations", Type = "select",
                Options =
                {
                    new() { Value = "off", Label = "Manually (no automatic routing)" },
                    new() { Value = "roundRobin", Label = "Round robin" },
                    new() { Value = "leastBusy", Label = "To the agent with the fewest open conversations" }
                }
            },
            new() { Key = "chatRouting.agents", Label = "Agents in the rotation", Type = "multiselect", OptionSource = Users,
                    Helper = "Leave empty to include everyone who can use Chat. Connection access still applies." },
            new() { Key = "sla.firstResponseMinutes", Label = "First response within", Type = "number", Unit = "Minutes", Min = 0, Max = 10080,
                    Helper = "0 turns the first-response SLA off." },
            new() { Key = "sla.resolutionHours", Label = "Resolve within", Type = "number", Unit = "Hours", Min = 0, Max = 720,
                    Helper = "0 turns the resolution SLA off." },
            new() { Key = "autoClose.hours", Label = "Close conversations quiet for", Type = "number", Unit = "Hours", Min = 0, Max = 720,
                    Helper = "0 never closes them automatically. A new message from the customer reopens a closed conversation." },
            new() { Key = "autoClose.message", Label = "Closing message (WhatsApp)", Type = "text",
                    Placeholder = "We're closing this chat for now. Reply any time to reopen it.",
                    Helper = "Sent only within WhatsApp's 24-hour service window." }
        }
    };

    private static OmniSettingsSectionDto ComplianceSection => new()
    {
        Key = "compliance",
        Label = "Compliance",
        Description = "Consent, quiet hours and message limits applied to every campaign on every channel.",
        Icon = "ShieldCheck",
        Fields =
        {
            new() { Key = "compliance.timeZone", Label = "Default time zone", Type = "select", OptionSource = TimeZones,
                    Helper = "Used for quiet hours and local-time sends when a contact has no time zone of their own." },
            new() { Key = "compliance.quietHours.enabled", Label = "Enforce quiet hours", Type = "toggle",
                    Helper = "Marketing messages due inside the window are held and sent when it ends. Transactional campaigns are exempt." },
            new() { Key = "compliance.quietHours.startHour", Label = "Quiet hours start", Type = "number", Unit = ":00 (24h)", Min = 0, Max = 23,
                    RequiredWhenKey = "compliance.quietHours.enabled" },
            new() { Key = "compliance.quietHours.endHour", Label = "Quiet hours end", Type = "number", Unit = ":00 (24h)", Min = 0, Max = 23,
                    RequiredWhenKey = "compliance.quietHours.enabled" },
            new() { Key = "compliance.frequencyCap.maxMessages", Label = "Frequency cap", Type = "number", Unit = "messages", Min = 0, Max = 100,
                    Helper = "Most marketing messages one contact may receive in the window, across email and WhatsApp. 0 turns the cap off." },
            new() { Key = "compliance.frequencyCap.days", Label = "Frequency cap window", Type = "number", Unit = "Days", Min = 1, Max = 90 },
            new() { Key = "compliance.whatsapp.requireOptIn", Label = "Require WhatsApp opt-in", Type = "toggle",
                    Helper = "Marketing WhatsApp messages go only to contacts with a recorded opt-in." },
            new() { Key = "compliance.optOutKeywords", Label = "WhatsApp opt-out keywords", Type = "tags", Placeholder = "STOP, UNSUBSCRIBE…",
                    Helper = "A message consisting of one of these opts the contact out of all WhatsApp marketing." },
            new() { Key = "compliance.optInKeywords", Label = "WhatsApp opt-in keywords", Type = "tags", Placeholder = "START, SUBSCRIBE…" },
            new() { Key = "compliance.optOutReply", Label = "Reply to an opt-out", Type = "text",
                    Placeholder = "You have been unsubscribed from our WhatsApp messages. Reply START to subscribe again." },
            new() { Key = "compliance.optInReply", Label = "Reply to an opt-in", Type = "text",
                    Placeholder = "You are subscribed to our WhatsApp messages. Reply STOP to unsubscribe." },
            new() { Key = "compliance.topics", Label = "Preference-centre topics", Type = "tags", Placeholder = "marketing, offers, newsletters…",
                    Helper = "The topics recipients can opt in or out of individually. Campaigns choose one of these." }
        },
        Notes =
        {
            new() { Tone = "info", Text = "Opt-outs always apply, including to transactional campaigns. Every change is recorded with its source and evidence on the contact." }
        }
    };

    private static OmniSettingsSectionDto ContactsSection => new()
    {
        Key = "contacts",
        Label = "Contacts",
        Description = "Which details must be filled in when a contact is added or edited by hand or imported.",
        Icon = "Users",
        Fields =
        {
            new()
            {
                Key = Catalogs.ContactFieldCatalog.RequiredFieldsSettingKey, Label = "Also required", Type = "multiselect", OptionSource = ContactFields,
                Helper = "Name, phone, type, status and source are always required. Contacts created automatically from an incoming message are exempt."
            }
        },
        Notes =
        {
            new() { Tone = "info", Text = "The age shown on a contact and used by segments is worked out from the date of birth, so it never goes out of date." }
        }
    };

    /// <summary>The section for a key, or null when the slug is not one we publish.</summary>
    public static OmniSettingsSectionDto? FindSection(string? key) =>
        AllSections.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every field across every section — used to read stored values in one pass.</summary>
    public static IEnumerable<OmniSettingsFieldDto> AllFields => AllSections.SelectMany(s => s.Fields);
}
