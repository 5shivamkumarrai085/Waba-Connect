using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Data;

/// <summary>
/// One definition of what makes a WhatsApp configuration usable.
///
/// <para>
/// "Connected" alone is not it. <see cref="WabaConfiguration.Connected"/> lives on the
/// configuration, but the connection that owns it can be soft-deleted independently
/// (<see cref="Connection.IsActive"/>), and a configuration belonging to a deleted connection is
/// not a live integration — it is a leftover.
/// </para>
/// <para>
/// That distinction was being spelled out, or forgotten, at each call site. It was forgotten in
/// the two uniqueness guards on the connect wizard, and the result was that a deleted connection
/// went on reserving its Facebook App ID and its WABA ID forever: the wizard refused them and told
/// the operator to "disconnect it first", naming a connection that no longer appears anywhere in
/// the product. There was no way out from the UI.
/// </para>
/// <para>
/// Repeating a predicate is how the next one gets forgotten too, so it lives here now. A caller
/// that genuinely wants configurations regardless of their connection's state can still say so
/// explicitly — but it has to say so.
/// </para>
/// </summary>
public static class WabaConfigurationQueries
{
    /// <summary>
    /// Configurations that are connected <em>and</em> whose connection still exists.
    ///
    /// This is what "connected" means everywhere a caller is asking "is this integration live" —
    /// duplicate checks, health checks, broadcast targets, dashboard lookups.
    /// </summary>
    public static IQueryable<WabaConfiguration> ForLiveConnections(
        this IQueryable<WabaConfiguration> configurations) =>
        configurations.Where(c => c.Connected && c.Connection != null && c.Connection.IsActive);
}
