using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Lab.Services.Ingestion;
using MarkdownViewer.Lab.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownViewer.Lab.Tests.Ingestion.Stubs;

/// <summary>
/// Builds a <see cref="WorkspaceIngestor"/> whose inner <see cref="IIngestor"/>
/// blocks <c>IngestAsync</c> until <see cref="Unblock"/> is called.
///
/// Stalls the <see cref="FolderWatcher"/> consumer so the bounded channel
/// fills up and drop-newest backpressure can be observed.
/// </summary>
internal sealed class BlockingIngestor : IAsyncDisposable
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _root;
    private readonly Workspace _ws;
    private readonly WorkspaceIngestor _wsIngestor;

    public BlockingIngestor(Workspace ws)
    {
        _root = string.Empty; // not used directly
        _ws = ws;
        _wsIngestor = new WorkspaceIngestor(
            ws: ws,
            embedder: StubEmbedder.Instance,
            ner: StubNer.Instance,
            ingestors: new IIngestor[] { new Inner(_gate) },
            log: NullLogger<WorkspaceIngestor>.Instance);
    }

    /// <summary>Releases all blocked <c>IngestAsync</c> calls.</summary>
    public void Unblock() => _gate.TrySetResult();

    /// <summary>Returns a WorkspaceIngestor backed by the blocking inner ingestor.</summary>
    public WorkspaceIngestor WorkspaceIngestor => _wsIngestor;

    public async ValueTask DisposeAsync()
    {
        Unblock(); // prevent consumer tasks from hanging
        await _ws.DisposeAsync();
    }

    private sealed class Inner : IIngestor
    {
        private readonly TaskCompletionSource _gate;
        public Inner(TaskCompletionSource gate) => _gate = gate;

        public bool CanHandle(string path) =>
            path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

        public async Task<IngestedDocument> IngestAsync(string path, CancellationToken ct)
        {
            // Block until Unblock() is called (or ct fires).
            await _gate.Task.WaitAsync(ct).ConfigureAwait(false);
            return new IngestedDocument(path, "text/markdown",
                Path.GetFileNameWithoutExtension(path),
                new List<RawSegment> { new RawSegment(0, "content") });
        }
    }
}
