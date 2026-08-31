using System.Text.RegularExpressions;

namespace LucidReader.Services;

/// <summary>
/// Finds the bundled user manual and prepares it for the reading pane.
///
/// The manual is an ordinary markdown file with ordinary relative image
/// references (<c>![Shell](screenshots/01-three-pane.png)</c>), shipped beside
/// the executable as <c>manual/user-manual.md</c> together with
/// <c>manual/screenshots/*.png</c>. mylo is a reader, so it reads it the same
/// way it reads an article: the text goes into the same
/// <c>LucidMarkdownView</c> everything else does.
///
/// That leaves one problem, which <see cref="RewriteImagePaths"/> exists to
/// solve without creating a worse one.
///
/// <c>LucidMarkdownView.OnAttachedToVisualTree</c> sets its renderer's
/// <c>ImageBasePath</c> from its own <c>SourcePath</c> property, falling back
/// to the temporary directory, and mylo never sets <c>SourcePath</c>. So a
/// relative image reference resolves against the temporary directory and
/// nothing is drawn. The obvious fix - set <c>SourcePath</c> to the manual's
/// folder - also changes how relative <em>links</em> resolve, and that is
/// exactly the shape of the bug this app already had once: a relative href
/// became a <c>file://</c> URL and was handed to the platform's "open this"
/// mechanism, which opened a local file in another application.
/// <c>SafeLinkOpener</c> refuses anything that is not http or https and
/// <c>MainWindow.OnArticleLinkClicked</c> marks every link handled so nothing
/// downstream gets a second go, but the safest change is the one that does not
/// touch link resolution at all.
///
/// So <c>SourcePath</c> is left alone and the manual's image references are
/// rewritten to absolute paths before the text reaches the view. An absolute
/// path is returned unchanged by the <c>Path.Combine</c> the renderer does
/// against its base path, so the image resolves whatever the base path happens
/// to be, and relative links keep resolving exactly as they did before: to
/// nothing.
/// </summary>
public static partial class UserManual
{
    /// <summary>What the reading pane's headline says while the manual is up.</summary>
    public const string Title = "mylo User Manual";

    public const string NotFoundMessage =
        "The user manual is not installed beside this build of mylo.";

    /// <summary>
    /// Said on the status line while the manual is showing, because the
    /// reading pane is otherwise the one place that always holds an article
    /// and the way back is not obvious from looking at it.
    /// </summary>
    public const string ShowingMessage =
        "Showing the user manual. Pick an article in the list to go back to reading.";

    private const string FileName = "user-manual.md";

    /// <summary>
    /// Markdown image references whose target is relative. The target is
    /// captured up to the closing bracket, and anything carrying a scheme
    /// (<c>https:</c>, <c>file:</c>, <c>data:</c>) or a leading slash is
    /// excluded by the negative lookahead rather than by rewriting and then
    /// undoing it.
    /// </summary>
    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?!\w+:|/)(?<path>[^)\s]+)\)")]
    private static partial Regex RelativeImage();

    /// <summary>
    /// Where the manual lives for this build, or null when it was not shipped.
    ///
    /// Beside the executable first, which is where the csproj copies it and
    /// therefore where a Release build and the macOS .app bundle have it. Then
    /// the source tree, found by walking up from the output directory, so that
    /// <c>dotnet run</c> and the UI harness still see a manual if the copy
    /// step is ever broken. The walk is bounded rather than a fixed number of
    /// <c>..</c> segments because the output path gains and loses a
    /// runtime-identifier level depending on how the build was invoked. Same
    /// two-step lookup lucidVIEW's OpenUserManual does, for the same reason.
    /// </summary>
    public static string? FindPath(string baseDirectory)
    {
        var bundled = Path.Combine(baseDirectory, "manual", FileName);
        if (File.Exists(bundled)) return bundled;

        var directory = new DirectoryInfo(baseDirectory);
        for (var level = 0; level < 6 && directory is not null; level++, directory = directory.Parent)
        {
            var source = Path.Combine(directory.FullName, "Assets", "manual", FileName);
            if (File.Exists(source)) return source;
        }

        return null;
    }

    /// <summary>
    /// Rewrites every relative markdown image target to an absolute path under
    /// <paramref name="manualDirectory"/>. Link targets are deliberately left
    /// untouched: see the class remarks.
    /// </summary>
    public static string RewriteImagePaths(string markdown, string manualDirectory) =>
        RelativeImage().Replace(markdown, match =>
        {
            var relative = match.Groups["path"].Value;
            var absolute = Path.GetFullPath(Path.Combine(manualDirectory, relative));
            return $"![{match.Groups["alt"].Value}]({absolute})";
        });

    /// <summary>
    /// Reads the manual and returns it ready to render, or null when it is not
    /// installed. Any read failure is left to the caller to report.
    /// </summary>
    public static async Task<string?> TryLoadAsync(string baseDirectory)
    {
        if (FindPath(baseDirectory) is not { } path) return null;

        var markdown = await File.ReadAllTextAsync(path);
        return RewriteImagePaths(markdown, Path.GetDirectoryName(path)!);
    }
}
