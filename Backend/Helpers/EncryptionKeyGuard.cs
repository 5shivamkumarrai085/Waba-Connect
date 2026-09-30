using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Refuses to start when <c>Encryption:Key</c> cannot read the credentials already stored in the
/// database — the key belongs to a different installation, or was replaced.
/// </summary>
/// <remarks>
/// Without this a process with the wrong key starts normally and fails later, one piece of work at
/// a time: every email send dead-letters with "could not be decrypted", every WhatsApp call is
/// refused. That happened on 2026-09-28 — a backend run with a stand-in key dead-lettered a
/// campaign email although the stored password was intact. Checking a sample at boot turns that
/// into one clear error before any work is taken.
/// </remarks>
public static class EncryptionKeyGuard
{
    /// <summary>
    /// True when there is nothing to check, or at least one sampled value decrypts. One readable
    /// value proves the key; a single corrupt row must not stop the whole application.
    /// </summary>
    public static bool KeyMatches(IReadOnlyCollection<string> samples, Func<string, string> decrypt)
    {
        if (samples.Count == 0) return true;
        foreach (var sample in samples)
        {
            try
            {
                decrypt(sample);
                return true;
            }
            catch (Exception)
            {
                // Try the next one.
            }
        }
        return false;
    }

    /// <summary>One stored credential, identified without its value.</summary>
    public sealed record StoredCredential(string Table, int RowId, string Column, string Value);

    /// <summary>
    /// Reads every stored credential (connections are per tenant, so this is thousands of rows at
    /// most) and throws when none of them decrypt. Individual unreadable values are logged by row
    /// so they can be re-entered; they do not stop the application, and the connection page flags
    /// them as needing attention. Also warns when another build of the app is polling this
    /// database's job queue.
    /// </summary>
    public static async Task VerifyAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(EncryptionKeyGuard));

        List<StoredCredential> stored;
        try
        {
            stored = await ReadAllAsync(db, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unreachable database is the health check's concern, not a reason to refuse to
            // start: only a proven mismatch stops the boot.
            logger.LogWarning(ex, "Could not check Encryption:Key against stored credentials; continuing.");
            return;
        }

        var unreadable = stored.Where(c => !CanDecrypt(c.Value, SecretCipher.Decrypt)).ToList();
        if (stored.Count > 0 && unreadable.Count == stored.Count)
        {
            throw new InvalidOperationException(
                $"Encryption:Key does not match this database: none of {stored.Count} stored credential(s) could be decrypted with it. " +
                "The process has a different key from the one the credentials were saved with — for example a value typed into this terminal, " +
                "or another installation's settings. Start it with the original key (user-secrets or Backend/appsettings.secrets.local.json). " +
                "Do not re-enter credentials under a new key unless the original key is truly lost: that makes the others unreadable.");
        }

        foreach (var c in unreadable)
        {
            logger.LogError(
                "Stored credential {Table}.{Column} of row {RowId} cannot be decrypted with the current key; re-enter it on its connection.",
                c.Table, c.Column, c.RowId);
        }

        var foreign = await Services.Queue.QueueHealthCheck.CountForeignQueueClientsAsync(db, ct);
        if (foreign > 0)
        {
            logger.LogWarning(
                "{Count} database session(s) from another build of the app are polling the job queue of this database. "
                + "They cannot take this build's jobs (queue contract {Version}), but they still run their own work; stop or update them.",
                foreign, Services.Queue.QueueNames.ContractVersion);
        }
    }

    public static bool CanDecrypt(string value, Func<string, string> decrypt)
    {
        try
        {
            decrypt(value);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Raw values: through the model the WABA columns would already be run through the decrypting
    // converter.
    private static Task<List<StoredCredential>> ReadAllAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<StoredCredential>("""
            SELECT "Table", "RowId", "Column", "Value" FROM (
                SELECT 'EmailConfigurations' AS "Table", "Id" AS "RowId", 'SmtpPasswordEncrypted' AS "Column", "SmtpPasswordEncrypted" AS "Value" FROM "EmailConfigurations"
                UNION ALL SELECT 'EmailConfigurations', "Id", 'ImapPasswordEncrypted', "ImapPasswordEncrypted" FROM "EmailConfigurations"
                UNION ALL SELECT 'WabaConfigurations', "Id", 'AccessToken', "AccessToken" FROM "WabaConfigurations"
                UNION ALL SELECT 'WabaConfigurations', "Id", 'FacebookAppSecret', "FacebookAppSecret" FROM "WabaConfigurations"
            ) s
            WHERE "Value" LIKE 'enc:v2:%'
            """).ToListAsync(ct);
}
