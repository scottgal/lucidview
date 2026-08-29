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

    [Theory]
    [InlineData("https://example.com/\"--gpu-launcher=calc.exe", "%22--gpu-launcher=calc.exe")]
    [InlineData("https://example.com/ --flag=1", "%20--flag=1")]
    [InlineData("https://example.com/`whoami`|ls&calc", "%60whoami%60%7Cls&calc")]
    public void The_string_handed_to_the_opener_is_the_percent_encoded_absolute_uri_not_the_raw_string(
        string url, string expectedEncodedSuffix)
    {
        Assert.True(SafeLinkOpener.IsSafe(url));

        Assert.True(SafeLinkOpener.TryGetSafeUri(url, out var uri));
        Assert.NotNull(uri);
        Assert.EndsWith(expectedEncodedSuffix, uri!.AbsoluteUri, StringComparison.Ordinal);

        // The raw string still contains the dangerous literal character;
        // what matters is that TryOpen never hands this string to the
        // process launcher, only uri.AbsoluteUri does.
        Assert.NotEqual(uri.AbsoluteUri, url);
    }

    [Theory]
    [InlineData("https://accounts.google.com@evil.com/")]
    [InlineData("http://user:password@example.com/")]
    public void A_url_with_embedded_credentials_is_refused(string url)
    {
        Assert.False(SafeLinkOpener.IsSafe(url));
    }
}
