using System.IO;
using FluentAssertions;
using MarkdownViewer.Lab;
using Xunit;

namespace MarkdownViewer.Lab.Tests;

public class ProjectStructureTests
{
    [Fact]
    public void AppPaths_LocalState_ExistsAndUnderLucidLab()
    {
        AppPaths.LocalState.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(AppPaths.LocalState).Should().BeTrue();
        AppPaths.LocalState.Should().EndWith("lucidLAB",
            because: "lucidLAB must not collide with lean's settings folder");
    }

    [Fact]
    public void AppPaths_SubdirsCreated()
    {
        Directory.Exists(AppPaths.WorkspacesRoot).Should().BeTrue();
        Directory.Exists(AppPaths.ModelCacheDir).Should().BeTrue();
        Directory.Exists(AppPaths.TelemetryDir).Should().BeTrue();
    }
}
