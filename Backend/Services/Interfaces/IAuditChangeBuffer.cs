namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>One field's before/after values.</summary>
public sealed class AuditFieldChange
{
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

/// <summary>Everything that happened to one entity in a single save.</summary>
public sealed class AuditEntityChange
{
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    /// <summary>Display label resolved by convention (Name → Title → Email → Phone).</summary>
    public string? EntityName { get; set; }
    /// <summary>Created, Updated or Deleted.</summary>
    public string Action { get; set; } = string.Empty;
    public List<AuditFieldChange> Changes { get; set; } = new();
}

/// <summary>
/// Per-request holding area for field-level changes observed by <c>AppDbContext.SaveChangesAsync</c>,
/// drained by <c>AuditService</c> when it writes the matching audit entry.
///
/// <para>
/// This exists so the 64 existing <c>LogAsync</c> call sites get before/after data without being
/// edited: they already call <c>LogAsync</c> straight after <c>SaveChangesAsync</c>, so whatever
/// is in the buffer at that moment is that operation's own work.
/// </para>
/// <para>
/// Scoped to the request. Anything never drained — a chat message insert, say, which writes no
/// audit entry — is simply discarded when the scope ends, and the buffer is capped so a bulk
/// import cannot grow it without bound.
/// </para>
/// </summary>
public interface IAuditChangeBuffer
{
    void Record(AuditEntityChange change);

    /// <summary>
    /// Takes the changes belonging to an audit entry and removes them from the buffer.
    ///
    /// Prefers an exact match on entity type (and id when supplied). Falls back to returning
    /// everything buffered when nothing matches, which covers the cases where the audit entry's
    /// label differs from the EF entity name — chat message deletion logs against the
    /// conversation while modifying the messages.
    /// </summary>
    IReadOnlyList<AuditEntityChange> Drain(string? entityType, string? entityId);
}
