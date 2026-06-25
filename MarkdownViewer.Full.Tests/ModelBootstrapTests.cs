using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class ModelBootstrapTests
{
    [Fact]
    public void Doctor_ReportsModelAndBrowserStatus()
    {
        var report = ModelBootstrap.Doctor();

        Assert.False(string.IsNullOrEmpty(report.ModelPath));
        Assert.False(string.IsNullOrEmpty(report.BrowsersPath));
        // Ready = ModelPresent && BrowsersPresent. Don't assert true; depends on host.
    }
}
