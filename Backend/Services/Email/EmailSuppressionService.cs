using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// The list of addresses we must not send to.
/// </summary>
public interface IEmailSuppressionService
{
    /// <summary>
    /// Filters a set of addresses down to those that are suppressed. Set-based rather than
    /// per-address because expansion checks hundreds at a time, and a query per recipient would
    /// dominate the cost of expanding a campaign.
    /// </summary>
    Task<HashSet<string>> FilterSuppressedAsync(
        IEnumerable<string> emailAddresses,
        int? connectionId,
        CancellationToken ct = default);

    Task<bool> IsSuppressedAsync(string emailAddress, int? connectionId, CancellationToken ct = default);

    /// <summary>
    /// Adds an address, or refreshes the reason on one already listed. Idempotent, because
    /// bounce and complaint notifications are redelivered.
    /// </summary>
    Task SuppressAsync(
        string emailAddress,
        SuppressionReason reason,
        string? source = null,
        string? detail = null,
        int? connectionId = null,
        string? createdBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// Removes an address. A deliberately audited act: taking someone off the list means mailing
    /// a person who asked not to be mailed, or whose address hard-bounced.
    /// </summary>
    Task<bool> UnsuppressAsync(string emailAddress, int? connectionId, string? removedBy = null, CancellationToken ct = default);
}

/// <inheritdoc />
public class EmailSuppressionService : IEmailSuppressionService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ILogger<EmailSuppressionService> _logger;

    public EmailSuppressionService(
        AppDbContext dbContext,
        IAuditService auditService,
        ILogger<EmailSuppressionService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<HashSet<string>> FilterSuppressedAsync(
        IEnumerable<string> emailAddresses,
        int? connectionId,
        CancellationToken ct = default)
    {
        var normalized = emailAddresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(Normalize)
            .Distinct()
            .ToList();

        if (normalized.Count == 0) return [];

        var now = DateTime.UtcNow;

        var suppressed = await _dbContext.EmailSuppressions
            .AsNoTracking()
            .Where(s => normalized.Contains(s.EmailAddressNormalized)
                        // Global rows apply everywhere; connection-scoped rows only to their own
                        // connection. A hard bounce against one sending identity does not always
                        // mean the address is dead for another.
                     && (s.ConnectionId == null || s.ConnectionId == connectionId)
                        // Soft suppressions lapse. A permanent one has no expiry at all.
                     && (s.ExpiresAt == null || s.ExpiresAt > now))
            .Select(s => s.EmailAddressNormalized)
            .ToListAsync(ct);

        return suppressed.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> IsSuppressedAsync(string emailAddress, int? connectionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress)) return false;

        var normalized = Normalize(emailAddress);
        var now = DateTime.UtcNow;

        return await _dbContext.EmailSuppressions
            .AsNoTracking()
            .AnyAsync(s => s.EmailAddressNormalized == normalized
                        && (s.ConnectionId == null || s.ConnectionId == connectionId)
                        && (s.ExpiresAt == null || s.ExpiresAt > now), ct);
    }

    public async Task SuppressAsync(
        string emailAddress,
        SuppressionReason reason,
        string? source = null,
        string? detail = null,
        int? connectionId = null,
        string? createdBy = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress)) return;

        var normalized = Normalize(emailAddress);
        var scope = connectionId is null ? SuppressionScope.Global : SuppressionScope.Connection;

        var existing = await _dbContext.EmailSuppressions
            .FirstOrDefaultAsync(s => s.EmailAddressNormalized == normalized
                                   && s.ConnectionId == connectionId, ct);

        if (existing is not null)
        {
            // Already listed. The reason is refreshed rather than duplicated — an address that
            // first unsubscribed and has since hard-bounced is more usefully described by the
            // bounce, and a redelivered notification must not create a second row.
            existing.Reason = reason;
            existing.Source = Truncate(source, 200) ?? existing.Source;
            existing.Detail = Truncate(detail, 2000) ?? existing.Detail;
            existing.SuppressedAt = DateTime.UtcNow;

            // Any lapse date is cleared: a fresh suppression should not inherit an expiry set
            // when the address was only softly suppressed.
            existing.ExpiresAt = null;

            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        _dbContext.EmailSuppressions.Add(new EmailSuppression
        {
            EmailAddressNormalized = normalized,
            Scope = scope,
            ConnectionId = connectionId,
            Reason = reason,
            Source = Truncate(source, 200),
            Detail = Truncate(detail, 2000),
            SuppressedAt = DateTime.UtcNow,
            CreatedBy = Truncate(createdBy, 100)
        });

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Lost a race with a concurrent notification for the same address. The unique index
            // did its job, and the outcome is what was wanted either way, so this is not an
            // error worth propagating to a webhook handler.
            _dbContext.ChangeTracker.Clear();
            _logger.LogDebug(ex, "Address {Address} was suppressed concurrently; ignoring the duplicate.", normalized);
            return;
        }

        _logger.LogInformation(
            "Suppressed {Address} ({Reason}) from {Source}.", normalized, reason, source ?? "unspecified");
    }

    public async Task<bool> UnsuppressAsync(
        string emailAddress,
        int? connectionId,
        string? removedBy = null,
        CancellationToken ct = default)
    {
        var normalized = Normalize(emailAddress);

        var rows = await _dbContext.EmailSuppressions
            .Where(s => s.EmailAddressNormalized == normalized && s.ConnectionId == connectionId)
            .ToListAsync(ct);

        if (rows.Count == 0) return false;

        var reasons = string.Join(", ", rows.Select(r => r.Reason.ToString()).Distinct());

        _dbContext.EmailSuppressions.RemoveRange(rows);
        await _dbContext.SaveChangesAsync(ct);

        // Audited deliberately. Un-suppressing an address that complained is the kind of action
        // that needs a name attached to it afterwards.
        await _auditService.LogAsync(
            "EmailSuppression.Removed", "Settings",
            $"Removed {normalized} from the suppression list (was suppressed for: {reasons}).",
            nameof(EmailSuppression), normalized,
            actorUserName: removedBy);

        return true;
    }

    /// <summary>
    /// Lower-cased and trimmed. Every comparison runs on this form, so casing can never let a
    /// suppressed address slip through.
    /// </summary>
    private static string Normalize(string emailAddress) => emailAddress.Trim().ToLowerInvariant();

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= maxLength ? value.Trim()
        : value.Trim()[..maxLength];
}
