using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WhatsAppCampaignApi.Services.Integrations;

/// <summary>The events a subscription can choose, grouped for the settings page.</summary>
public static class WebhookEventCatalog
{
    public sealed record EventType(string Key, string Group, string Description);

    public static readonly IReadOnlyList<EventType> All =
    [
        new("message.sent", "Messages", "A campaign message was accepted for delivery."),
        new("message.delivered", "Messages", "A campaign message reached the recipient."),
        new("message.read", "Messages", "A WhatsApp campaign message was read."),
        new("message.failed", "Messages", "A campaign message could not be sent."),
        new("email.opened", "Email", "A campaign email was opened."),
        new("email.clicked", "Email", "A link in a campaign email was clicked."),
        new("email.replied", "Email", "A recipient replied to a campaign email."),
        new("email.bounced", "Email", "A campaign email bounced."),
        new("email.unsubscribed", "Email", "A recipient unsubscribed."),
        new("email.complained", "Email", "A recipient marked a campaign email as spam."),
        new("campaign.status_changed", "Campaigns", "A campaign changed status (sending, sent, cancelled…)."),
        new("approval.decided", "Campaigns", "A campaign waiting for approval was approved or rejected."),
        new("consent.changed", "Contacts", "A contact opted in or out."),
        new("conversation.message_received", "Chat", "A customer sent a message."),
        new("conversation.assigned", "Chat", "A conversation was assigned or unassigned."),
        new("conversation.status_changed", "Chat", "A conversation was resolved, closed or reopened."),
        new("webhook.test", "Webhooks", "A test event, sent from the settings page.")
    ];

    private static readonly HashSet<string> Keys = All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>Checks a subscription's selection: known keys, "family.*" or "*".</summary>
    public static List<string> Normalize(IEnumerable<string>? selected)
    {
        var list = (selected ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (list.Count == 0) throw new ArgumentException("Choose at least one event.");
        foreach (var key in list)
        {
            var ok = key == "*" || Keys.Contains(key)
                || (key.EndsWith(".*", StringComparison.Ordinal) && All.Any(e => e.Key.StartsWith(key[..^1], StringComparison.Ordinal)));
            if (!ok) throw new ArgumentException($"\"{key}\" is not an event this app sends.");
        }
        return list.Contains("*") ? ["*"] : list;
    }

    /// <summary>Whether a stored selection covers an event type.</summary>
    public static bool Matches(string storedSelection, string eventType) =>
        storedSelection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(s =>
            s == "*" || s == eventType
            || (s.EndsWith(".*", StringComparison.Ordinal) && eventType.StartsWith(s[..^1], StringComparison.Ordinal)));
}

/// <summary>
/// Signatures in the Stripe style: <c>X-Waba-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c>, where the
/// hex is HMAC-SHA256(secret, "&lt;t&gt;.&lt;body&gt;"). The timestamp inside the signed text lets a
/// receiver refuse replays older than a few minutes.
/// </summary>
public static class WebhookSigner
{
    public const string SignatureHeader = "X-Waba-Signature";
    public const string EventIdHeader = "X-Waba-Event-Id";
    public const string EventTypeHeader = "X-Waba-Event-Type";

    public static string NewSecret() => "whsec_" + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Sign(string secret, long timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>What a receiver does. Used by the tests, and a reference for integrators.</summary>
    public static bool Verify(string secret, string header, string body, TimeSpan tolerance, DateTimeOffset now)
    {
        var parts = header.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (!parts.TryGetValue("t", out var t) || !long.TryParse(t, out var ts) || !parts.TryGetValue("v1", out var v1)) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(ts)).Duration() > tolerance) return false;
        var expected = Sign(secret, ts, body).Split("v1=")[1];
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(v1));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Keeps webhooks from being pointed at the server's own network (SSRF): https only (plain http
/// only when explicitly allowed outside Production), no credentials in the URL, and no private,
/// loopback, link-local or metadata addresses — checked when saved and again at connect time, so
/// a name re-pointed after saving (DNS rebinding) is still refused.
/// </summary>
public sealed class WebhookUrlGuard
{
    private readonly bool _allowHttp;
    private readonly bool _allowPrivateNetworks;

    public WebhookUrlGuard(bool allowHttp, bool allowPrivateNetworks)
    {
        _allowHttp = allowHttp;
        _allowPrivateNetworks = allowPrivateNetworks;
    }

    public static WebhookUrlGuard FromConfiguration(IConfiguration configuration, IHostEnvironment environment) => new(
        allowHttp: !environment.IsProduction() && configuration.GetValue("Webhooks:AllowHttp", false),
        allowPrivateNetworks: !environment.IsProduction() && configuration.GetValue("Webhooks:AllowPrivateNetworks", false));

    /// <summary>Throws ArgumentException with a reason the settings page can show.</summary>
    public async Task<Uri> ValidateAsync(string? url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("Enter the full URL, starting with https://.");
        if (uri.Scheme != Uri.UriSchemeHttps && !(_allowHttp && uri.Scheme == Uri.UriSchemeHttp))
            throw new ArgumentException("Webhook URLs must use https://.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Put credentials in a header or the path, not in the URL.");
        if (uri.AbsoluteUri.Length > 500)
            throw new ArgumentException("The URL can be at most 500 characters.");

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(uri.Host, ct);
        }
        catch (SocketException)
        {
            throw new ArgumentException($"\"{uri.Host}\" could not be found.");
        }
        if (addresses.Length == 0) throw new ArgumentException($"\"{uri.Host}\" could not be found.");
        if (!_allowPrivateNetworks && addresses.Any(IsBlocked))
            throw new ArgumentException("That address is on a private or internal network, which webhooks may not reach.");
        return uri;
    }

    /// <summary>For SocketsHttpHandler.ConnectCallback: resolves, refuses internal addresses, connects.</summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        var allowed = addresses.Where(a => _allowPrivateNetworks || !IsBlocked(a)).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException($"Refused to connect to {host}: it resolves to a private or internal address.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6 = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast
                || (v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x0d && v6[3] == 0xb8); // documentation range
        }

        var b = address.GetAddressBytes();
        return b[0] == 10
            || b[0] == 127
            || b[0] == 0
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)          // link-local, including cloud metadata 169.254.169.254
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // carrier-grade NAT
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)
            || b[0] >= 224;                           // multicast and reserved
    }
}
