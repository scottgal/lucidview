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
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        // Control characters can be used to smuggle a second target past a
        // naive check or a shell.
        if (url.Any(char.IsControl)) return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public static bool TryOpen(string? url, out string? refusalReason)
    {
        if (!IsSafe(url))
        {
            refusalReason = $"Refused to open a link that is not a web address: {Describe(url)}";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!.Trim()) { UseShellExecute = true });
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
