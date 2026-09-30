using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// One-time, idempotent upgrade of stored credentials at startup:
/// WABA access tokens and app secrets that were stored in plaintext are encrypted, and email
/// credentials still in the legacy AES-CBC format are re-encrypted as AES-GCM.
/// </summary>
/// <remarks>
/// Runs in the background after startup and touches only rows that still need it, so after the
/// first run it is a single cheap query. A failure is logged and retried on the next start; the
/// application keeps working meanwhile because readers accept both formats.
/// </remarks>
public sealed class CredentialEncryptionMigrator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CredentialEncryptionMigrator> _logger;

    public CredentialEncryptionMigrator(IServiceScopeFactory scopeFactory, ILogger<CredentialEncryptionMigrator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Raw query: through the model the converter would already present decrypted values,
            // hiding which rows are still plaintext at rest.
            var wabaIds = await db.Database
                .SqlQueryRaw<int>("""
                    SELECT "Id" AS "Value" FROM "WabaConfigurations"
                     WHERE ("AccessToken" <> '' AND "AccessToken" NOT LIKE 'enc:v2:%')
                        OR ("FacebookAppSecret" <> '' AND "FacebookAppSecret" NOT LIKE 'enc:v2:%')
                    """)
                .ToListAsync(stoppingToken);

            if (wabaIds.Count > 0)
            {
                var rows = await db.WabaConfigurations.Where(c => wabaIds.Contains(c.Id)).ToListAsync(stoppingToken);
                foreach (var row in rows)
                {
                    // Re-assigning is not a change to EF; marking the columns modified makes the save
                    // write them through the encrypting converter.
                    db.Entry(row).Property(r => r.AccessToken).IsModified = true;
                    db.Entry(row).Property(r => r.FacebookAppSecret).IsModified = true;
                }

                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Encrypted stored WhatsApp credentials on {Count} connection(s).", rows.Count);
            }

            var emailRows = await db.EmailConfigurations
                .Where(c => (c.SmtpPasswordEncrypted != null && c.SmtpPasswordEncrypted != "" && !c.SmtpPasswordEncrypted.StartsWith(SecretCipher.Prefix))
                         || (c.ImapPasswordEncrypted != null && c.ImapPasswordEncrypted != "" && !c.ImapPasswordEncrypted.StartsWith(SecretCipher.Prefix)))
                .ToListAsync(stoppingToken);

            foreach (var row in emailRows)
            {
                row.SmtpPasswordEncrypted = Upgrade(row.SmtpPasswordEncrypted, row.Id);
                row.ImapPasswordEncrypted = Upgrade(row.ImapPasswordEncrypted, row.Id);
            }

            if (emailRows.Count > 0)
            {
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Upgraded encryption of stored email credentials on {Count} connection(s).", emailRows.Count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Credential encryption upgrade failed; it will be retried on the next start.");
        }
    }

    private string? Upgrade(string? value, int configId)
    {
        if (string.IsNullOrEmpty(value) || SecretCipher.IsEncrypted(value)) return value;

        try
        {
            return SecretCipher.Encrypt(SecretCipher.Decrypt(value));
        }
        catch (Exception ex)
        {
            // Unreadable with the current key: leave it exactly as it is rather than destroy it.
            _logger.LogWarning(ex, "A stored credential on email configuration {ConfigId} could not be upgraded.", configId);
            return value;
        }
    }
}
