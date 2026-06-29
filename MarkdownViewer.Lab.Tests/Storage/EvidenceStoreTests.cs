using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Storage;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Storage;

public class EvidenceStoreTests : IAsyncDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), $"lab-evid-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task PutThenGet_RoundTrips()
    {
        await using var store = await EvidenceStore.OpenAsync(_tmp, new(), CancellationToken.None);
        var h = ContentHash.Compute("hello");
        await store.PutAsync(h, "hello world", CancellationToken.None);
        await store.DrainAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        var got = await store.GetAsync(h, CancellationToken.None);
        got.Should().Be("hello world");
    }

    [Fact]
    public async Task Reopen_ReturnsPersistedValues()
    {
        var h = ContentHash.Compute("persist");
        {
            await using var store = await EvidenceStore.OpenAsync(_tmp, new(), CancellationToken.None);
            await store.PutAsync(h, "persisted text", CancellationToken.None);
            await store.DrainAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        }
        {
            await using var store = await EvidenceStore.OpenAsync(_tmp, new(), CancellationToken.None);
            (await store.GetAsync(h, CancellationToken.None)).Should().Be("persisted text");
        }
    }

    [Fact]
    public async Task SecondGet_HitsCache()
    {
        await using var store = await EvidenceStore.OpenAsync(_tmp, new(), CancellationToken.None);
        var h = ContentHash.Compute("cache");
        await store.PutAsync(h, "cached", CancellationToken.None);
        await store.DrainAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        _ = await store.GetAsync(h, CancellationToken.None);
        var statsBefore = store.GetStats();
        _ = await store.GetAsync(h, CancellationToken.None);
        var statsAfter = store.GetStats();

        statsAfter.Hits.Should().BeGreaterThan(statsBefore.Hits);
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_tmp)) File.Delete(_tmp);
        await Task.CompletedTask;
    }
}
