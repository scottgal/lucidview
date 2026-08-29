using System.Diagnostics;

namespace LucidReader.Services;

/// <summary>
/// The only sanctioned way to open a URL that came from feed content.
///
/// Every URL the reading pane shows was written by a remote publisher, so it
/// is attacker-controlled. Handing an arbitrary scheme to the platform's
/// "open this" mechanism is a real exploit path: javascript: and data: can run
/// script in some hosts, file: can read the disk, and several platform-specific
/// schemes launch handlers with arguments.
///
/// This is an allowlist of http and https, deliberately not a blocklist. The
/// set of dangerous schemes is open-ended and platform-specific, so enumerating
/// the bad ones is guaranteed to miss some.
/// </summary>
public static class SafeLinkOpener
{
    public static bool IsSafe(string? url) => TryGetSafeUri(url, out _);

    /// <summary>
    /// The real gate. Returns the parsed <see cref="Uri"/> rather than the
    /// original string, because the original string is exactly what must
    /// never reach a process launcher: <see cref="Uri"/> normalises and
    /// percent-encodes characters (quotes, spaces, backticks, pipes,
    /// ampersands) that are perfectly legal inside a path or query segment
    /// but that a shell or an OS URL handler's argument parsing can treat
    /// as a delimiter. None of those characters are caught by a control
    /// character check, so the fix is to never hand the raw string to
    /// anything that launches a process; always hand out the parsed URI's
    /// <see cref="Uri.AbsoluteUri"/> instead.
    /// </summary>
    public static bool TryGetSafeUri(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;

        // Control characters can be used to smuggle a second target past a
        // naive check or a shell.
        if (url.Any(char.IsControl)) return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;

        // userinfo@host is a classic phishing shape: the visible host in the
        // status bar or hover preview is the trusted one, but navigation
        // lands on whatever host follows the @. A feed-supplied link
        // legitimately needing embedded credentials is not a real case.
        if (!string.IsNullOrEmpty(parsed.UserInfo)) return false;

        uri = parsed;
        return true;
    }

    public static bool TryOpen(string? url, out string? refusalReason)
    {
        if (!TryGetSafeUri(url, out var uri))
        {
            refusalReason = $"Refused to open a link that is not a web address: {Describe(url)}";
            return false;
        }

        try
        {
            // uri.AbsoluteUri, not the raw string: this is what actually
            // reaches Process.Start / the OS URL handler under
            // UseShellExecute, and it is percent-encoded, so a character an
            // OS handler's argument parsing could treat as a delimiter
            // (quote, space, backtick, pipe, ampersand) cannot break out.
            Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true });
            refusalReason = null;
            return true;
        }
        catch (Exception ex)
        {
            refusalReason = "Could not open the link: " + ex.Message;
            return false;
        }
    }

    private static string Describe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(empty)";
        var trimmed = url.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80] + "...";
    }
}
