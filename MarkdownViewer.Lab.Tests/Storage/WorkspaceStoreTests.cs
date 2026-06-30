using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Storage;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Storage;

public class WorkspaceStoreTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lab-ws-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateThenOpen_HealthyState()
    {
        Directory.CreateDirectory(_root);
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        ws.State.Should().Be(WorkspaceState.Healthy);
        ws.Manifest.Name.Should().Be("default");
    }

    [Fact]
    public async Task IntegritySweep_NoDrift_Healthy()
    {
        Directory.CreateDirectory(_root);
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        ws.LastSweep.DriftRatio.Should().Be(0.0);
        ws.LastSweep.Issues.Should().BeEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        await Task.CompletedTask;
    }
}
