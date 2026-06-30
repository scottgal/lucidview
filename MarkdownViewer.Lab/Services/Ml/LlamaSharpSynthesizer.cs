using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace MarkdownViewer.Lab.Services.Ml;

/// <summary>
/// Options for loading a GGUF model into <see cref="LlamaSharpSynthesizer"/>.
/// </summary>
/// <param name="ModelPath">Absolute path to the GGUF file.</param>
/// <param name="ContextSize">KV-cache context size in tokens. Default 4096.</param>
/// <param name="GpuLayers">GPU offload layers (0 = CPU-only). Default 0.</param>
public sealed record LlamaSharpOptions(
    string ModelPath,
    int ContextSize = 4096,
    int GpuLayers = 0);

/// <summary>
/// Per-call inference options for <see cref="LlamaSharpSynthesizer.SynthesizeAsync"/>.
/// </summary>
/// <param name="MaxTokens">Maximum output tokens. Default 1024.</param>
/// <param name="Temperature">Sampling temperature. Default 0.4f.</param>
public sealed record SynthesisOptions(
    int MaxTokens = 1024,
    float Temperature = 0.4f);

/// <summary>
/// In-process LLM synthesis via LLamaSharp 0.27.0 using the stateless executor.
/// Tokens are streamed as an <see cref="IAsyncEnumerable{T}"/> so callers can
/// display partial output while inference is in flight.
///
/// <para>
/// Thread safety: <see cref="LoadAsync"/> serialises concurrent load requests via
/// a <see cref="SemaphoreSlim"/>. The underlying <see cref="StatelessExecutor"/>
/// creates one <see cref="LLamaContext"/> per inference call, so concurrent
/// <see cref="SynthesizeAsync"/> calls are safe (each gets its own context).
/// </para>
/// </summary>
public sealed class LlamaSharpSynthesizer : IAsyncDisposable
{
    // Guards concurrent LoadAsync calls. Weight loading is expensive and not
    // thread-safe in llama.cpp; serialise it.
    private static readonly SemaphoreSlim _loadGate = new(1, 1);

    private readonly LLamaWeights _weights;
    private readonly ModelParams _contextParams;

    private LlamaSharpSynthesizer(LLamaWeights weights, ModelParams contextParams)
    {
        _weights = weights;
        _contextParams = contextParams;
    }

    /// <summary>
    /// Loads a GGUF model from disk (or throws <see cref="FileNotFoundException"/>
    /// with explicit guidance if the file is missing).
    /// </summary>
    /// <param name="opts">Model path and sizing options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A fully loaded synthesizer ready for inference.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <paramref name="opts"/>.<see cref="LlamaSharpOptions.ModelPath"/> does not
    /// exist on disk. Run <c>--download-model</c> or set the <c>LAB_LLM_PATH</c>
    /// environment variable to a valid GGUF file.
    /// </exception>
    public static async Task<LlamaSharpSynthesizer> LoadAsync(
        LlamaSharpOptions opts,
        CancellationToken ct)
    {
        if (!File.Exists(opts.ModelPath))
            throw new FileNotFoundException(
                $"GGUF model not found at '{opts.ModelPath}'. " +
                "Run --download-model or set LAB_LLM_PATH to an existing .gguf file.",
                opts.ModelPath);

        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var mp = new ModelParams(opts.ModelPath)
            {
                ContextSize = (uint?)opts.ContextSize,
                GpuLayerCount = opts.GpuLayers,
            };

            var weights = await LLamaWeights.LoadFromFileAsync(mp, ct, progressReporter: null)
                .ConfigureAwait(false);

            return new LlamaSharpSynthesizer(weights, mp);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <summary>
    /// Streams generated tokens for <paramref name="prompt"/>.
    /// Each yielded string is a raw token string fragment (not a complete sentence).
    /// </summary>
    /// <param name="prompt">The full prompt text to send to the model.</param>
    /// <param name="o">Per-call inference options.</param>
    /// <param name="ct">Cancellation token propagated into the LLamaSharp inference loop.</param>
    /// <returns>An async sequence of token strings.</returns>
    public async IAsyncEnumerable<string> SynthesizeAsync(
        string prompt,
        SynthesisOptions o,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var executor = new StatelessExecutor(_weights, _contextParams, logger: null);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = o.MaxTokens,
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = o.Temperature },
        };

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct)
                           .ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            yield return token;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _weights.Dispose();
        return ValueTask.CompletedTask;
    }
}
