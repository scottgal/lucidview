using System.Collections.Generic;
using MarkdownViewer.Lab.Services.Ml;

namespace MarkdownViewer.Lab.Tests.Ingestion.Stubs;

/// <summary>
/// No-op NER extractor stub for tests that must not require model files.
/// Always returns an empty entity list.
/// </summary>
internal sealed class StubNer : INerExtractor
{
    public static readonly StubNer Instance = new();

    public IReadOnlyList<EntitySpan> Extract(string text) =>
        System.Array.Empty<EntitySpan>();
}
