using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

public class SafeLinkOpenerTests
{
    [Theory]
    [InlineData("https://example.com/article")]
    [InlineData("http://example.com/article")]
    [InlineData("HTTPS://EXAMPLE.COM/SHOUTING")]
    [InlineData("https://example.com/path?a=1&b=2#frag")]
    public void Http_and_https_are_allowed(string url)
    {
        Assert.True(SafeLinkOpener.IsSafe(url));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("file://C:/Windows/System32/calc.exe")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ms-msdt:/id")]
    [InlineData("smb://attacker.example/share")]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("about:blank")]
    [InlineData("chrome://settings")]
    public void Every_other_scheme_is_refused(string url)
    {
        Assert.False(SafeLinkOpener.IsSafe(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url at all")]
    [InlineData("/relative/path")]
    [InlineData("//protocol-relative.example/x")]
    public void Anything_that_is_not_an_absolute_http_url_is_refused(string? url)
    {
        Assert.False(SafeLinkOpener.IsSafe(url));
    }

    [Fact]
    public void A_refused_url_reports_a_reason_and_does_not_open()
    {
        var opened = SafeLinkOpener.TryOpen("javascript:alert(1)", out var reason);

        Assert.False(opened);
        Assert.NotNull(reason);
        Assert.Contains("javascript", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_url_with_an_embedded_newline_is_refused()
    {
        Assert.False(SafeLinkOpener.IsSafe("https://example.com/\njavascript:alert(1)"));
    }
}
