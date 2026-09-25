using MailKit.Net.Smtp;
using MailKit.Security;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// A safe, pooled SMTP connection that can be reused across multiple sends.
///
/// <para>
/// MailKit's SmtpClient is not thread-safe, so each pool slot is used by exactly one goroutine
/// at a time (guarded by the pool's SemaphoreSlim). Connections are validated before reuse
/// and recreated on failure.
/// </para>
/// </summary>
public interface ISmtpConnectionPool : IAsyncDisposable
{
    /// <summary>
    /// Rents a connected, authenticated SMTP client. The caller must return it via
    /// <see cref="ReturnAsync"/> when done, even on failure.
    /// </summary>
    Task<PooledSmtpClient> RentAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a client to the pool. Pass <paramref name="healthy"/> = false to signal that
    /// the connection should be discarded rather than reused.
    /// </summary>
    Task ReturnAsync(PooledSmtpClient client, bool healthy = true);
}

/// <summary>
/// A rented SMTP client together with the pool slot it occupies.
/// </summary>
public sealed class PooledSmtpClient(SmtpClient inner, int slot)
{
    public SmtpClient Inner { get; } = inner;
    internal int Slot { get; } = slot;
}

/// <summary>
/// Pool key — identifies a distinct SMTP server configuration.
/// Two connections to the same host with the same credentials share a pool.
/// </summary>
public sealed record SmtpPoolKey(
    string Host,
    int Port,
    string Username,
    string PasswordHash,   // SHA256 of the decrypted password — for equality, never logged
    SecureSocketOptions Security);
