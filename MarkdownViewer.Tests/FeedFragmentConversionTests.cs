using System;
using MarkdownViewer.Services;
using Xunit;
using Xunit.Abstractions;

namespace MarkdownViewer.Tests;

/// <summary>
/// Converting a FEED FRAGMENT, which is a different problem from converting an
/// article page and is the shape mylo's reading pane hands this service for
/// every item that has not been downloaded.
///
/// A fragment is what a publisher puts in an RSS description or an Atom
/// content element: a few paragraphs, sometimes a single anchor wrapping a
/// single image, with no html, body, article or main element around it. The
/// existing tests in this project record that StyloExtract returns empty
/// markdown for HTML with no recognisable content region, so whether it copes
/// with a bare fragment is exactly the question that has to be answered before
/// the reading pane depends on it.
/// </summary>
public class FeedFragmentConversionTests(ITestOutputHelper output)
{
    /// <summary>
    /// The real APOD item shape, taken from https://apod.nasa.gov/apod.rss.
    /// Its entire content is one anchor wrapping one image, which is why that
    /// feed rendered as an empty article: the pane was handed this HTML and
    /// passed it to a markdown renderer unconverted.
    /// </summary>
    private const string ApodFragment =
        """<p><a href="https://apod.nasa.gov/apod/astropix.html"><img src="https://apod.nasa.gov/apod/calendar/S_260901.jpg" align="left" alt="Did you need to be on the right side of this airplane to see this eclipse?" border="0" /></a> Did you need to be on the right side of this airplane to see this eclipse?</p><br clear="all"/>""";

    [Fact]
    public void Apod_fragment_keeps_its_image()
    {
        var markdown = FeedFragmentToMarkdown.Convert(
            ApodFragment, new Uri("https://apod.nasa.gov/apod.rss"));

        output.WriteLine($"[{markdown}]");

        Assert.False(string.IsNullOrWhiteSpace(markdown));
        Assert.Contains("S_260901.jpg", markdown);
    }

    [Fact]
    public void Fragment_with_headings_keeps_them()
    {
        const string html =
            "<h2>A heading</h2><p>Some prose with <strong>bold</strong> in it.</p>" +
            "<h3>A smaller heading</h3><p>More prose.</p>";

        var markdown = FeedFragmentToMarkdown.Convert(html, new Uri("https://example.test/post"));

        output.WriteLine($"[{markdown}]");

        Assert.Contains("A heading", markdown);
        Assert.Contains("#", markdown);
    }

    [Fact]
    public void Relative_links_become_absolute_against_the_item_url()
    {
        // The whole reason the item's own link is passed as the base URI. A
        // relative href reaching SafeLinkOpener is refused, because that gate
        // requires an absolute http or https URL, so the link does nothing
        // when clicked.
        const string html = """<p>See <a href="/apod/ap260831.html">yesterday's picture</a>.</p>""";

        var markdown = FeedFragmentToMarkdown.Convert(
            html, new Uri("https://apod.nasa.gov/apod/astropix.html"));

        output.WriteLine($"[{markdown}]");

        Assert.Contains("https://apod.nasa.gov/apod/ap260831.html", markdown);
    }

    [Fact]
    public void Relative_image_becomes_absolute_against_the_item_url()
    {
        const string html = """<p><img src="image/2609/eclipse.jpg" alt="An eclipse" /></p>""";

        var markdown = FeedFragmentToMarkdown.Convert(
            html, new Uri("https://apod.nasa.gov/apod/ap260901.html"));

        output.WriteLine($"[{markdown}]");

        Assert.Contains("https://apod.nasa.gov/apod/image/2609/eclipse.jpg", markdown);
    }
}
