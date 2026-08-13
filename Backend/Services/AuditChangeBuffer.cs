using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public sealed class AuditChangeBuffer : IAuditChangeBuffer
{
    /// <summary>
    /// Upper bound on buffered entities. A CSV import saves hundreds of contacts in one call and
    /// writes at most one audit entry for the batch; without a cap the buffer would hold every
    /// row's diff for the life of the request. Beyond this the extra entities are dropped — the
    /// audit entry still records the operation, just not a diff per row.
    /// </summary>
    private const int MaxBufferedEntities = 50;

    private readonly List<AuditEntityChange> _changes = new();

    public void Record(AuditEntityChange change)
    {
        if (_changes.Count >= MaxBufferedEntities) return;
        _changes.Add(change);
    }

    public IReadOnlyList<AuditEntityChange> Drain(string? entityType, string? entityId)
    {
        if (_changes.Count == 0) return Array.Empty<AuditEntityChange>();

        List<AuditEntityChange> matched;

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            matched = _changes
                .Where(c => string.Equals(c.EntityType, entityType, StringComparison.OrdinalIgnoreCase)
                            && (string.IsNullOrWhiteSpace(entityId)
                                || string.Equals(c.EntityId, entityId, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matched.Count > 0)
            {
                foreach (var change in matched) _changes.Remove(change);
                return matched;
            }
        }

        // No exact match. The audit entry names something the change tracker didn't see under
        // that name, so hand back the whole unit of work rather than reporting no changes at all.
        matched = new List<AuditEntityChange>(_changes);
        _changes.Clear();
        return matched;
    }
}
