using System.Collections.Generic;
using System.Linq;
using MarkdownViewer.Lab.Services.Ml;
using MarkdownViewer.Lab.Services.Storage;

namespace MarkdownViewer.Lab.Tests.Ingestion.Stubs;

/// <summary>
/// Deterministic 384-dimension stub embedder for use in tests that must not
/// require model files. Produces reproducible vectors by seeding a
/// <see cref="System.Random"/> from the XxHash64 of the input text.
///
/// 384 dimensions matches the hardcoded dimension in <see cref="MarkdownViewer.Lab.Services.Storage.WorkspaceStore"/>
/// so no plumbing changes are required.
/// </summary>
internal sealed class StubEmbedder : IOnnxEmbedder
{
    public static readonly StubEmbedder Instance = new();

    public int Dimensions => 384;

    public float[] Embed(string text)
    {
        var h = (int)(ContentHash.Compute(text).Value & 0x7FFF_FFFF);
        var rng = new System.Random(h);
        var v = new float[384];
        for (int i = 0; i < 384; i++)
            v[i] = (float)rng.NextDouble();
        return v;
    }

    public float[][] EmbedBatch(IReadOnlyList<string> texts) =>
        texts.Select(Embed).ToArray();
}
