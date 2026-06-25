using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class ExtractionTelemetryTests
{
    [Fact]
    public void Record_StoresLast()
    {
        var t = new ExtractionTelemetry();
        var info = new LastExtractionInfo(
            DateTime.UtcNow, new Uri("https://example.com/"), "Novel",
            Guid.NewGuid(), 1, "Http", TimeSpan.FromMilliseconds(120),
            LlmInductionFired: false, TimeSpan.Zero, BlockCount: 12, OutputCharacterCount: 800);

        t.Record(info);

        Assert.NotNull(t.Last);
        Assert.Equal(info, t.Last);
        Assert.Single(t.Recent);
    }

    [Fact]
    public void Record_CircularBufferCapsAt50()
    {
        var t = new ExtractionTelemetry();
        for (var i = 0; i < 60; i++)
            t.Record(Stub(i));
        Assert.Equal(50, t.Recent.Count);
        Assert.Equal(59, ((LastExtractionInfo)t.Last!).BlockCount);
    }

    [Fact]
    public void ExportNdjson_ProducesOneLinePerRecord()
    {
        var t = new ExtractionTelemetry();
        t.Record(Stub(1));
        t.Record(Stub(2));

        var ndjson = t.ExportNdjson();
        var lines = ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("{", l));
    }

    private static LastExtractionInfo Stub(int i) => new(
        DateTime.UtcNow, new Uri($"https://e/{i}"), "FastPathHit",
        Guid.NewGuid(), 1, "Http", TimeSpan.FromMilliseconds(10),
        false, TimeSpan.Zero, i, i * 100);
}
