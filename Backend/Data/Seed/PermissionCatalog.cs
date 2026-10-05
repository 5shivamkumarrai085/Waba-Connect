using System.Collections.Generic;
using System.Linq;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Data.Seed;

/// <summary>
/// The single source of truth for every grantable permission.
///
/// <para>
/// Capabilities are declared per-feature rather than as a shared enum because they are genuinely
/// ragged: Connect Account has Connect/Disconnect, bots have Clone, Template has LoadTemplate.
/// A uniform View/Create/Edit/Delete cross-product would render checkboxes that grant nothing.
/// </para>
/// <para>
/// Adding a permission here and restarting is enough — the seeder inserts new rows, the catalog
/// endpoint serves them, and the matrix UI renders them with no frontend change. Rows are never
/// deleted automatically; removing a feature is a deliberate migration.
/// </para>
/// </summary>
public static class PermissionCatalog
{
    public const string View = "View";
    public const string Create = "Create";
    public const string Edit = "Edit";
    public const string Delete = "Delete";
    public const string Clone = "Clone";

    private record FeatureDef(
        string Feature,
        string DisplayName,
        string Group,
        (string Capability, string Label)[] Capabilities);

    private static readonly (string Capability, string Label)[] Crud =
    {
        (View, "View"), (Create, "Create"), (Edit, "Edit"), (Delete, "Delete")
    };

    private static readonly (string Capability, string Label)[] CrudClone =
    {
        (View, "View"), (Create, "Create"), (Edit, "Edit"), (Delete, "Delete"), (Clone, "Clone")
    };

    private static readonly (string Capability, string Label)[] ViewOnly =
    {
        (View, "View")
    };

