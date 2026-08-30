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
        var host = NormaliseHost(uri.DnsSafeHost);
        if (host.Length == 0) return true;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // A name is left alone on purpose: see the class remarks.
        return IPAddress.TryParse(host, out var ip) && IsLocalOrPrivate(ip);
    }

    /// <summary>
    /// Drops the root label's trailing dot. "169.254.169.254." and
    /// "localhost." resolve to exactly the same place as the forms without
    /// it, but Uri.DnsSafeHost keeps the dot, so IPAddress.TryParse fails on
    /// the first and the literal "localhost" comparison misses the second.
    /// Both would then fall through to the "it is a name, allow it" path.
    /// Only one dot is stripped: a host ending in two dots is not a name any
    /// resolver accepts, so there is nothing to normalise it to.
    /// </summary>
    private static string NormaliseHost(string host) =>
        host.EndsWith('.') ? host[..^1] : host;

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
        {
            if (ip.IsIPv6LinkLocal        // fe80::/10
                || ip.IsIPv6SiteLocal     // fec0::/10, deprecated but still routed by some stacks
                || ip.IsIPv6UniqueLocal   // fc00::/7
                || IPAddress.IPv6Any.Equals(ip))
                return true;

            // An IPv6 address can carry an IPv4 one inside it, and the stack
            // that receives it ends up talking to that IPv4 address. The
            // v4-mapped shape (::ffff:127.0.0.1) is handled above by
            // MapToIPv4; NAT64 (64:ff9b::/96) and 6to4 (2002::/16) hide the
            // same thing in a form MapToIPv4 does not recognise, so
            // [64:ff9b::7f00:1] and [2002:7f00:1::] would otherwise be
            // spellings of loopback that this policy waved through.
            var embedded = EmbeddedIPv4(ip);
            return embedded is not null && IsLocalOrPrivate(embedded);
        }

        return false;
    }

    /// <summary>
    /// The IPv4 address a NAT64 or 6to4 address translates to, or null when
    /// the address is neither.
    /// </summary>
    private static IPAddress? EmbeddedIPv4(IPAddress ip)
    {
        var b = ip.GetAddressBytes();

        // 64:ff9b::/96, the well-known NAT64 prefix: the last four bytes are
        // the IPv4 address.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b
            && b.Skip(4).Take(8).All(x => x == 0))
            return new IPAddress(b[12..16]);

        // 2002::/16, 6to4: the IPv4 address is bytes 2 to 5.
        if (b[0] == 0x20 && b[1] == 0x02)
            return new IPAddress(b[2..6]);

        // ::a.b.c.d, the deprecated v4-compatible form. Deprecated is not the
        // same as unroutable, and it costs one comparison to refuse it.
        if (b.Take(12).All(x => x == 0))
            return new IPAddress(b[12..16]);

        return null;
    }
}
