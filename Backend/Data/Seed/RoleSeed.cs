using System.Collections.Generic;
using System.Linq;

namespace WhatsAppCampaignApi.Data.Seed;

/// <summary>
/// The six default roles. All are marked IsSystem, so they can be renamed and re-scoped but not
/// deleted — deleting one would silently strip access from every user holding it.
/// </summary>
public static class RoleSeed
{
    public record RoleDef(
        string Name,
        string Description,
        bool IsAdministrator,
        List<string> PermissionKeys);

    public static List<RoleDef> All()
    {
        // Admin gets everything except the handful of irreversible actions reserved for
        // Super Admin: deleting people and roles, and wiping audit/system logs.
        var adminExclusions = new HashSet<string>
        {
            "User.Delete",
            "Role.Delete",
            "SystemLog.Clear",
            "ConnectAccount.Disconnect"
        };

        var adminKeys = PermissionCatalog.AllKeys()
            .Where(k => !adminExclusions.Contains(k))
            .ToList();

        // Manager runs day-to-day marketing and support: full control of the operational
        // entities, read-only visibility elsewhere, and nothing that *changes* infrastructure
        // (WABA connections) or the audit trail.
        //
        // "Doesn't touch infrastructure" originally meant no ConnectAccount keys at all, which
        // went too far: every campaign and every chat message has to name the connection it
        // sends from, so a Manager with full Campaign.* and Chat.* still could not create a
        // campaign — the wizard's connection dropdown came back 403 and rendered empty.
        // ConnectAccount.View restores the read; Connect/Edit/Disconnect/Delete stay withheld.
        var managerKeys = PermissionCatalog.KeysForFeatures(
                "Contact", "ContactGroup", "ContactType", "Campaign", "BulkCampaign", "Template",
                "TemplateBot", "MessageBot", "BotFlow", "CannedReply", "Status", "Source")
            .Concat(PermissionCatalog.KeysForFeatures("Chat"))
            .Concat(new[]
            {
                "Dashboard.View", "Reporting.View", "Reporting.Export",
                "ActivityLog.View", "User.View", "Role.View",
                "Language.View", "AiPrompt.View", "Setup.View",
                "ConnectAccount.View"
            })
            .Distinct()
            .ToList();

        // Agent works the inbox. Can create and update contacts but never delete anything —
        // including chat messages, which is why Chat.Delete is absent here.
        // Template.LoadTemplate is deliberately not granted either: it triggers a WhatsApp-side
        // sync, which is an infrastructure action rather than an inbox one.
        var agentKeys = new List<string>
        {
            "Dashboard.View",
            "Contact.View", "Contact.Create", "Contact.Edit",
            "ContactGroup.View", "ContactType.View",
            "Chat.View", "Chat.Send", "Chat.InitiateChat",
            "Template.View",
            "CannedReply.View",
            "Campaign.View",
            // Read-only sight of connections. Sending a message means choosing which WABA number
            // it leaves from, and the chat sidebar's connection selector reads /connections —
            // without this an agent granted Chat.Send got an empty selector and could not send
            // at all. They still cannot connect, edit, disconnect or delete one.
            "ConnectAccount.View"
        };

        var normalUserKeys = new List<string>
        {
            "Dashboard.View",
            "Contact.View",
            "Chat.View", "Chat.Send",
            "Template.View",
            // Same reason as Agent above — Chat.Send is unusable without it.
            "ConnectAccount.View"
        };

        return new List<RoleDef>
        {
            new("Super Admin",
                "Unrestricted access to every feature and setting.",
                IsAdministrator: true,
                // Intentionally empty: administrator access short-circuits every permission
                // check, so materializing rows here would go stale the moment a new permission
                // is added to the catalogue.
                new List<string>()),

            new("Admin",
                "Full access except deleting users/roles and clearing logs.",
                IsAdministrator: false,
                adminKeys),

            new("Manager",
                "Runs campaigns, contacts and bots. Read-only on setup and reporting.",
                IsAdministrator: false,
                managerKeys),

            new("Agent",
                "Handles conversations and contacts. Cannot delete records.",
                IsAdministrator: false,
                agentKeys),

            new("Normal User",
                "Basic access to chat, contacts and the dashboard.",
                IsAdministrator: false,
                normalUserKeys),

            new("Read Only User",
                "Can view everything but change nothing.",
                IsAdministrator: false,
                PermissionCatalog.AllViewKeys()),
        };
    }
}
