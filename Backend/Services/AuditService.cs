using System.Text.Json;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class AuditService : IAuditService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditChangeBuffer _changeBuffer;
    private readonly ILogger<AuditService> _logger;

    private static readonly JsonSerializerOptions ChangesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AuditService(
        AppDbContext dbContext,
        ICurrentUserService currentUser,
        IAuditChangeBuffer changeBuffer,
        ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _changeBuffer = changeBuffer;
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
            // Module and Action are split out of the event name rather than passed separately, so
            // the 64 existing call sites need no edit and a new one cannot forget them. The
            // convention has always been "Entity.Verb" — "Contact.Updated", "SystemLog.Cleared".
            var separator = eventName.IndexOf('.');
            var module = separator > 0 ? eventName[..separator] : eventName;
            var action = separator > 0 && separator < eventName.Length - 1
                ? eventName[(separator + 1)..]
                : string.Empty;

            var changes = _changeBuffer.Drain(entityType, entityId);

            _dbContext.AuditLogs.Add(new AuditLog
            {
                Event = eventName,
                Category = category,
                Module = Trim(module, 100),
                Action = Trim(action, 100),
                Status = IsFailureAction(action) ? "Failed" : "Success",
                Description = description,
                EntityType = entityType,
                EntityId = entityId,
                EntityName = Trim(ResolveEntityName(changes), 200),
                ChangesJson = SerializeChanges(changes),
                // Explicit actor wins (sign-in has no token yet); otherwise resolve from the
                // request. Both null for background callers — the scheduler and the webhook
                // have no HttpContext.
                UserId = actorUserId ?? _currentUser.UserId,
                UserName = actorUserName ?? _currentUser.UserName,
                IpAddress = _currentUser.IpAddress,
                UserAgent = Trim(_currentUser.UserAgent, 500)
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

    /// <summary>
    /// Verbs that mean the action did not happen.
    ///
    /// Suffix-matched rather than listed exhaustively, so a new failure event — "Export.Denied",
    /// "Sync.Errored" — is classified without editing this. Everything else is a completed
    /// action and reads as Success.
    /// </summary>
    private static readonly string[] FailureVerbSuffixes = { "Failed", "Denied", "Rejected", "Errored" };

    private static bool IsFailureAction(string action) =>
        FailureVerbSuffixes.Any(suffix => action.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Names the entity the entry is about, so the log reads "Mukul RMA" rather than "124".
    /// Prefers a change set that actually resolved a label; several rows may be touched by one
    /// operation and only some carry a display name.
    /// </summary>
    private static string? ResolveEntityName(IReadOnlyList<AuditEntityChange> changes) =>
        changes.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.EntityName))?.EntityName;

    private static string? SerializeChanges(IReadOnlyList<AuditEntityChange> changes)
    {
        if (changes.Count == 0) return null;

        // Entities whose fields all turned out to be unchanged or ignored carry no information;
        // storing them would make the details panel show empty rows.
        var meaningful = changes.Where(c => c.Changes.Count > 0).ToList();
        if (meaningful.Count == 0) return null;

        try
        {
            return JsonSerializer.Serialize(meaningful, ChangesJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
