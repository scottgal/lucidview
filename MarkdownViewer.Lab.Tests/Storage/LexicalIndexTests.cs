using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Storage;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Storage;

public class LexicalIndexTests : IAsyncDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), $"lab-lex-{Guid.NewGuid():N}");

    [Fact]
    public async Task IndexThenSearch_ReturnsBm25Top()
    {
        Directory.CreateDirectory(_tmp);
        await using var idx = await LexicalIndex.OpenAsync(_tmp, CancellationToken.None);
        await idx.IndexAsync(1, ContentHash.Compute("a"),
                              "the quick brown fox jumps", CancellationToken.None);
        await idx.IndexAsync(2, ContentHash.Compute("b"),
                              "a lazy red fox sleeps in the sun", CancellationToken.None);
        await idx.IndexAsync(3, ContentHash.Compute("c"),
                              "crows are very black birds", CancellationToken.None);
        await idx.CommitAsync(CancellationToken.None);

        var results = await idx.SearchAsync("fox", k: 5, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(r => r.SegmentId).Should().Contain(new long[] { 1, 2 });
    }

    public async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_tmp)) Directory.Delete(_tmp, recursive: true);
        await Task.CompletedTask;
    }
}
