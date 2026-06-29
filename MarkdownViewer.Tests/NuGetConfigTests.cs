using System.IO;
using System.Xml.Linq;
using Xunit;

namespace MarkdownViewer.Tests;

public class NuGetConfigTests
{
    [Fact]
    public void NuGetConfig_DeclaresGitHubPackagesSourceForLucidSupport()
    {
        var configPath = Path.Combine(Path.GetDirectoryName(typeof(NuGetConfigTests).Assembly.Location)!,
                                       "..", "..", "..", "..", "NuGet.Config");
        Assert.True(File.Exists(configPath), $"NuGet.Config not found at {configPath}");

        var doc = XDocument.Load(configPath);
        var sources = doc.Descendants("packageSources").Elements("add");

        Assert.Contains(sources, s => s.Attribute("value")?.Value == "https://api.nuget.org/v3/index.json");
        Assert.Contains(sources, s => s.Attribute("value")?.Value == "https://nuget.pkg.github.com/scottgal/index.json");
    }

    [Fact]
    public void NuGetConfig_HasNoLocalFileFeeds()
    {
        var configPath = Path.Combine(Path.GetDirectoryName(typeof(NuGetConfigTests).Assembly.Location)!,
                                       "..", "..", "..", "..", "NuGet.Config");
        var doc = XDocument.Load(configPath);
        var sources = doc.Descendants("packageSources").Elements("add");

        foreach (var s in sources)
        {
            var v = s.Attribute("value")?.Value ?? "";
            Assert.False(v.StartsWith("/tmp/"), $"Local file feed not allowed: {v}");
            Assert.False(v.Contains("local-feed"), $"Local file feed not allowed: {v}");
        }
    }
}