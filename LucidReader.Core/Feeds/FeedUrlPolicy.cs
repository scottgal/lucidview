using System.Net;
using System.Net.Sockets;

namespace LucidReader.Core.Feeds;

/// <summary>
/// The gate every feed URL that came from a file rather than from the user's
/// own typing has to pass before it is written to the database.
///
/// An OPML file is attacker-supplied data: it arrives by mail, by chat, by
/// download, and nothing about opening one implies the user vouched for the
/// addresses inside it. Every imported feed is fetched, unattended, by the
/// refresh scheduler, so an unchecked xmlUrl turns "import my subscriptions"
/// into an outbound GET against any address the file names. The cloud metadata
/// endpoint (169.254.169.254) and anything bound to loopback or to the local
/// network are the interesting targets, none of which a real subscription
/// list has any reason to contain.
///
/// Scheme handling is an allowlist of http and https, matching the policy
/// stated in SafeLinkOpener rather than inventing a second one: the set of
/// dangerous schemes is open-ended, so enumerating the bad ones is guaranteed
/// to miss some.
///
/// What this deliberately does not do is resolve host names. A name that
/// resolves to a private address still passes, because resolving here would
/// mean a DNS lookup per outline during import and the answer could change
/// before the fetch anyway. This blocks the literal-address shapes, which is
/// what an OPML file has to use to name an internal service it cannot get a
/// public name for.
/// </summary>
public static class FeedUrlPolicy
{
    public static bool IsAllowed(string? url) => TryValidate(url, out _, out _);

    /// <summary>
    /// Returns the parsed URI when the address is one this app is willing to
    /// fetch, or false plus a reason short enough to show in a status line.
    /// </summary>
    public static bool TryValidate(string? url, out Uri? uri, out string? reason)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            reason = "the address is empty";
            return false;
        }

        var trimmed = url.Trim();

        // Control characters can be used to smuggle a second target past a
        // check that only looks at the visible part of the string.
        if (trimmed.Any(char.IsControl))
        {
            reason = "the address contains control characters";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            reason = "the address is not a complete URL";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            reason = $"only http and https feeds can be read, not {parsed.Scheme}";
            return false;
        }

        // userinfo@host hides the host the request actually goes to, and the
        // credentials would then be stored in feeds.feed_url and replayed on
        // every scheduler tick.
        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            reason = "the address carries an embedded username or password";
            return false;
        }

        if (IsLocalOrPrivateHost(parsed))
        {
            reason = "the address points at a loopback, link-local or private network host";
            return false;
        }

        uri = parsed;
        reason = null;
        return true;
    }

    private static bool IsLocalOrPrivateHost(Uri uri)
    {
        var host = uri.DnsSafeHost;
        if (host.Length == 0) return true;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // A name is left alone on purpose: see the class remarks.
        return IPAddress.TryParse(host, out var ip) && IsLocalOrPrivate(ip);
    }

    private static bool IsLocalOrPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();

            // 0.0.0.0/8 (this network), 127.0.0.0/8 (loopback, already caught
            // above for the usual spellings), 10/8, 172.16/12, 192.168/16
            // (RFC1918) and 169.254/16 (link-local, which is where the cloud
            // metadata endpoint lives).
            return b[0] is 0 or 10 or 127
                   || (b[0] == 172 && b[1] is >= 16 and <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal        // fe80::/10
                   || ip.IsIPv6SiteLocal     // fec0::/10, deprecated but still routed by some stacks
                   || ip.IsIPv6UniqueLocal   // fc00::/7
                   || IPAddress.IPv6Any.Equals(ip);

        return false;
    }
}
