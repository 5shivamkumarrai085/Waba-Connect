using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Data;

/// <summary>Reusable audit log filters.</summary>
public static class AuditQueries
{
    /// <summary>
    /// The history of particular records, given as "EntityType:EntityId" pairs (for example
    /// "Contact:80", "ChatConversation:267"). Served by the (EntityType, EntityId) index. A
    /// malformed pair is ignored; if none is valid the result is empty, never the whole log.
    /// </summary>
    public static IQueryable<AuditLog> ForEntities(IQueryable<AuditLog> query, IEnumerable<string> entities)
    {
        var pairs = entities
            .Select(e => e.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2 && p[0].Length > 0 && p[1].Length > 0)
            .Select(p => p[0] + ":" + p[1])
            .Distinct()
            .Take(20)
            .ToList();
        if (pairs.Count == 0) return query.Where(_ => false);
        return query.Where(a => a.EntityType != null && a.EntityId != null && pairs.Contains(a.EntityType + ":" + a.EntityId));
    }
}
