using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Generates and validates opaque tracking tokens for open-pixel and click-tracking URLs.
/// </summary>
public interface IEmailTrackingService
{
    /// <summary>Generates a tracking ID for a new recipient. Should be stored in CampaignContact.TrackingId.</summary>
    string GenerateTrackingId();

    /// <summary>Builds a signed open-tracking URL for the given tracking ID.</summary>
    string BuildOpenUrl(string trackingId);

    /// <summary>Builds a signed click-tracking URL embedding the destination URL and a link index.</summary>
    string BuildClickUrl(string trackingId, string destinationUrl, int linkIndex = 0);

    /// <summary>
    /// Validates an open token. Returns the tracking ID on success, or null on failure.
    /// </summary>
    string? ValidateOpenToken(string token);

    /// <summary>
    /// Validates a click token. Returns (trackingId, destinationUrl) on success, or null on failure.
    /// </summary>
    (string TrackingId, string DestinationUrl)? ValidateClickToken(string token);
}

public class EmailTrackingService : IEmailTrackingService
{
    private readonly IOptionsMonitor<EmailOptions> _options;

    public EmailTrackingService(IOptionsMonitor<EmailOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public string GenerateTrackingId()
    {
        // 32 cryptographically random bytes = 256 bits of entropy
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    /// <inheritdoc />
    public string BuildOpenUrl(string trackingId)
    {
        var baseUrl = _options.CurrentValue.Tracking.BaseUrl.TrimEnd('/');
        var token   = SignOpen(trackingId);
        return $"{baseUrl}/api/t/o/{token}";
    }

    /// <inheritdoc />
    public string BuildClickUrl(string trackingId, string destinationUrl, int linkIndex = 0)
    {
        var baseUrl = _options.CurrentValue.Tracking.BaseUrl.TrimEnd('/');
        var token   = SignClick(trackingId, destinationUrl, linkIndex);
        return $"{baseUrl}/api/t/c/{token}";
    }

    /// <inheritdoc />
    public string? ValidateOpenToken(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            // 1. Base64Url format
            if (!token.Contains(':'))
            {
                try
                {
                    var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
                    var parts = raw.Split(':');
                    if (parts.Length == 2)
                    {
                        var trackingId = parts[0];
                        var sig        = parts[1];
                        var expected   = ComputeHmac($"open:{trackingId}");

                        if (CryptographicOperations.FixedTimeEquals(
                                Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected)))
                        {
                            return trackingId;
                        }
                    }
                }
                catch { }
            }

            // 2. Legacy unencoded format
            var unescaped = Uri.UnescapeDataString(token);
            var legacyParts = unescaped.Split(':');
            if (legacyParts.Length >= 2)
            {
                var trackingId = string.Join(":", legacyParts[..^1]);
                var sig        = legacyParts[^1];
                var expected   = ComputeHmac($"open:{trackingId}");

                if (CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected)))
                {
                    return trackingId;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public (string TrackingId, string DestinationUrl)? ValidateClickToken(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            // 1. Base64Url format
            if (!token.Contains(':') && !token.Contains('%'))
            {
                try
                {
                    var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
                    var parts = raw.Split('|');
                    if (parts.Length == 4)
                    {
                        var trackingId = parts[0];
                        var linkIndex  = parts[1];
                        var destUrl    = parts[2];
                        var sig        = parts[3];

                        var expected = ComputeHmac($"click:{trackingId}:{linkIndex}:{destUrl}");
                        if (CryptographicOperations.FixedTimeEquals(
                                Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected)))
                        {
                            return (trackingId, destUrl);
                        }
                    }
                }
                catch { }
            }

            // 2. Legacy format
            var decoded = Uri.UnescapeDataString(token);
            var legacyParts = decoded.Split(':');
            if (legacyParts.Length >= 4)
            {
                var trackingId = legacyParts[0];
                var linkIndex  = legacyParts[1];
                var sig        = legacyParts[^1];
                var encodedOrRawUrl = string.Join(":", legacyParts[2..^1]);
                var destUrl    = Uri.UnescapeDataString(encodedOrRawUrl);

                var expected1 = ComputeHmac($"click:{trackingId}:{linkIndex}:{encodedOrRawUrl}");
                var expected2 = ComputeHmac($"click:{trackingId}:{linkIndex}:{destUrl}");
                var expected3 = ComputeHmac($"click:{trackingId}:{linkIndex}:{Uri.EscapeDataString(destUrl)}");

                if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected1)) ||
                    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected2)) ||
                    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected3)))
                {
                    return (trackingId, destUrl);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string SignOpen(string trackingId)
    {
        var sig = ComputeHmac($"open:{trackingId}");
        var payload = $"{trackingId}:{sig}";
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
    }

    private string SignClick(string trackingId, string destinationUrl, int linkIndex)
    {
        var sig = ComputeHmac($"click:{trackingId}:{linkIndex}:{destinationUrl}");
        var payload = $"{trackingId}|{linkIndex}|{destinationUrl}|{sig}";
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
    }

    private string ComputeHmac(string data)
    {
        var secret = _options.CurrentValue.Tracking.SigningSecret ?? "change-me-in-production-tracking-secret";
        var key = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash);
    }
}
