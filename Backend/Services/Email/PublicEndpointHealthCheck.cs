using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Degraded while the public address tracking and unsubscribe links are built on cannot be
/// reached by recipients — every open, click and unsubscribe is then lost silently. Degraded,
/// not Unhealthy: the API itself still works, so the node must stay in the load balancer.
/// </summary>
public sealed class PublicEndpointHealthCheck : IHealthCheck
{
    private readonly IPublicEndpointProbe _probe;

    public PublicEndpointHealthCheck(IPublicEndpointProbe probe) => _probe = probe;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _probe.CheckAsync(cancellationToken);
        var data = new Dictionary<string, object> { ["baseUrl"] = status.BaseUrl, ["reason"] = status.Reason };
        return status.Reachable
            ? HealthCheckResult.Healthy($"Tracking address {status.BaseUrl} is reachable.", data)
            : HealthCheckResult.Degraded($"Opens, clicks and unsubscribes will not be counted: {status.Description}.", data: data);
    }
}
