using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The wording of the approval step, and the scraped note on the per-feed
/// update line.
///
/// A Window cannot be constructed in a unit test in this repo, so the dialog
/// itself is exercised by ux-scripts/run-scraped-feed.sh; everything worth
/// asserting about what it says lives here, the same split AddFeedTests uses.
/// </summary>
public class ScrapeApprovalTests
{
    [Fact]
    public void The_offer_says_it_is_a_guess_and_how_many_articles_were_found()
    {
        var message = AddFeedInput.DescribeScrapeOffer(20);

        Assert.Contains("No feed here", message);
        Assert.Contains("20 articles", message);
        Assert.Contains("guess", message);
    }

    /// <summary>
    /// An offer that came from the fallback reads differently from one the
    /// detector found. Both are guesses and both say so; this one is the guess
    /// mylo's own reading of the page disagreed with, and a user deciding
    /// whether to subscribe should be told that rather than shown the same
    /// sentence.
    /// </summary>
    [Fact]
    public void An_offer_from_the_fallback_says_the_guess_is_a_weaker_one()
    {
        var confident = AddFeedInput.DescribeScrapeOffer(10);
        var fallback = AddFeedInput.DescribeScrapeOffer(10, fromFallback: true);

        Assert.NotEqual(confident, fallback);
        Assert.Contains("second pass", fallback);
        Assert.Contains("less certain", fallback);
        Assert.DoesNotContain("second pass", confident);
    }

    [Fact]
    public void The_offer_counts_one_article_in_the_singular()
    {
        Assert.Contains("1 article.", AddFeedInput.DescribeScrapeOffer(1));
        Assert.DoesNotContain("1 articles", AddFeedInput.DescribeScrapeOffer(1));
    }

    /// <summary>
    /// The sample is what makes the offer judgeable. Without it a user cannot
    /// tell "it found the articles" from "it found the tag cloud", which is
    /// precisely the mistake the detector can make.
    /// </summary>
    [Fact]
    public void The_sample_lists_the_titles_that_were_found()
    {
        var message = AddFeedInput.DescribeScrapeSample(
            ["First article", "Second article", "Third article"]);

        Assert.Contains("First article", message);
        Assert.Contains("Second article", message);
        Assert.Contains("Third article", message);
    }

    [Fact]
    public void The_sample_shortens_a_very_long_title_rather_than_wrapping_the_dialog()
    {
        var message = AddFeedInput.DescribeScrapeSample([new string('x', 200)]);

        Assert.EndsWith("...", message);
        Assert.True(message.Length < 100);
    }

    [Fact]
    public void The_sample_is_empty_when_there_is_nothing_to_show()
    {
        Assert.Equal(string.Empty, AddFeedInput.DescribeScrapeSample(null));
        Assert.Equal(string.Empty, AddFeedInput.DescribeScrapeSample([]));
    }

    [Fact]
    public void Pressing_add_without_approving_says_what_is_missing()
    {
        Assert.Contains("approval", AddFeedInput.ScrapeNotApprovedMessage);
    }

    // -------------------------------------------------------------------
    // The update line
    // -------------------------------------------------------------------

    private static FeedUpdateLine Describe(bool isScraped, bool isAutoPaused = false) =>
        FeedUpdateSummary.Describe(
            isFeedSelected: true,
            isRefreshing: false,
            isAutoPaused: isAutoPaused,
            isEnabled: true,
            lastFetchedUtc: DateTimeOffset.Parse("2026-08-28T11:55:00Z"),
            lastSuccessUtc: DateTimeOffset.Parse("2026-08-28T11:55:00Z"),
            lastError: null,
            nextDueUtc: DateTimeOffset.Parse("2026-08-28T12:25:00Z"),
            now: DateTimeOffset.Parse("2026-08-28T12:00:00Z"),
            isScraped: isScraped);

    [Fact]
    public void A_published_feeds_line_is_unchanged()
    {
        var line = Describe(isScraped: false);

        Assert.DoesNotContain(FeedUpdateSummary.ScrapedShortNote, line.ShortText);
        Assert.DoesNotContain("Scraped", line.Text);
        Assert.Equal("5 min ago", line.ShortText);
    }

    [Fact]
    public void A_scraped_feeds_line_says_so_in_both_forms()
    {
        var line = Describe(isScraped: true);

        Assert.Contains(FeedUpdateSummary.ScrapedShortNote, line.ShortText);
        Assert.Contains("Scraped page, not a published feed", line.Text);
        Assert.Contains("changes its layout", line.Text);
        Assert.StartsWith("5 min ago", line.ShortText);
    }

    /// <summary>
    /// A paused scrape is still a scrape, and that is the state where knowing
    /// which kind of subscription broke matters most.
    /// </summary>
    [Fact]
    public void A_paused_scrape_still_says_it_is_a_scrape()
    {
        var line = Describe(isScraped: true, isAutoPaused: true);

        Assert.Contains("Paused after repeated failures", line.Text);
        Assert.Contains(FeedUpdateSummary.ScrapedShortNote, line.ShortText);
    }

    [Fact]
    public void A_scraped_note_never_appears_on_a_hidden_line()
    {
        var line = FeedUpdateSummary.Describe(
            isFeedSelected: false,
            isRefreshing: false,
            isAutoPaused: false,
            isEnabled: true,
            lastFetchedUtc: null,
            lastSuccessUtc: null,
            lastError: null,
            nextDueUtc: null,
            now: DateTimeOffset.Parse("2026-08-28T12:00:00Z"),
            isScraped: true);

        Assert.False(line.IsVisible);
        Assert.Equal(string.Empty, line.Text);
        Assert.Equal(string.Empty, line.ShortText);
    }
}
