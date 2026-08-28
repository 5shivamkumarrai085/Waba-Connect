using System.Net;
using System.Net.Sockets;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Turns a connection's remote address into the address a human should see in the audit trail.
///
/// <para>
/// By the time this runs, <c>UseForwardedHeaders</c> has already replaced
/// <c>Connection.RemoteIpAddress</c> with the originating client's address — but only if the
/// immediate peer was a configured known proxy. That gate is the whole point: without it any
/// caller could put whatever they liked in <c>X-Forwarded-For</c> and choose the IP recorded
/// against their own actions, which in an audit trail is worse than recording the load balancer.
/// </para>
/// <para>
/// What is left to do here is presentation. Kestrel hands back IPv6-mapped IPv4 addresses
/// (<c>::ffff:203.0.113.5</c>) and the IPv6 loopback (<c>::1</c>), neither of which is what an
/// operator expects to read beside a user's name.
/// </para>
/// </summary>
public static class ClientIpResolver
{
    /// <summary>
    /// The client address for this request, or null when there is no connection at all — the
    /// campaign scheduler and the Meta webhook processor both run without an HttpContext.
    /// </summary>
    public static string? Resolve(HttpContext? context) => Format(context?.Connection?.RemoteIpAddress);

    /// <summary>
    /// Normalises one address: IPv4-mapped IPv6 collapses to dotted quad, and either family's
    /// loopback becomes <c>127.0.0.1</c>.
    ///
    /// Loopback is normalised rather than left as <c>::1</c> because "::1" is a correct answer that
    /// reads as a bug — it was the reported problem — and because a browser reaching a local server
    /// over IPv6 or IPv4 is the same visit either way.
    /// </summary>
    public static string? Format(IPAddress? address)
    {
        if (address is null) return null;

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return IPAddress.Loopback.ToString();
        }

        // A link-local IPv6 address carries a zone suffix (fe80::1%17) that identifies the
        // server's own interface, not the client. It is noise in a stored record.
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
        {
            address = new IPAddress(address.GetAddressBytes());
        }

        return address.ToString();
    }
}
