using LucidReader.Core.Offline;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

public class StubDetectorTests
{
    private static string Words(int count) =>
        "<p>" + string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", count)) + "</p>";

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
    public void Probe_script_block_text_content_is_counted_as_visible_length()
    {
        // Documented, accepted limitation: the tag-stripping regex removes
        // markup but not the text content of <script> or <style> elements.
        // A short teaser followed by an embedded widget's inline script can
        // therefore read as long enough to be a full article. Feed sanitizers
        // upstream of this class are expected to strip <script>/<style>
        // before content reaches here; this test documents what happens if
        // one doesn't, rather than trying to guard against it in IsStub.
        var teaser = "<p>Just the opening sentence of the piece, nothing more here.</p>";
        var script = "<script>" + string.Concat(Enumerable.Repeat(
            "var x = 1; console.log('tracking pixel loaded'); ", 40)) + "</script>";

        var html = teaser + script;

        Assert.False(StubDetector.IsStub(html));
    }
}
