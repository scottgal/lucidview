using System.IO;
using System.Threading.Tasks;
using Avalonia.Input;
using LucidReader.Models;
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The bundled user manual, checked at the two points a Window is not needed:
/// how its image references are rewritten, and how it is reached.
///
/// The rewrite is the part with a trap in it. The manual references its
/// screenshots relatively, LucidMarkdownView resolves a relative image against
/// its SourcePath, and mylo never sets SourcePath - so the pictures would not
/// draw. Setting SourcePath is the fix that must NOT be made, because
/// SourcePath also changes how relative LINKS resolve, and a relative href
/// that becomes a file:// URL handed to the platform opener is a bug this app
/// has already had once. Rewriting only image targets is what keeps the
/// pictures working without reopening that. The tests below are what say so:
/// images move, links do not.
///
/// That the images then actually paint is not something a unit test can see.
/// ux-scripts/run-user-manual.sh checks that, by counting colour in a snip of
/// the rendered pane.
/// </summary>
public class UserManualTests
{
    // Path.GetFullPath, not a literal: RewriteImagePaths returns
    // Path.GetFullPath(Path.Combine(dir, relative)), and on Windows that roots a
    // POSIX-looking "/opt/mylo/manual" onto the current drive and joins with
    // backslashes. A hardcoded "/opt/..." expectation passes on Unix and fails on
    // Windows, which is exactly how this file broke CI.
    private static readonly string Directory =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mylo-manual-fixture"));

    [Fact]
    public void RewritesARelativeImageToAnAbsolutePath()
    {
        var result = UserManual.RewriteImagePaths(
            "![The three panes](screenshots/01-three-pane.png)", Directory);

        var expected = Path.GetFullPath(
            Path.Combine(Directory, "screenshots", "01-three-pane.png"));

        Assert.Equal($"![The three panes]({expected})", result);
    }

    [Fact]
    public void LeavesRemoteImagesAlone()
    {
        const string markdown = "![Logo](https://example.com/logo.png)";

        Assert.Equal(markdown, UserManual.RewriteImagePaths(markdown, Directory));
    }

    [Theory]
    [InlineData("![Already absolute](/etc/passwd.png)")]
    [InlineData("![Explicit file url](file:///etc/passwd.png)")]
    [InlineData("![Data url](data:image/png;base64,iVBOR)")]
    public void LeavesAnythingThatIsNotARelativePathAlone(string markdown) =>
        Assert.Equal(markdown, UserManual.RewriteImagePaths(markdown, Directory));

    /// <summary>
    /// The one that matters most. A link is not an image, and the whole point
    /// of rewriting rather than setting SourcePath is that link resolution is
    /// left exactly as it was: a relative href stays relative, resolves to
    /// nothing, and cannot become a local file the platform is asked to open.
    /// </summary>
    [Fact]
    public void LeavesLinksAlone()
    {
        const string markdown =
            "[a relative link](screenshots/01-three-pane.png) and " +
            "[a local file](../../../etc/passwd) and " +
            "[a real one](https://example.com/)";

        Assert.Equal(markdown, UserManual.RewriteImagePaths(markdown, Directory));
    }

    [Fact]
    public void RewritesEveryImageInADocument()
    {
        var result = UserManual.RewriteImagePaths(
            "![one](screenshots/a.png)\n\ntext\n\n![two](screenshots/b.png)", Directory);

        Assert.Contains(Path.GetFullPath(Path.Combine(Directory, "screenshots", "a.png")), result);
        Assert.Contains(Path.GetFullPath(Path.Combine(Directory, "screenshots", "b.png")), result);
    }

    [Fact]
    public async Task ReportsAMissingManualRatherThanThrowing()
    {
        var empty = System.IO.Directory.CreateTempSubdirectory("mylo-manual-test");
        try
        {
            Assert.Null(UserManual.FindPath(empty.FullName));
            Assert.Null(await UserManual.TryLoadAsync(empty.FullName));
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FindsAndRewritesAManualBesideTheExecutable()
    {
        var root = System.IO.Directory.CreateTempSubdirectory("mylo-manual-test");
        try
        {
            var manual = Path.Combine(root.FullName, "manual");
            System.IO.Directory.CreateDirectory(Path.Combine(manual, "screenshots"));
            await File.WriteAllTextAsync(
                Path.Combine(manual, "user-manual.md"),
                "# mylo User Manual\n\n![Shell](screenshots/01-three-pane.png)\n");

            var loaded = await UserManual.TryLoadAsync(root.FullName);

            Assert.NotNull(loaded);
            Assert.Contains(
                Path.Combine(manual, "screenshots", "01-three-pane.png"),
                loaded);
            Assert.DoesNotContain("](screenshots/", loaded);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>
    /// F1 is the only gesture with no modifier that survives the text-entry
    /// guard. It has to: it produces no character, so it cannot be typed by
    /// accident, and help that stops working while the caret is in the search
    /// box is help nobody can reach at the moment they want it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void F1OpensTheManualWhereverFocusIs(bool focusIsTextEntry) =>
        Assert.Equal(
            ReaderShortcut.OpenUserManual,
            ReaderShortcuts.Resolve(
                Key.F1, KeyModifiers.None, focusIsTextEntry, KeyModifiers.Meta));

    [Fact]
    public void TheHelpMenuOpensTheManual()
    {
        var help = Assert.Single(ReaderMenu.Build(), section => section.Header == "Help");
        var item = Assert.Single(
            help.Items, i => i.Action == ReaderMenuAction.OpenUserManual);

        Assert.Equal("mylo User Manual", item.Header);
        Assert.Equal(ReaderMenuEnablement.Always, item.Enablement);

        // No accelerator drawn. A NativeMenuItem's gesture becomes an AppKit
        // key equivalent, matched before the focused control sees the key, and
        // F1 carries no command modifier - the same rule that keeps the bare
        // letters out of the menus.
        Assert.False(item.HasGesture);
    }

    [Fact]
    public void TheShortcutCardNamesF1()
    {
        Assert.Contains("F1", ReaderMenu.KeyboardShortcutSummary);
        Assert.Contains("manual", ReaderMenu.KeyboardShortcutSummary);
    }
}
