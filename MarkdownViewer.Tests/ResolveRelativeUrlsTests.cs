using System;
using AngleSharp.Html.Parser;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

/// <summary>
/// Making a downloaded article's own links work.
///
/// The conversion pipeline is handed the page's address and AngleSharp
/// resolves against it correctly - but only through the typed Href and Source
/// properties. The markdown renderer reads the raw attribute, which still held
/// whatever the publisher wrote, so a link to "/posts/two" reached the reader
/// as "/posts/two". SafeLinkOpener requires an absolute http or https URL,
/// correctly, so clicking one of those did nothing whatsoever.
/// </summary>
public class ResolveRelativeUrlsTests
{
    private static readonly Uri Page = new("https://example.com/posts/one");

    private static string HrefAfterResolving(string html, string selector = "a")
    {
        var doc = new HtmlParser().ParseDocument(html);
        HtmlPreProcessor.ResolveRelativeUrls(doc, Page);
        return doc.QuerySelector(selector)!.GetAttribute(selector == "a" ? "href" : "src")!;
    }

    [Fact]
    public void A_root_relative_link_resolves_against_the_host()
    {
        // The regression that prompted all of this. On Unix,
        // Uri.TryCreate("/root/page.html", UriKind.Absolute, out _) SUCCEEDS,
        // parsing it as a file:// path, so an "is it already absolute" guard
        // written that way skips exactly the links a site uses for its own
        // pages. It has to be a scheme test, not a parse.
        Assert.Equal(
            "https://example.com/root/page.html",
            HrefAfterResolving("""<a href="/root/page.html">x</a>"""));
    }

    [Fact]
    public void A_document_relative_link_resolves_against_the_page()
    {
        Assert.Equal(
            "https://example.com/posts/doc/page.html",
            HrefAfterResolving("""<a href="doc/page.html">x</a>"""));
    }

    [Fact]
    public void A_relative_image_resolves_too()
    {
        Assert.Equal(
            "https://example.com/img/pic.png",
            HrefAfterResolving("""<img src="/img/pic.png" alt="p">""", "img"));
    }

    [Theory]
    // Already addresses something in its own right.
    [InlineData("https://other.example/page")]
    [InlineData("http://other.example/page")]
    // Left for the link gate to refuse, rather than rewritten into something
    // that looks like a page on this site.
    [InlineData("mailto:someone@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    public void An_address_that_carries_a_scheme_is_left_alone(string href)
    {
        Assert.Equal(href, HrefAfterResolving($"""<a href="{href}">x</a>"""));
    }

    [Fact]
    public void A_bare_fragment_stays_a_fragment()
    {
        // It addresses this document. Rewriting it would turn an in-page jump
        // into a navigation away from the article being read.
        Assert.Equal("#section", HrefAfterResolving("""<a href="#section">x</a>"""));
    }

    [Fact]
    public void A_path_with_a_colon_in_it_is_not_mistaken_for_a_scheme()
    {
        // The scheme pattern is anchored for this reason: a colon later in a
        // path is ordinary, and treating it as a scheme would leave the link
        // relative and broken.
        Assert.Equal(
            "https://example.com/posts/notes/2026:review.html",
            HrefAfterResolving("""<a href="notes/2026:review.html">x</a>"""));
    }

    [Fact]
    public void Without_a_base_nothing_is_rewritten()
    {
        var doc = new HtmlParser().ParseDocument("""<a href="/root/page.html">x</a>""");
        HtmlPreProcessor.ResolveRelativeUrls(doc, null);

        Assert.Equal("/root/page.html", doc.QuerySelector("a")!.GetAttribute("href"));
    }
}
