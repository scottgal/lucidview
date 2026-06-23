using System.Reflection;

namespace MarkdownViewer.Services;

// UA format follows the bot convention (Googlebot-style "+URL") so site
// operators inspecting logs can identify the client and visit the project
// page rather than guessing. We also send Accept: text/markdown first so
// servers that produce markdown (mostlylucid.net, Cloudflare URL→markdown,
// Jina Reader, etc.) can short-circuit the HTML conversion path.
internal static class UserAgent
{
    public static readonly string Value = BuildValue();

    private static string BuildValue()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        return $"lucidVIEW/{version} (Markdown Browser; +https://www.mostlylucid.net/lucidview)";
    }
}
