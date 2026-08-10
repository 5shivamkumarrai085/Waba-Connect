using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class AuditService : IAuditService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext dbContext, ICurrentUserService currentUser, ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task LogAsync(
        string eventName,
        string category,
        string? description = null,
        string? entityType = null,
        string? entityId = null,
        int? actorUserId = null,
        string? actorUserName = null)
    {
        try
        {
            _dbContext.AuditLogs.Add(new AuditLog
            {
                Event = eventName,
                Category = category,
                Description = description,
                EntityType = entityType,
                EntityId = entityId,
                // Explicit actor wins (sign-in has no token yet); otherwise resolve from the
                // request. Both null for background callers — the scheduler and the webhook
                // have no HttpContext.
                UserId = actorUserId ?? _currentUser.UserId,
                UserName = actorUserName ?? _currentUser.UserName,
                IpAddress = _currentUser.IpAddress
            });

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Auditing must never break the operation it is recording. A dropped audit row is
            // bad; a failed user creation because the audit insert failed is worse.
            _logger.LogError(ex, "Failed to write audit log entry {Event}.", eventName);
        }
    }
}
