using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;

namespace MarkdownViewer.Lab.Services.Ml;

/// <summary>
/// Named-entity span returned by <see cref="NerExtractor"/>.
/// </summary>
/// <param name="Type">BIO entity type label (e.g. "PER", "ORG", "LOC").</param>
/// <param name="Surface">The original surface text of the entity.</param>
/// <param name="Start">Character start offset in the original input string (inclusive).</param>
/// <param name="End">Character end offset in the original input string (exclusive).</param>
public sealed record EntitySpan(string Type, string Surface, int Start, int End);

/// <summary>
/// NER extractor interface consumed by downstream tasks (e.g. WorkspaceIngestor).
/// Kept here so Task 10 can stub the extractor without re-introducing the interface.
/// </summary>
public interface INerExtractor
{
    /// <summary>Extract named-entity spans from <paramref name="text"/>.</summary>
    IReadOnlyList<EntitySpan> Extract(string text);
}

/// <summary>
/// ONNX-Runtime based named-entity recogniser for BERT-base-NER.
///
/// <para>
/// <b>Tokenizer scaffold:</b> <see cref="Extract"/> throws <see cref="NotSupportedException"/>
/// until <c>Mostlylucid.LucidRAG.DocSummarizer.Core</c> (which contains <c>MiniLmTokenizer</c>)
/// is published to nuget.org. Chunking strategy: 400-token windows, 50-token overlap, BIO decode.
/// Replace the throw with the real pipeline once the package lands.
/// </para>
/// </summary>
public sealed class NerExtractor : INerExtractor, IDisposable
{
    /// <summary>
    /// Canonical scaffold message used in <see cref="NotSupportedException"/>.
    /// Exposed as a public constant so tests can pin the exact text without duplication.
    /// </summary>
    public const string TokenizerNotAvailableMessage =
        "Tokenizer not available: Mostlylucid.LucidRAG.DocSummarizer.Core MiniLmTokenizer " +
        "is pending publish to nuget.org. Replace this throw once the package is available.";

    private readonly InferenceSession _session;

    private NerExtractor(InferenceSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Opens an ONNX inference session from <paramref name="modelPath"/> using the given
    /// execution provider.
    /// </summary>
    public static NerExtractor Load(string modelPath, ExecutionProvider ep)
    {
        var opts = BuildSessionOptions(ep);
        return new NerExtractor(new InferenceSession(modelPath, opts));
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// Always thrown until the tokenizer package is published. See <see cref="TokenizerNotAvailableMessage"/>.
    /// </exception>
    public IReadOnlyList<EntitySpan> Extract(string text)
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
                break;
        }
        return opts;
    }
}