    private static readonly FeatureDef[] Features =
    {
        // ---- Overview -------------------------------------------------------------
        new("Dashboard", "Dashboard", "Overview", ViewOnly),
        new("Reporting",  "Reporting",  "Overview", new[] { (View, "View"), ("Export", "Export"), ("Manage", "Save/Share Reports"), ("Schedule", "Email reports on a schedule") }),

        // ---- Contacts -------------------------------------------------------------
        new("Contact", "Contact", "Contacts", new[]
        {
            (View, "View"), (Create, "Create"), (Edit, "Edit"), (Delete, "Delete"),
            ("Import", "Import"), ("Export", "Export")
        }),
        new("ContactGroup", "Contact Group", "Contacts", Crud),
        new("Segment", "Segment", "Contacts", new[] { (View, "View"), ("Manage", "Create, edit and delete") }),
        // Consent is a compliance record: changing it on someone's behalf is a separate grant.
        new("Consent", "Consent", "Contacts", new[] { (View, "View"), ("Manage", "Record opt-in / opt-out") }),
        new("ContactType",  "Contact Type",  "Contacts", Crud),

        // ---- Templates ------------------------------------------------------------
        // Edit was missing even though PUT /api/Templates/{id} has always existed, so template
        // edits were ungatable.
        new("Template", "Template", "Templates", new[]
        {
            (View, "View"), ("LoadTemplate", "Load template"),
            (Create, "Create"), (Edit, "Edit"), (Delete, "Delete")
        }),

        // ---- Marketing ------------------------------------------------------------
        new("Campaign",     "Campaign",      "Marketing", new[]
        {
            (View, "View"), (Create, "Create"), (Edit, "Edit"), (Delete, "Delete"), ("Send", "Send"),
            // Maker-checker: approving is a different grant from creating, and nobody may approve
            // their own campaign (enforced in CampaignService, not only by the checkbox).
            ("Approve", "Approve / reject"),
            ("Retry", "Retry failed recipients")
        }),
        new("BulkCampaign", "Bulk Campaign", "Marketing", new[]
        {
            (View, "View"), (Create, "Create"), (Delete, "Delete")
        }),
        new("MessageBot",   "Message Bot",   "Marketing", CrudClone),
        new("TemplateBot",  "Template Bot",  "Marketing", CrudClone),
        // No Clone: unlike the other two bot types, BotFlows has no clone endpoint, so the
        // checkbox would grant nothing.
        new("BotFlow",      "Bot Flow",      "Marketing", Crud),

        // ---- Support --------------------------------------------------------------
        // "Assign" was removed: no conversation-assignment endpoint exists, so the checkbox
        // granted nothing. Delete covers removing messages and conversations from OmniConnect —
        // it never removes anything from the recipient's phone.
        new("Chat", "Chat", "Support", new[]
        {
            (View, "View"), ("Send", "Send"), ("InitiateChat", "Initiate chat"), (Delete, "Delete"),
            // Assigning a conversation to an agent (the inbox's assign menu and routing overrides).
            ("Assign", "Assign conversations")
        }),
        new("CannedReply", "Canned Reply", "Support", Crud),

        // ---- Connections ----------------------------------------------------------
        // Deliberately not plain CRUD — connecting a WABA account is an OAuth handshake, not a
        // create. Edit renames the connection record; Delete removes it entirely, which is a
        // different and more destructive act than Disconnect.
        new("ConnectAccount", "Connect Account", "Connections", new[]
        {
            (View, "View"), ("Connect", "Connect"), (Edit, "Edit"),
            ("Disconnect", "Disconnect"), (Delete, "Delete")
        }),
        new("ConnectionAccess", "Connection Access", "Connections", new[]
        {
            (View, "View"), ("Assign", "Assign"), (Delete, "Delete")
        }),

        // Mirrors ConnectAccount's shape rather than plain CRUD, for the same reason: configuring
        // a sending provider is not a "create", and Test is called out separately because sending
        // a test email costs real send quota and reputation against the account.
        new("EmailConnection", "Email Connection", "Connections", new[]
        {
            (View, "View"), ("Connect", "Connect"), (Edit, "Edit"),
            ("Disconnect", "Disconnect"), (Delete, "Delete"), ("Test", "Test connection / send test")
        }),

        // Domain authentication. Separate from the connection because publishing DNS records is
        // usually somebody else's job, and granting it should not also grant credential access.
        new("EmailDomain", "Email Domain", "Connections", new[]
        {
            (View, "View"), ("Manage", "Add and verify domains")
        }),

        // Suppression is a compliance surface: removing an address that unsubscribed means mailing
        // somebody who asked not to be, so Manage is deliberately a distinct grant from View.
        new("EmailSuppression", "Email Suppression", "Connections", new[]
        {
            (View, "View"), ("Manage", "Add and remove entries")
        }),

        // Operational visibility into the send queue. Requeue is separate because replaying
        // dead-lettered jobs re-attempts real sends.
        new("EmailQueue", "Email Queue", "Connections", new[]
        {
            (View, "View"), ("Requeue", "Requeue dead-lettered jobs")
        }),

        // ---- Setup ----------------------------------------------------------------
        new("Setup",         "Setup",          "Setup", ViewOnly),
        new("User",          "User",           "Setup", Crud),
        new("Role",          "Role",           "Setup", Crud),
        new("Status",        "Status",         "Setup", Crud),
        new("Source",        "Source",         "Setup", Crud),
        new("AiPrompt",      "Ai Prompt",      "Setup", Crud),
        new("Webhook",       "Webhook",        "Setup", new[] { (View, "View"), ("Manage", "Create, edit, test and replay") }),
        new("Language",      "Language",       "Setup", new[]
        {
            (View, "View"), (Create, "Create"), (Edit, "Edit"), (Delete, "Delete"),
            ("Translate", "Translate")
        }),
        // Create and Delete arrived with the email channel: the templates were previously a fixed
        // set of seeded system notifications, and are now also the source of truth for campaign
        // content, which operators need to author. View/Edit/Toggle are unchanged, so existing
        // role grants keep working exactly as before.
        new("EmailTemplate", "Email Template", "Setup", new[]
        {
            (View, "View"), (Create, "Create"), (Edit, "Edit"), (Delete, "Delete"),
            ("Toggle", "Enable/Disable")
        }),
        // View only: the audit log is append-only, so there is nothing to delete or clear.
        new("ActivityLog",   "Audit Log",      "Setup", new[]
        {
            (View, "View")
        }),
        new("SystemLog",     "System Log",     "Setup", new[]
        {
            (View, "View"), (Delete, "Delete"), ("Clear", "Clear all logs")
        }),
        // View and Edit only. Settings are not created or deleted — the catalogue decides which
        // exist — so a Create or Delete capability here would grant nothing.
        new("OmniSettings",  "OmniConnect Settings", "Setup", new[]
        {
            (View, "View"), (Edit, "Edit")
        }),
    };

    /// <summary>Every permission in the catalogue, in stable display order.</summary>
    public static List<Permission> All()
    {
        var result = new List<Permission>();
        var order = 0;

        foreach (var feature in Features)
        {
            foreach (var (capability, label) in feature.Capabilities)
            {
                result.Add(new Permission
                {
                    Key = $"{feature.Feature}.{capability}",
                    Feature = feature.Feature,
                    Capability = capability,
                    FeatureDisplayName = feature.DisplayName,
                    CapabilityDisplayName = label,
                    GroupName = feature.Group,
                    SortOrder = order++
                });
            }
        }

        return result;
    }

    /// <summary>Every key in the catalogue — used to grant "everything" to a role.</summary>
    public static List<string> AllKeys() => All().Select(p => p.Key).ToList();

    /// <summary>Every "*.View" key — the Read Only User grant.</summary>
    public static List<string> AllViewKeys() =>
        All().Where(p => p.Capability == View).Select(p => p.Key).ToList();

    /// <summary>All keys for the given features, whatever their capabilities.</summary>
    public static List<string> KeysForFeatures(params string[] features)
    {
        var wanted = new HashSet<string>(features);
        return All().Where(p => wanted.Contains(p.Feature)).Select(p => p.Key).ToList();
    }
}
