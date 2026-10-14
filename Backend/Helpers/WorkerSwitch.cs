namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Registers a background worker unless configuration switches it off:
/// <c>Workers:{TypeName}:Enabled = false</c> (e.g. <c>Workers__WhatsAppWebhookWorker__Enabled=false</c>).
/// Lets one deployment run some instances as API-only and others as workers, and lets a developer
/// connect to a shared database without competing for its jobs.
/// </summary>
public static class WorkerSwitch
{
    public static IServiceCollection AddWorker<TWorker>(this IServiceCollection services, IConfiguration configuration)
        where TWorker : class, IHostedService
    {
        var name = typeof(TWorker).Name;
        if (configuration.GetValue($"Workers:{name}:Enabled", true) && configuration.GetValue("Workers:Enabled", true))
        {
            services.AddHostedService<TWorker>();
        }
        return services;
    }
}
