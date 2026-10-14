using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services;

namespace WhatsAppCampaignApi.Data;

/// <summary>
/// Clears the <see cref="HotPathCache"/> regions whose data a save changed, so an admin's edit
/// (a new keyword, a reconnected number, a new corridor) takes effect at once on this instance,
/// whichever service or controller saved it. Other instances catch up when their entries expire.
/// </summary>
/// <remarks>
/// Invalidated both before and after the save: before, so nothing reads the old cache while the
/// save runs; after, so a read that slipped in during the save and cached the old rows is dropped.
/// Bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> statements do not pass through here; those are
/// only used on these tables for runtime state (sessions, statuses), not configuration.
/// </remarks>
public sealed class HotPathCacheInvalidator : SaveChangesInterceptor
{
    private readonly HotPathCache _cache;

    public HotPathCacheInvalidator(HotPathCache cache) => _cache = cache;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Invalidate(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Invalidate(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Invalidate(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Invalidate(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    private void Invalidate(DbContext? context)
    {
        if (context is null) return;

        foreach (var region in context.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                     .Select(e => RegionFor(e.Entity))
                     .Where(r => r is not null)
                     .Distinct())
        {
            _cache.Invalidate(region!);
        }
    }

    private static string? RegionFor(object entity) => entity switch
    {
        BotFlow or FlowNode or FlowEdge => HotPathCache.BotFlows,
        MessageBot or TemplateBot or TemplateBotVariable or Template => HotPathCache.Bots,
        WabaConfiguration or WabaPhoneNumber or Connection or Business => HotPathCache.Connections,
        RemittanceCorridor or RemittancePayoutMethod or RemittanceBeneficiaryField or RemittanceInstitution
            or RemittanceFeeRule or RemittanceLookup or RemittancePaymentMethod or WhatsAppFlowRegistration => HotPathCache.SendMoney,
        _ => null
    };
}
