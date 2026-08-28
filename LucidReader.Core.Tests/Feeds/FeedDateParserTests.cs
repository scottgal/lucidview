using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedDateParserTests
{
    [Theory]
    [InlineData("Wed, 26 Aug 2026 09:00:00 GMT", "2026-08-26T09:00:00+00:00")]
    [InlineData("26 Aug 2026 09:00:00 GMT", "2026-08-26T09:00:00+00:00")]
    [InlineData("Wed, 26 Aug 2026 09:00:00 +0100", "2026-08-26T09:00:00+01:00")]
    [InlineData("2026-08-27T09:00:00Z", "2026-08-27T09:00:00+00:00")]
    [InlineData("2026-08-27T09:00:00+02:00", "2026-08-27T09:00:00+02:00")]
    [InlineData("2026-08-27", "2026-08-27T00:00:00+00:00")]
    public void Recognised_formats_parse(string input, string expected)
    {
        var parsed = FeedDateParser.TryParse(input);

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeOffset.Parse(expected), parsed!.Value);
    }

    [Theory]
    [InlineData("last Tuesday-ish")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0000-00-00")]
    public void Unrecognised_input_returns_null_rather_than_throwing(string? input)
    {
        Assert.Null(FeedDateParser.TryParse(input));
    }

    [Fact]
    public void Surrounding_whitespace_is_tolerated()
    {
        Assert.NotNull(FeedDateParser.TryParse("  Wed, 26 Aug 2026 09:00:00 GMT \n"));
    }

    [Fact]
    public void Wrong_weekday_name_still_parses_to_the_correct_instant()
    {
        // 27 August 2026 is a Thursday, not a Wednesday. Feed generators get
        // this wrong routinely; the date itself is still trustworthy.
        var parsed = FeedDateParser.TryParse("Wed, 27 Aug 2026 10:00:00 GMT");

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T10:00:00+00:00"), parsed!.Value);
    }

    [Fact]
    public void Correct_weekday_name_still_parses_unchanged()
    {
        // 27 August 2026 is genuinely a Thursday.
        var parsed = FeedDateParser.TryParse("Thu, 27 Aug 2026 10:00:00 GMT");

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T10:00:00+00:00"), parsed!.Value);
    }

    [Fact]
    public void Leading_non_weekday_token_is_not_silently_stripped()
    {
        // "Foo" is not a weekday name and not part of any recognised format;
        // stripping arbitrary leading tokens would corrupt this into a valid
        // parse of "27 Aug 2026 10:00:00 GMT", which must not happen.
        Assert.Null(FeedDateParser.TryParse("Foo, 27 Aug 2026 10:00:00 GMT"));
    }
}
