// Shim: MarkdownViewer.LabProgram in the MarkdownViewer namespace so that
// the #if LAB blocks in lean source compile. CLI arg parsing moved to
// MarkdownViewer.Lab.LabProgram; values set there and read here.
namespace MarkdownViewer;

internal static class LabProgram
{
    /// <summary>URL to auto-load + screenshot (--shot URL). Null = normal startup.</summary>
    internal static string? AutoShotUrl { get; set; }

    /// <summary>Output path for the --shot screenshot.</summary>
    internal static string? AutoShotOutput { get; set; }

    /// <summary>Milliseconds to wait after page load before capturing (--wait N).</summary>
    internal static int AutoShotWaitMs { get; set; } = 30_000;
}
