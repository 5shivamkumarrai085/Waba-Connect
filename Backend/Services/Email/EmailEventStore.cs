using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <inheritdoc />
public class EmailEventStore : IEmailEventStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<EmailEventStore> _logger;

    public EmailEventStore(AppDbContext db, ILogger<EmailEventStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string idempotencyKey, CancellationToken ct = default)
    {
        return await _db.EmailEvents
            .AsNoTracking()
            .AnyAsync(e => e.IdempotencyKey == idempotencyKey, ct);
    }

    /// <inheritdoc />
    public async Task<EmailEvent?> RecordAsync(
        EmailEventKind kind,
        string idempotencyKey,
        string source,
        int? campaignId,
        int? campaignContactId,
        string? messageId = null,
        string? providerMessageId = null,
        string? recipientAddress = null,
        DateTime? occurredAt = null,
        EmailBounceType? bounceType = null,
        string? bounceSubType = null,
        string? diagnosticCode = null,
        string? originalUrl = null,
        string? userAgent = null,
        string? ipAddress = null,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        // Fast idempotency check before acquiring a write lock.
        // The unique index is the authoritative guard; this is an optimisation to avoid
        // the INSERT attempt for the common case of a replayed event.
        if (await ExistsAsync(idempotencyKey, ct))
        {
            _logger.LogDebug(
                "Email event {Kind} with idempotency key {Key} already recorded; skipping.",
                kind, idempotencyKey);
            return null;
        }

        var ev = new EmailEvent
        {
            EventKind       = kind,
            IdempotencyKey  = idempotencyKey,
            Source          = source,
            CampaignId      = campaignId,
            CampaignContactId = campaignContactId,
            MessageId       = messageId,
            ProviderMessageId = providerMessageId,
            RecipientAddress = recipientAddress,
            OccurredAt      = occurredAt?.ToUniversalTime() ?? DateTime.UtcNow,
            BounceType      = bounceType,
            BounceSubType   = bounceSubType,
            DiagnosticCode  = diagnosticCode,
            OriginalUrl     = originalUrl,
            UserAgent       = userAgent,
            IpAddress       = ipAddress,
            MetadataJson    = metadataJson,
            CreatedAt       = DateTime.UtcNow
        };

        _db.EmailEvents.Add(ev);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost a race: another instance recorded the same event concurrently.
            // The unique index did its job; this is not an error.
            _db.ChangeTracker.Clear();
            _logger.LogDebug(
                "Email event {Kind} / {Key} was recorded concurrently by another instance.",
                kind, idempotencyKey);
            return null;
        }

        return ev;
    }

    /// <summary>
    /// Detects a PostgreSQL unique constraint violation (error code 23505).
    /// Checking by error code rather than message text is stable across PG versions.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Npgsql wraps the PostgresException in a DbUpdateException.
        // We check the class hierarchy rather than a direct type reference so the
        // code compiles without a direct Npgsql dependency in this file.
        var inner = ex.InnerException;
        return inner is not null &&
               inner.GetType().Name == "PostgresException" &&
               inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }
}
