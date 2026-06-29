using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Storage;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Storage;

public class VectorStoreTests : IAsyncDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), $"lab-vec-{Guid.NewGuid():N}.duckdb");

    private static readonly float[] Vec1000 = { 1f, 0f, 0f, 0f };
    private static readonly float[] Vec0100 = { 0f, 1f, 0f, 0f };
    private static readonly float[] Vec9100 = { 0.9f, 0.1f, 0f, 0f };
    private static readonly float[] QueryVec = { 1f, 0f, 0f, 0f };

    [Fact]
    public async Task UpsertThenSearch_ReturnsNearest()
    {
        await using var store = await VectorStore.OpenAsync(_tmp, dimensions: 4, CancellationToken.None);
        await store.UpsertAsync(1, ContentHash.Compute("a"), Vec1000, CancellationToken.None);
        await store.UpsertAsync(2, ContentHash.Compute("b"), Vec0100, CancellationToken.None);
        await store.UpsertAsync(3, ContentHash.Compute("c"), Vec9100, CancellationToken.None);

        var results = await store.SearchAsync(QueryVec, k: 2, CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].SegmentId.Should().Be(1);    // exact match first
        results[1].SegmentId.Should().Be(3);    // close match second
    }

    [Fact]
    public async Task EmptyStore_SearchReturnsEmpty()
    {
        await using var store = await VectorStore.OpenAsync(_tmp, dimensions: 4, CancellationToken.None);
        var results = await store.SearchAsync(QueryVec, k: 5, CancellationToken.None);
        results.Should().BeEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_tmp)) File.Delete(_tmp);
        // DuckDB may create a WAL file alongside the main DB file.
        var wal = _tmp + ".wal";
        if (File.Exists(wal)) File.Delete(wal);
        await Task.CompletedTask;
    }
}
