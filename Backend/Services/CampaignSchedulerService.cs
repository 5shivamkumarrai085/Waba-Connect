using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Safety net for scheduled WhatsApp campaigns. Scheduled sends are queued with their start time
/// at creation, so this only re-queues (idempotently) anything that predates that.
/// </summary>
public class CampaignSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CampaignSchedulerService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(1);

    public CampaignSchedulerService(IServiceProvider serviceProvider, ILogger<CampaignSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Campaign Scheduler Service started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var campaignService = scope.ServiceProvider.GetRequiredService<ICampaignService>();

                    await campaignService.ProcessScheduledCampaignsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error occurred executing scheduled campaigns.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown: the delay was cancelled.
        }
    }
}
