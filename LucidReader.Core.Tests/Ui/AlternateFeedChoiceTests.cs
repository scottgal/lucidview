using LucidReader.Core.Feeds;
using LucidReader.Models;
using LucidReader.Views;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// What the add-feed chooser does with a site that offers the same articles
/// twice. DiscoveredFeedChoice is a plain class on purpose, so this is
/// asserted directly rather than by building a Window, which this project's
/// tests cannot do.
/// </summary>
public class AlternateFeedChoiceTests
{
    private static DiscoveredFeedChoice ChoiceFor(DiscoveredFeed feed) =>
        new() { Feed = feed, IsSelected = !feed.IsAlternate };

    [Fact]
    public void An_ordinary_feed_starts_ticked()
    {
        var choice = ChoiceFor(new DiscoveredFeed("https://example.com/atom", "Example", null));

        Assert.True(choice.IsSelected);
        Assert.Equal(string.Empty, choice.AlternateSuffix);
    }

    [Fact]
    public void An_alternate_format_starts_unticked()
    {
        var choice = ChoiceFor(new DiscoveredFeed(
            "https://example.com/rss", "Example", null,
            IsAlternate: true, AlternateOfUrl: "https://example.com/atom"));

        Assert.False(choice.IsSelected);
    }

    [Fact]
    public void An_unticked_row_says_why_it_is_unticked()
    {
        var choice = ChoiceFor(new DiscoveredFeed(
            "https://example.com/rss", "Example", null,
            IsAlternate: true, AlternateOfUrl: "https://example.com/atom"));

        Assert.Contains("same articles as https://example.com/atom", choice.Label);
    }

    [Fact]
    public void An_alternate_with_no_named_partner_still_explains_itself()
    {
        var choice = ChoiceFor(new DiscoveredFeed(
            "https://example.com/rss", null, null, IsAlternate: true));

        Assert.Contains("same articles as", choice.Label);
    }

    [Fact]
    public void The_status_line_explains_an_unticked_alternate()
    {
        var message = AddFeedInput.DescribeDiscovery(2, 1);

        Assert.Contains("Found 2 feeds", message);
        Assert.Contains("same articles in another format", message);
        Assert.Contains("it is left unticked", message);
    }

    [Fact]
    public void The_status_line_pluralises_several_alternates()
    {
        var message = AddFeedInput.DescribeDiscovery(4, 2);

        Assert.Contains("2 of them carry", message);
        Assert.Contains("they are left unticked", message);
    }

    [Fact]
    public void The_status_line_is_unchanged_when_nothing_is_an_alternate()
    {
        Assert.Equal(
            "Found 3 feeds. Choose the ones you want.",
            AddFeedInput.DescribeDiscovery(3, 0));
    }
}
