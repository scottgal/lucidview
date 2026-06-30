using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;

namespace MarkdownViewer.Lab.Services.Ml;

/// <summary>
/// Embedder interface consumed by downstream tasks (e.g. WorkspaceIngestor).
/// Kept here so Task 10 can stub the embedder without re-introducing the interface.
/// </summary>
public interface IOnnxEmbedder
{
    /// <summary>Output vector dimensionality (384 for all-MiniLM-L6-v2).</summary>
    int Dimensions { get; }

    /// <summary>Embed a single text string into a float vector.</summary>
    float[] Embed(string text);

    /// <summary>Embed a batch of text strings into float vectors.</summary>
    float[][] EmbedBatch(IReadOnlyList<string> texts);
}

/// <summary>
/// ONNX-Runtime based sentence embedder for <c>all-MiniLM-L6-v2</c>.
/// Dimensions are hardcoded at 384.
///
/// <para>
/// <b>Tokenizer scaffold:</b> <see cref="Embed"/> and <see cref="EmbedBatch"/> throw
/// <see cref="NotSupportedException"/> until <c>Mostlylucid.LucidRAG.DocSummarizer.Core</c>
/// (which contains <c>MiniLmTokenizer</c>) is published to nuget.org. The typed exception
/// ensures downstream callers fail loudly, never silently receive garbage embeddings.
/// Replace the throw with the real tokenizer call once the package lands.
/// </para>
/// </summary>
public sealed class OnnxEmbedder : IOnnxEmbedder, IDisposable
{
    /// <summary>
    /// Canonical scaffold message used in <see cref="NotSupportedException"/>.
    /// Exposed as a public constant so tests can pin the exact text without duplication.
    /// </summary>
    public const string TokenizerNotAvailableMessage =
        "Tokenizer not available: Mostlylucid.LucidRAG.DocSummarizer.Core MiniLmTokenizer " +
        "is pending publish to nuget.org. Replace this throw once the package is available.";

    private readonly InferenceSession _session;

    /// <inheritdoc/>
    public int Dimensions { get; } = 384;

    private OnnxEmbedder(InferenceSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Opens an ONNX inference session from <paramref name="modelPath"/> using the given
    /// execution provider.
    /// </summary>
    public static OnnxEmbedder Load(string modelPath, ExecutionProvider ep)
    {
        using var opts = BuildSessionOptions(ep);
        return new OnnxEmbedder(new InferenceSession(modelPath, opts));
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// Always thrown until the tokenizer package is published. See <see cref="TokenizerNotAvailableMessage"/>.
    /// </exception>
    public float[] Embed(string text)
        => EmbedBatch(new[] { text })[0];

    /// <summary>
    /// Embeds a batch of texts. Currently throws until tokenizer is available.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Always thrown until the tokenizer package is published. See <see cref="TokenizerNotAvailableMessage"/>.
    /// </exception>
    public float[][] EmbedBatch(IReadOnlyList<string> texts)
        => throw new NotSupportedException(TokenizerNotAvailableMessage);

    /// <inheritdoc/>
    public void Dispose() => _session.Dispose();

    private static SessionOptions BuildSessionOptions(ExecutionProvider ep)
    {
        var opts = new SessionOptions();
        switch (ep)
        {
            case ExecutionProvider.Cuda:
                opts.AppendExecutionProvider_CUDA();
                break;
            case ExecutionProvider.DirectML:
                opts.AppendExecutionProvider_DML();
                break;
            case ExecutionProvider.CoreML:
                opts.AppendExecutionProvider_CoreML();
                break;
            case ExecutionProvider.Cpu:
                // CPU provider is the OnnxRuntime default; no call required.
                break;
        }
        return opts;
    }
}
