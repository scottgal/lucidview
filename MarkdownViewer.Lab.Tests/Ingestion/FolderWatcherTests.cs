using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Ingestion;
using MarkdownViewer.Lab.Services.Storage;
using MarkdownViewer.Lab.Tests.Ingestion.Stubs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ingestion;

/// <summary>
/// Tests for <see cref="FolderWatcher"/>.
/// </summary>
public class FolderWatcherTests : IAsyncDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"lab-fw-{Guid.NewGuid():N}");

    public FolderWatcherTests()
    {
        Directory.CreateDirectory(_root);
    }

    // ── GetStats initial state ─────────────────────────────────────────────

    [Fact]
    public async Task FolderWatcher_InitialStats_AreZero()
    {
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var ingestor = BuildIngestor(ws);

        var watcher = new FolderWatcher(
            ingestor, _root,
            includeGlob: "*.md",
            excludeGlob: string.Empty,
            channelCapacity: 10,
            log: NullLogger<FolderWatcher>.Instance);

        var stats = watcher.GetStats();
        stats.Depth.Should().Be(0);
        stats.Dropped.Should().Be(0);
        stats.Processed.Should().Be(0);

        await watcher.DisposeAsync();
    }

    // ── Happy path: file arrival increments Processed ─────────────────────

    [Fact]
    public async Task FolderWatcher_ProcessedAdvances_WhenFileDropped()
    {
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var ingestor = BuildIngestor(ws);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var watcher = new FolderWatcher(
            ingestor, _root,
            includeGlob: "*.md",
            excludeGlob: string.Empty,
            channelCapacity: 10,
            log: NullLogger<FolderWatcher>.Instance);

        await watcher.StartAsync(cts.Token);

        // Drop a file into the watched folder.
        var file = Path.Combine(_root, "hello.md");
        await File.WriteAllTextAsync(file,
            "# Hello\n\nSome content for the folder watcher test.", cts.Token);

        // Poll until Processed > 0 or timeout.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (watcher.GetStats().Processed > 0) break;
            await Task.Delay(200, cts.Token);
        }

        watcher.GetStats().Processed.Should().BeGreaterThan(0,
            "the watcher consumer must drain the channel and call IngestAsync");

        await watcher.DisposeAsync();
    }

    // ── Backpressure: drop-newest when channel is full ────────────────────

    [Fact]
    public async Task FolderWatcher_DroppedAdvances_WhenChannelSaturated()
    {
        // Build a watcher with a tiny channel (capacity=2). No StartAsync is
        // called so no consumer is running — the channel fills up deterministically
        // without any timing dependency.
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var ingestor = BuildIngestor(ws);

        var watcher = new FolderWatcher(
            ingestor, _root,
            includeGlob: "*.md",
            excludeGlob: string.Empty,
            channelCapacity: 2,
            log: NullLogger<FolderWatcher>.Instance);

        // Flood 10 items directly using the internal test seam.
        // Consumer is NOT running so the channel fills immediately to capacity=2,
        // and every subsequent enqueue must be dropped.
        for (int i = 0; i < 10; i++)
            watcher.TryEnqueue(Path.Combine(_root, $"fake-{i}.md"));

        var stats = watcher.GetStats();

        stats.Depth.Should().Be(2,
            "channel capacity is 2 so exactly 2 items should be queued");
        stats.Dropped.Should().Be(8,
            "10 enqueues minus 2 capacity = 8 dropped");

        await watcher.DisposeAsync();
    }

    // ── Backpressure: processed counter advances when consumer is running ──

    [Fact]
    public async Task FolderWatcher_DroppedAndProcessed_BothAdvanceUnderLoad()
    {
        // Start a watcher with capacity=2.  Consumer runs for real.
        // Use TryEnqueue to simulate many events without FileSystemWatcher timing.
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var ingestor = BuildIngestor(ws);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Write real .md files so IngestAsync doesn't throw.
        var files = new string[10];
        for (int i = 0; i < 10; i++)
        {
            files[i] = Path.Combine(_root, $"load-{i}.md");
            await File.WriteAllTextAsync(files[i], $"# Load {i}\n\nContent.", cts.Token);
        }

        var watcher = new FolderWatcher(
            ingestor, _root,
            includeGlob: "*.md",
            excludeGlob: string.Empty,
            channelCapacity: 2,
            log: NullLogger<FolderWatcher>.Instance);

        await watcher.StartAsync(cts.Token);

        // Rapid-fire enqueues: with capacity=2, most will drop; those that
        // land will be processed.
        for (int i = 0; i < 10; i++)
            watcher.TryEnqueue(files[i]);

        // Allow consumer to drain.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var s = watcher.GetStats();
            if (s.Processed > 0 && s.Dropped > 0) break;
            await Task.Delay(100, cts.Token);
        }

        var stats = watcher.GetStats();
        stats.Dropped.Should().BeGreaterThan(0,
            "10 enqueues with capacity=2 must produce drops");
        stats.Processed.Should().BeGreaterThan(0,
            "at least some items must be processed by the consumer");

        await watcher.DisposeAsync();
    }

    // ── Exclude glob filters paths out ────────────────────────────────────

    [Fact]
    public async Task FolderWatcher_ExcludeGlob_FiltersMatchingFiles()
    {
        var store = new WorkspaceStore(_root);
        await using var ws = await store.CreateAsync("default", CancellationToken.None);
        var ingestor = BuildIngestor(ws);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var watcher = new FolderWatcher(
            ingestor, _root,
            includeGlob: "*.md",
            excludeGlob: "excluded-*.md",
            channelCapacity: 10,
            log: NullLogger<FolderWatcher>.Instance);

        await watcher.StartAsync(cts.Token);

        // Drop a file that matches the exclude pattern — should be filtered.
        await File.WriteAllTextAsync(
            Path.Combine(_root, "excluded-file.md"),
            "# Excluded\n\nThis should not be ingested.", cts.Token);

        // Give it time to (not) process.
        await Task.Delay(1500, cts.Token);

        watcher.GetStats().Processed.Should().Be(0,
            "excluded files must not reach the consumer");

        await watcher.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static WorkspaceIngestor BuildIngestor(Workspace ws)
        => new WorkspaceIngestor(ws,
            embedder: StubEmbedder.Instance,
            ner: StubNer.Instance,
            ingestors: new IIngestor[] { new MarkdownIngestor() },
            log: NullLogger<WorkspaceIngestor>.Instance);

    public async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        await Task.CompletedTask;
    }
}
