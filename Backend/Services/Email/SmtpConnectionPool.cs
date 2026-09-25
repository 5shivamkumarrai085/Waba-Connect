using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Per-key SMTP connection pool, safe for concurrent use by multiple dispatch workers.
///
/// <para>
/// Architecture:
/// - One pool instance per (host, port, username, security) combination
/// - Each pool has a configurable maximum connection count (default: WorkerCount)
/// - Connections are validated with a NOOP before reuse; broken ones are replaced
/// - Idle connections are closed after IdleTimeoutSeconds (default 60s)
/// - The pool is thread-safe via per-slot semaphores (no global lock, no convoy)
/// </para>
///
/// <para>
/// Why per-key rather than per-connection: the dispatch worker resolves a connection's SMTP
/// config and passes it here. Two workers sending for the same connection share the pool.
/// </para>
/// </summary>
public sealed class SmtpConnectionPoolManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<SmtpPoolKey, SmtpConnectionPool> _pools = new();
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<SmtpConnectionPoolManager> _logger;

    public SmtpConnectionPoolManager(
        IOptionsMonitor<EmailOptions> options,
        ILogger<SmtpConnectionPoolManager> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Gets or creates the pool for the given key.
    /// </summary>
    public SmtpConnectionPool GetPool(SmtpPoolKey key)
    {
        return _pools.GetOrAdd(key, k => new SmtpConnectionPool(
            k,
            maxSize: Math.Max(1, _options.CurrentValue.Dispatch.WorkerCount),
            connectTimeoutMs: _options.CurrentValue.Dispatch.SmtpTimeoutSeconds * 1000,
            idleTimeoutSeconds: 60,
            _logger));
    }

    /// <summary>Builds the pool key for a given SMTP configuration.</summary>
    public static SmtpPoolKey BuildKey(
        string host, int port, string username, string password, SecureSocketOptions security)
    {
        // Hash the password so the key can be compared without storing plaintext
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)))[..16];
        return new SmtpPoolKey(host, port, username, hash, security);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pool in _pools.Values)
            await pool.DisposeAsync();
        _pools.Clear();
    }
}

/// <summary>
/// A fixed-size pool of reusable MailKit SMTP connections for one server/credential pair.
/// </summary>
public sealed class SmtpConnectionPool : ISmtpConnectionPool, IAsyncDisposable
{
    private readonly SmtpPoolKey _key;
    private readonly int _maxSize;
    private readonly int _connectTimeoutMs;
    private readonly int _idleTimeoutSeconds;
    private readonly ILogger _logger;

    // Each slot is independent: a SemaphoreSlim(1,1) guards ownership, and the stored
    // client (or null for "not yet created") is accessed only while the slot is held.
    private readonly SemaphoreSlim[] _slotLocks;
    private readonly SmtpClient?[] _clients;
    private readonly DateTime[] _lastUsed;

    private volatile bool _disposed;

    public SmtpConnectionPool(
        SmtpPoolKey key,
        int maxSize,
        int connectTimeoutMs,
        int idleTimeoutSeconds,
        ILogger logger)
    {
        _key = key;
        _maxSize = maxSize;
        _connectTimeoutMs = connectTimeoutMs;
        _idleTimeoutSeconds = idleTimeoutSeconds;
        _logger = logger;

        _slotLocks = Enumerable.Range(0, maxSize)
            .Select(_ => new SemaphoreSlim(1, 1))
            .ToArray();
        _clients  = new SmtpClient?[maxSize];
        _lastUsed = new DateTime[maxSize];
    }

    /// <inheritdoc />
    public async Task<PooledSmtpClient> RentAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Try slots in order; the first available (not currently held by another worker) wins.
        // This is a fair, contention-free algorithm for small pool sizes (2–8).
        for (var attempt = 0; attempt < _maxSize * 2; attempt++)
        {
            var slot = attempt % _maxSize;

            // Non-blocking try first
            if (!await _slotLocks[slot].WaitAsync(0, ct))
                continue;

            try
            {
                var client = await EnsureConnectedAsync(slot, ct);
                // Slot lock remains held until ReturnAsync
                return new PooledSmtpClient(client, slot);
            }
            catch
            {
                _slotLocks[slot].Release();
                throw;
            }
        }

        // All slots busy — wait on slot 0 (simple, avoids starvation for small pools)
        await _slotLocks[0].WaitAsync(ct);
        try
        {
            var client = await EnsureConnectedAsync(0, ct);
            return new PooledSmtpClient(client, 0);
        }
        catch
        {
            _slotLocks[0].Release();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ReturnAsync(PooledSmtpClient pooled, bool healthy = true)
    {
        if (!healthy)
        {
            // Discard the broken connection; next rent will create a fresh one
            await DisposeClientAsync(_clients[pooled.Slot]);
            _clients[pooled.Slot] = null;
        }
        else
        {
            _lastUsed[pooled.Slot] = DateTime.UtcNow;
        }

        _slotLocks[pooled.Slot].Release();
    }

    /// <summary>
    /// Returns the existing client for this slot if it is still healthy, or creates a new one.
    /// Called while the slot lock is held.
    /// </summary>
    private async Task<SmtpClient> EnsureConnectedAsync(int slot, CancellationToken ct)
    {
        var existing = _clients[slot];

        // Check idle timeout
        if (existing is not null && _lastUsed[slot] != default)
        {
            var idle = (DateTime.UtcNow - _lastUsed[slot]).TotalSeconds;
            if (idle > _idleTimeoutSeconds)
            {
                _logger.LogDebug("SMTP pool slot {Slot} idle for {Idle:F0}s — reconnecting.", slot, idle);
                await DisposeClientAsync(existing);
                existing = null;
                _clients[slot] = null;
            }
        }

        // Validate existing connection with NOOP
        if (existing is not null && existing.IsConnected && existing.IsAuthenticated)
        {
            try
            {
                await existing.NoOpAsync(ct);
                return existing;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SMTP pool slot {Slot} NOOP failed — reconnecting.", slot);
                await DisposeClientAsync(existing);
                _clients[slot] = null;
            }
        }
        else if (existing is not null)
        {
            await DisposeClientAsync(existing);
            _clients[slot] = null;
        }

        // Create a fresh connection
        var client = new SmtpClient();
        using var timeout = new CancellationTokenSource(_connectTimeoutMs);
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        await client.ConnectAsync(_key.Host, _key.Port, _key.Security, linked.Token);

        // Authenticate using the stored key. NOTE: the key only holds a hash for equality;
        // the caller (SmtpEmailProvider) passes the actual credential to EnsureConnectedWithCredentialAsync.
        // This internal path is only called from within the pool, so this is safe.
        // The pool gets the real password via a separate auth callback — see AuthCallback.
        if (_authCallback is not null)
        {
            await _authCallback(client, linked.Token);
        }

        _clients[slot] = client;
        _lastUsed[slot] = DateTime.UtcNow;

        _logger.LogDebug("SMTP pool slot {Slot} connected to {Host}:{Port}.", slot, _key.Host, _key.Port);

        return client;
    }

    // Auth callback set by SmtpEmailProvider when it first establishes the pool.
    internal Func<SmtpClient, CancellationToken, Task>? _authCallback;

    private static async Task DisposeClientAsync(SmtpClient? client)
    {
        if (client is null) return;
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync(quit: true, CancellationToken.None);
        }
        catch { /* best-effort */ }
        finally
        {
            client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        for (var i = 0; i < _maxSize; i++)
        {
            await DisposeClientAsync(_clients[i]);
            _clients[i] = null;
        }
    }
}
