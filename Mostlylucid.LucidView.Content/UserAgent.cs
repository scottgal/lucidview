using System.Reflection;

namespace MarkdownViewer.Services;

// UA format follows the bot convention (Googlebot-style "+URL") so site
// operators inspecting logs can identify the client and visit the project
// page rather than guessing. We also send Accept: text/markdown first so
// servers that produce markdown (mostlylucid.net, Cloudflare URL→markdown,
// Jina Reader, etc.) can short-circuit the HTML conversion path.
//
// Uses GetEntryAssembly (not GetExecutingAssembly) so the reported version is
// always the hosting app's (lucidVIEW or lucidVIEW.Full), not this shared
// library's own assembly version, now that the code lives in a separate
// assembly from the app.
public static class UserAgent
{
    public static readonly string Value = BuildValue();

    private static string BuildValue()
    {
        var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetName().Version?.ToString(3) ?? "0.0.0";
        return $"lucidVIEW/{version} (Markdown Browser; +https://www.mostlylucid.net/lucidview)";
    }
}
