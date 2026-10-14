using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// "Only one instance does this": a Postgres advisory lock taken for the length of one run of a
/// background sweep. Queue workers do not need it (claims are already exclusive), but sweeps that
/// read a set of rows and then message customers do — with two instances, two auto-close sweeps
/// each sent the closing message.
/// </summary>
/// <remarks>
/// Transaction-scoped (<c>pg_try_advisory_xact_lock</c>) rather than session-scoped, so it behaves
/// behind a transaction-mode connection pooler such as Neon's: the lock lives exactly as long as
/// the transaction this helper holds open around the work, and is released however the run ends.
/// </remarks>
public static class LeaderLock
{
    /// <summary>Runs <paramref name="work"/> if no other instance is running the same named job. Returns false when skipped.</summary>
    public static async Task<bool> TryRunAsync(NpgsqlDataSource dataSource, string name, Func<Task> work, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", connection, transaction))
        {
            command.Parameters.AddWithValue("key", KeyFor(name));
            if (await command.ExecuteScalarAsync(ct) is not true) return false;
        }

        try
        {
            await work();
        }
        finally
        {
            // Ends the transaction and with it the lock; nothing was written on this connection.
            try { await transaction.RollbackAsync(CancellationToken.None); } catch (Exception) { /* connection already gone: lock released with it */ }
        }
        return true;
    }

    /// <summary>A stable 64-bit key per job name (the same on every instance and build).</summary>
    private static long KeyFor(string name) =>
        BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("WabaConnect.Leader:" + name)), 0);
}
