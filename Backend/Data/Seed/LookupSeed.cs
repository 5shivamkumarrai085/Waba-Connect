using System.Collections.Generic;

namespace WhatsAppCampaignApi.Data.Seed;

/// <summary>
/// Seed values for the contact status, source and language lookups.
///
/// <para>
/// These mirror the original ContactStatus / ContactSource enums <b>in full</b>, not just the
/// subset the old hardcoded endpoints exposed. That matters: ContactsController's
/// <c>/statuses</c> hid Active and Inactive, and <c>/sources</c> hid Web, Import and Manual —
/// yet contacts in the database genuinely carry those values. Seeding only the visible subset
/// would leave those rows unable to resolve a label and render blank.
/// </para>
/// <para>
/// The previously hidden values are seeded with <c>IsActive = false</c>, which reproduces the
/// old behaviour exactly — they stay out of pickers — but as data an admin can revisit rather
/// than a literal buried in a controller.
/// </para>
/// </summary>
public static class LookupSeed
{
    public record StatusDef(string Value, string Name, string Color, bool IsActive, int SortOrder);
    public record SourceDef(string Value, string Name, string Color, bool IsActive, int SortOrder);
    public record TypeDef(string Value, string Name, string Color, bool IsActive, int SortOrder);
    public record LanguageDef(string Code, string Name, string Color, bool IsDefault, int SortOrder);

    public static List<StatusDef> Statuses() => new()
    {
        // Visible in the original /statuses endpoint.
        new("New",        "New",         "#22C55E", true,  0),
        new("InProgress", "In Progress", "#3B82F6", true,  1),
        new("Contacted",  "Contacted",   "#EAB308", true,  2),
        new("Qualified",  "Qualified",   "#A855F7", true,  3),
        new("Closed",     "Closed",      "#EF4444", true,  4),

        // Present in the enum and in real data, but hidden by the old endpoint.
        new("Active",     "Active",      "#10B981", false, 5),
        new("Inactive",   "Inactive",    "#6B7280", false, 6),
    };

    public static List<SourceDef> Sources() => new()
    {
        // Visible in the original /sources endpoint.
        new("Facebook", "facebook", "#1877F2", true,  0),
        new("WhatsApp", "WhatsApp", "#25D366", true,  1),
        new("Saas",     "saas",     "#8B5CF6", true,  2),

        // Hidden by the old endpoint. "Import" in particular is in live use — contacts created
        // through CSV import carry it — so omitting it would blank their Source column.
        new("Web",      "Web",      "#0EA5E9", false, 3),
        new("Import",   "Import",   "#F59E0B", false, 4),
        new("Manual",   "Manual",   "#6B7280", false, 5),
    };

    /// <summary>
    /// Mirrors the original ContactType enum. Seeded IsSystem so the three values a contact may
    /// already carry cannot be deleted out from under existing rows.
    /// </summary>
    public static List<TypeDef> Types() => new()
    {
        new("Lead",     "Lead",     "#6366F1", true, 0),
        new("Customer", "Customer", "#22C55E", true, 1),
        new("Vendor",   "Vendor",   "#F97316", true, 2),
    };

    public static List<LanguageDef> Languages() => new()
    {
        new("en", "English",       "#3B82F6", true,  0),
        new("ms", "Bahasa Melayu", "#10B981", false, 1),
        new("zh", "Chinese",       "#EF4444", false, 2),
    };
}
