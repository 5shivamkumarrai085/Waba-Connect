using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Issues and validates one-click unsubscribe tokens.
/// </summary>
public interface IUnsubscribeTokenService
{
    /// <summary>Builds the absolute URL that goes in the List-Unsubscribe header.</summary>
    string BuildUnsubscribeUrl(string emailAddress, int? campaignId, int? contactId);

    /// <summary>
    /// Validates a token and returns what it authorises. Returns null for anything tampered with,
    /// expired or malformed — the caller reports one generic failure either way, so nothing about
    /// which check failed leaks back.
    /// </summary>
    UnsubscribePayload? Validate(string token);
}

/// <param name="Email">The address to suppress.</param>
/// <param name="CampaignId">Which campaign the link came from, for attribution.</param>
/// <param name="ContactId">The contact, when known.</param>
public sealed record UnsubscribePayload(string Email, int? CampaignId, int? ContactId);

/// <summary>
/// Stateless, HMAC-signed unsubscribe tokens.
///
/// <para>
/// Signed rather than stored. A database-backed token would mean a row per recipient per
/// campaign — millions of rows whose only purpose is to be looked up once — and a sequential id
/// in the URL, which is trivially enumerable: anyone could walk the range and unsubscribe every
/// recipient. A signed token carries its own claims, cannot be forged without the key, and needs
/// no storage at all.
/// </para>
/// <para>
/// The key comes from the environment (<c>Email__Unsubscribe__SigningKey</c>) and startup refuses
/// to proceed without it when the channel is enabled. That is deliberate: unsubscribe is a legal
/// requirement for bulk mail, and a deployment that cannot generate working opt-out links should
/// not be sending.
/// </para>
/// </summary>
public class UnsubscribeTokenService : IUnsubscribeTokenService
{
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<UnsubscribeTokenService> _logger;

    public UnsubscribeTokenService(IOptionsMonitor<EmailOptions> options, ILogger<UnsubscribeTokenService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string BuildUnsubscribeUrl(string emailAddress, int? campaignId, int? contactId)
    {
        var options = _options.CurrentValue.Unsubscribe;

        var baseUrl = (options.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            baseUrl.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase) ||
            baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            baseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://waba-connect-api-wnir.onrender.com";
        }

        var token = Issue(emailAddress, campaignId, contactId, TimeSpan.FromDays(options.TokenTtlDays));
        return $"{baseUrl}/api/public/email/unsubscribe?t={token}";
    }

    public UnsubscribePayload? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            var parts = token.Split('.', 2);
            if (parts.Length != 2) return null;

            var payloadBytes = FromBase64Url(parts[0]);
            var providedSignature = FromBase64Url(parts[1]);

            var expectedSignature = Sign(payloadBytes);

            // Fixed-time comparison. A short-circuiting comparison leaks, byte by byte, how much
            // of a guessed signature was right — which is enough to forge one given enough
            // attempts.
            if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature)) return null;

            var claims = JsonSerializer.Deserialize<TokenClaims>(payloadBytes);
            if (claims is null || string.IsNullOrWhiteSpace(claims.Email)) return null;

            if (DateTimeOffset.FromUnixTimeSeconds(claims.Exp) < DateTimeOffset.UtcNow)
            {
                _logger.LogInformation("Rejected an expired unsubscribe token for {Email}.", claims.Email);
                return null;
            }

            return new UnsubscribePayload(claims.Email, claims.CampaignId, claims.ContactId);
        }
        catch (Exception ex)
        {
            // Malformed input, not a fault. Logged at debug because an anonymous endpoint will
            // receive garbage from scanners as a matter of course.
            _logger.LogDebug(ex, "Rejected a malformed unsubscribe token.");
            return null;
        }
    }

    private string Issue(string emailAddress, int? campaignId, int? contactId, TimeSpan ttl)
    {
        var claims = new TokenClaims
        {
            Email = emailAddress.Trim().ToLowerInvariant(),
            CampaignId = campaignId,
            ContactId = contactId,
            Exp = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds()
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(claims);
        return $"{ToBase64Url(payload)}.{ToBase64Url(Sign(payload))}";
    }

    private byte[] Sign(byte[] payload)
    {
        var key = _options.CurrentValue.Unsubscribe.SigningKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            // Never falls back to a default key. A predictable signing key means anyone can mint
            // a token for any address, and a startup failure is far better than silently issuing
            // forgeable opt-out links.
            throw new InvalidOperationException(
                "Email:Unsubscribe:SigningKey is not configured. Set it via the "
              + "Email__Unsubscribe__SigningKey environment variable.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(payload);
    }

    // Base64url, because a standard Base64 token carrying '+', '/' or '=' breaks when a mail
    // client re-encodes the URL it is embedded in.
    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }

    private sealed class TokenClaims
    {
        public string Email { get; set; } = string.Empty;
        public int? CampaignId { get; set; }
        public int? ContactId { get; set; }
        public long Exp { get; set; }
    }
}
