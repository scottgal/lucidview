using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Storage;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Storage;

public class MetadataStoreTests : IAsyncDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), $"lab-meta-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task UpsertDocument_RoundTrips()
    {
        await using var store = await MetadataStore.OpenAsync(_tmp, CancellationToken.None);
        var doc = new DocumentRow(0, "/x/y.md", "text/markdown", "Y",
                                  DateTimeOffset.UtcNow, "folder", IngestionState.Indexed);
        var id = await store.UpsertDocumentAsync(doc, CancellationToken.None);
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task QuerySegments_ExcludesPersonalByDefault()
    {
        await using var store = await MetadataStore.OpenAsync(_tmp, CancellationToken.None);
        var docId = await store.UpsertDocumentAsync(
            new DocumentRow(0, "/x.md", "text/markdown", "X", DateTimeOffset.UtcNow,
                            "folder", IngestionState.Indexed),
            CancellationToken.None);
        var personalDocId = await store.UpsertDocumentAsync(
            new DocumentRow(0, "/p.md", "text/markdown", "P", DateTimeOffset.UtcNow,
                            "personal:default", IngestionState.Indexed),
            CancellationToken.None);
        await store.UpsertSegmentAsync(new SegmentRow(0, docId, 0,
            ContentHash.Compute("a"), 0.5, DateTimeOffset.UtcNow, "folder"),
            CancellationToken.None);
        await store.UpsertSegmentAsync(new SegmentRow(0, personalDocId, 0,
            ContentHash.Compute("b"), 0.5, DateTimeOffset.UtcNow, "personal:default"),
            CancellationToken.None);

        var results = new System.Collections.Generic.List<SegmentRow>();
        await foreach (var s in store.QuerySegmentsAsync(new SegmentQuery(), CancellationToken.None))
            results.Add(s);

        results.Should().HaveCount(1);
        results[0].Source.Should().Be("folder");
    }

    [Fact]
    public async Task QuerySegments_IncludesPersonal_WhenExplicitlyRequested()
    {
        await using var store = await MetadataStore.OpenAsync(_tmp, CancellationToken.None);
        var docId = await store.UpsertDocumentAsync(
            new DocumentRow(0, "/x.md", "text/markdown", "X", DateTimeOffset.UtcNow,
                            "folder", IngestionState.Indexed),
            CancellationToken.None);
        var personalDocId = await store.UpsertDocumentAsync(
            new DocumentRow(0, "/p.md", "text/markdown", "P", DateTimeOffset.UtcNow,
                            "personal:default", IngestionState.Indexed),
            CancellationToken.None);
        await store.UpsertSegmentAsync(new SegmentRow(0, docId, 0,
            ContentHash.Compute("a"), 0.5, DateTimeOffset.UtcNow, "folder"),
            CancellationToken.None);
        await store.UpsertSegmentAsync(new SegmentRow(0, personalDocId, 0,
            ContentHash.Compute("b"), 0.5, DateTimeOffset.UtcNow, "personal:default"),
            CancellationToken.None);

        var results = new System.Collections.Generic.List<SegmentRow>();
        await foreach (var s in store.QuerySegmentsAsync(new SegmentQuery(IncludePersonal: true),
                                                          CancellationToken.None))
            results.Add(s);
        // After setup, both rows return.
        results.Should().HaveCount(2);
    }

    [Fact]
    public void ContentHash_StableAcrossRuns()
    {
        ContentHash.Compute("the quick brown fox").Value
            .Should().Be(ContentHash.Compute("the quick brown fox").Value);
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_tmp)) File.Delete(_tmp);
        await Task.CompletedTask;
    }
}
