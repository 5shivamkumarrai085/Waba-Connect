using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Data;

/// <summary>
/// Turns EF's change tracker into the before/after data the audit trail shows.
///
/// <para>
/// Runs from <c>AppDbContext.SaveChangesAsync</c> so every audited write is covered without a
/// line of code in any service. Nothing here may throw: a dropped audit diff is a nuisance, a
/// failed customer operation because the audit capture threw is an outage.
/// </para>
/// </summary>
internal static class AuditChangeCapture
{
    /// <summary>
    /// Tables excluded from capture.
    ///
    /// The two log tables would recurse — writing an audit row is itself a tracked insert. The
    /// rest are high-churn runtime state (inbound/outbound message rows, bot conversation
    /// position, health pings) whose inserts would swamp the buffer on every request while
    /// telling an auditor nothing.
    ///
    /// <para>
    /// <see cref="ChatMessage"/> was deliberately left in for a while, because its IsDeleted
    /// false→true diffs were the only record of which message a delete removed. It is excluded
    /// now that both delete paths write a proper snapshot to <c>AuditLog.MetadataJson</c> — the
    /// diffs said nothing the snapshot does not say better, and every ordinary send was spending
    /// buffer slots (capped at 50) to record a message nobody audits.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ExcludedEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(AuditLog),
        nameof(LoginAttempt),
        nameof(MessageActivityLog),
        nameof(HealthLog),
        nameof(ConversationState),
        nameof(ChatMessage)
    };

    /// <summary>
    /// Properties that change on virtually every save and carry no audit meaning. Recording them
    /// would bury the one field the user actually edited under bookkeeping noise.
    /// </summary>
    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreatedAt", "UpdatedAt"
    };

    /// <summary>Longest stored value. Enough for a chat message, short of a base64 blob.</summary>
    private const int MaxValueLength = 500;

    /// <summary>Per-entity field cap, so a wide table cannot produce an unbounded diff.</summary>
    private const int MaxFieldsPerEntity = 40;

    /// <summary>
    /// Snapshots the pending changes. Called before <c>base.SaveChangesAsync</c>, because that is
    /// the only moment <c>OriginalValues</c> still holds the "before" state and deleted rows are
    /// still tracked.
    /// </summary>
    internal static List<(EntityEntry Entry, AuditEntityChange Change)> Capture(ChangeTracker changeTracker)
    {
        var captured = new List<(EntityEntry, AuditEntityChange)>();

        foreach (var entry in changeTracker.Entries())
        {
            if (entry.Entity is null) continue;

            var entityType = entry.Entity.GetType().Name;
            if (ExcludedEntities.Contains(entityType)) continue;

            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => null
            };

            if (action is null) continue;

            var change = new AuditEntityChange
            {
                EntityType = entityType,
                Action = action,
                EntityId = ReadKey(entry),
                EntityName = ResolveDisplayName(entry),
                Changes = ReadFieldChanges(entry)
            };

            captured.Add((entry, change));
        }

        return captured;
    }

    /// <summary>
    /// Publishes the snapshot after the save succeeded.
    ///
    /// Split from <see cref="Capture"/> because an inserted row has no primary key until the
    /// database assigns one — capturing before the save would record every new entity as id 0.
    /// </summary>
    internal static void Publish(
        List<(EntityEntry Entry, AuditEntityChange Change)> captured,
        IAuditChangeBuffer buffer)
    {
        foreach (var (entry, change) in captured)
        {
            if (string.IsNullOrWhiteSpace(change.EntityId))
            {
                // Deleted entries are detached by now, so re-reading the key can fail; the
                // pre-save value already captured above stands in those cases.
                try { change.EntityId = ReadKey(entry); } catch { /* keep what we have */ }
            }

            buffer.Record(change);
        }
    }

    private static string? ReadKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;

        var values = new List<string>();
        foreach (var keyProperty in key.Properties)
        {
            var property = entry.Property(keyProperty.Name);

            // Before the insert lands, EF holds a placeholder key (a large negative number).
            // Recording it would put "-2147482647" in the audit trail where the row id belongs;
            // leaving it null lets Publish read the real key once the database has assigned one.
            if (property.IsTemporary) return null;

            var value = property.CurrentValue?.ToString();
            if (!string.IsNullOrWhiteSpace(value) && value != "0") values.Add(value);
        }

        return values.Count > 0 ? string.Join(":", values) : null;
    }

    /// <summary>
    /// Picks a human label for the entity by convention, so the audit trail reads
    /// "Contact — Mukul RMA" rather than "Contact 124". Order matters: most entities carry Name,
    /// templates and campaigns carry Title, users carry Email, contacts fall back to Phone.
    /// </summary>
    private static string? ResolveDisplayName(EntityEntry entry)
    {
        foreach (var candidate in new[] { "Name", "Title", "Email", "Phone", "FirstName" })
        {
            var property = entry.Metadata.FindProperty(candidate);
            if (property is null) continue;

            var value = entry.Property(candidate).CurrentValue?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return Truncate(value);
        }

        return null;
    }

    private static List<AuditFieldChange> ReadFieldChanges(EntityEntry entry)
    {
        var changes = new List<AuditFieldChange>();

        foreach (var property in entry.Properties)
        {
            if (changes.Count >= MaxFieldsPerEntity) break;

            var name = property.Metadata.Name;
            if (IgnoredProperties.Contains(name)) continue;
            // The primary key is already carried as EntityId; repeating it as a "changed field"
            // is noise, and on an insert it would show EF's placeholder value.
            if (property.Metadata.IsPrimaryKey()) continue;

            switch (entry.State)
            {
                case EntityState.Modified when property.IsModified:
                {
                    var oldValue = Format(property.OriginalValue, name);
                    var newValue = Format(property.CurrentValue, name);
                    // EF flags a property modified whenever it was assigned, even to the value it
                    // already held. Recording "Status: Active → Active" is noise.
                    if (oldValue == newValue) continue;
                    changes.Add(new AuditFieldChange { Field = name, OldValue = oldValue, NewValue = newValue });
                    break;
                }

                case EntityState.Added:
                {
                    var newValue = Format(property.CurrentValue, name);
                    if (string.IsNullOrEmpty(newValue)) continue;
                    changes.Add(new AuditFieldChange { Field = name, OldValue = null, NewValue = newValue });
                    break;
                }

                case EntityState.Deleted:
                {
                    // The "before" state is the whole point here — after the save this row is
                    // gone, so this is the only record of what it contained.
                    var oldValue = Format(property.OriginalValue, name);
                    if (string.IsNullOrEmpty(oldValue)) continue;
                    changes.Add(new AuditFieldChange { Field = name, OldValue = oldValue, NewValue = null });
                    break;
                }
            }
        }

        return changes;
    }

    private static string? Format(object? value, string fieldName)
    {
        if (PayloadRedactor.IsSensitiveFieldName(fieldName)) return PayloadRedactor.RedactedValue;
        if (value is null) return null;

        var text = value switch
        {
            DateTime dt => dt.ToString("u"),
            bool b => b ? "true" : "false",
            _ => value.ToString()
        };

        return Truncate(text);
    }

    private static string? Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= MaxValueLength ? text : text[..MaxValueLength] + "…";
    }
}
