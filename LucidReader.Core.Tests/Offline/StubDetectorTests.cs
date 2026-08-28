using LucidReader.Core.Offline;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

public class StubDetectorTests
{
    private static string Words(int count) =>
        "<p>" + string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", count)) + "</p>";

    // Produces HTML whose visible text (after tag-stripping) is exactly
    // `length` characters long, so band-edge tests can pin the thresholds
    // precisely instead of approximating with word counts.
    private static string ExactVisibleText(int length) =>
        "<p>" + new string('a', length) + "</p>";

    // Same, but the last `suffix.Length` visible characters are `suffix`
    // (separated by a single space), so the read-more tail check has
    // something exact to match against at a known total length.
    private static string ExactVisibleTextEndingWith(int totalLength, string suffix)
    {
        var fillerLength = totalLength - suffix.Length - 1;
        return "<p>" + new string('a', fillerLength) + " " + suffix + "</p>";
    }

    [Fact]
    public void Null_content_is_a_stub()
    {
        Assert.True(StubDetector.IsStub(null));
    }

    [Fact]
    public void Empty_content_is_a_stub()
    {
        Assert.True(StubDetector.IsStub("   "));
    }

    [Fact]
    public void A_short_summary_is_a_stub()
    {
        Assert.True(StubDetector.IsStub("<p>Just the opening sentence of the piece.</p>"));
    }

    [Fact]
    public void A_long_body_is_not_a_stub()
    {
        Assert.False(StubDetector.IsStub(Words(200)));
    }

    [Theory]
    [InlineData("<p>An opening line.</p><p><a href=\"https://x.example/1\">Read more</a></p>")]
    [InlineData("<p>An opening line.</p><a href=\"https://x.example/1\">Continue reading</a>")]
    [InlineData("<p>An opening line.</p><a href=\"https://x.example/1\">Read the full article</a>")]
    [InlineData("<p>An opening line.</p><a href=\"https://x.example/1\">[...]</a>")]
    public void A_trailing_read_more_link_marks_a_stub(string html)
    {
        Assert.True(StubDetector.IsStub(html));
    }

    [Fact]
    public void A_read_more_phrase_in_the_middle_of_a_long_article_is_not_a_stub()
    {
        var html = Words(150) + "<p>As we said, read more about this elsewhere.</p>" + Words(150);

        Assert.False(StubDetector.IsStub(html));
    }

    // The tests above never actually exercise the middle-band tail check:
    // the theory cases are short enough to hit the obvious-stub floor, and
    // the two full-body tests are long enough to hit the full-article
    // ceiling. Everything below lands deliberately inside [400, 1500) so the
    // branch that decides based on how the content ends is proven to run.

    [Fact]
    public void A_middle_band_teaser_ending_with_a_read_more_link_is_a_stub()
    {
        var html = ExactVisibleTextEndingWith(700, "Read more");

        Assert.True(StubDetector.IsStub(html));
    }

    [Fact]
    public void A_middle_band_article_with_no_read_more_ending_is_not_a_stub()
    {
        var html = ExactVisibleText(700);

        Assert.False(StubDetector.IsStub(html));
    }

    [Fact]
    public void A_middle_band_article_with_a_read_more_phrase_in_the_middle_is_not_a_stub()
    {
        // The phrase sits well before the last 120 characters (both the lead
        // and trail blocks are longer than the tail window), so this pins
        // that the check only looks at how the content ends, not whether the
        // phrase appears anywhere in the body.
        var lead = new string('a', 400);
        var trail = new string('b', 400);
        var html = "<p>" + lead + " read more about this elsewhere. " + trail + "</p>";

        Assert.False(StubDetector.IsStub(html));
    }

    [Fact]
    public void Exactly_399_characters_is_a_stub_via_the_obvious_stub_floor()
    {
        Assert.True(StubDetector.IsStub(ExactVisibleText(399)));
    }

    [Fact]
    public void Exactly_400_characters_enters_the_middle_band_and_a_read_more_tail_marks_it_a_stub()
    {
        var html = ExactVisibleTextEndingWith(400, "Read more");

        Assert.True(StubDetector.IsStub(html));
    }

    [Fact]
    public void Exactly_1499_characters_with_a_read_more_tail_is_still_middle_band_and_a_stub()
    {
        var html = ExactVisibleTextEndingWith(1499, "Read more");

        Assert.True(StubDetector.IsStub(html));
    }

    [Fact]
    public void Exactly_1500_characters_is_a_full_article_even_with_a_read_more_tail()
    {
        // The full-article threshold is checked before the tail, so at
        // exactly 1500 the ending stops mattering. This, paired with the
        // 1499 case above, proves the two thresholds meet with no gap and
        // no overlap.
        var html = ExactVisibleTextEndingWith(1500, "Read more");

        Assert.False(StubDetector.IsStub(html));
    }

    [Fact]
    public void Markup_does_not_count_toward_the_length()
    {
        // Long enough in raw characters, but almost no actual text.
        var html = "<div class=\"wrapper-with-a-very-long-class-name-indeed\">"
                   + string.Concat(Enumerable.Repeat("<span style=\"color:#ffffff\"></span>", 60))
                   + "<p>Two words.</p></div>";

        Assert.True(StubDetector.IsStub(html));
    }

    [Fact]
    public void Script_content_is_not_counted_as_visible_text()
    {
        // <script> content is not visible text in any rendering context, so
        // it must not count toward the length. Without this, a short teaser
        // padded with an embedded widget's inline tracking script (common in
        // real feeds) would read as long enough to be a full article, and
        // the offline fetch that the teaser actually needed would be skipped.
        var teaser = "<p>Just the opening sentence of the piece, nothing more here.</p>";
        var script = "<script>" + string.Concat(Enumerable.Repeat(
            "var x = 1; console.log('tracking pixel loaded'); ", 40)) + "</script>";

        var html = teaser + script;

        Assert.True(StubDetector.IsStub(html));
    }

    [Fact]
    public void Style_content_is_not_counted_as_visible_text()
    {
        var teaser = "<p>Just the opening sentence of the piece, nothing more here.</p>";
        var style = "<style>" + string.Concat(Enumerable.Repeat(
            ".widget-class-name { color: #fff; margin: 0; padding: 0; } ", 40)) + "</style>";

        var html = teaser + style;

        Assert.True(StubDetector.IsStub(html));
    }
}
