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
}
