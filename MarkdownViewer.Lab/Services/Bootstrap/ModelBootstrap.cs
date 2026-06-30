using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Lab.Services.Bootstrap;

/// <summary>
/// Downloads and caches the three model artefacts used by lucidLAB.
/// All methods are idempotent: if the file already exists at
/// <see cref="AppPaths.ModelCacheDir"/> the download is skipped and the
/// existing path is returned immediately.
///
/// <list type="table">
///   <listheader><term>Model</term><description>Source</description></listheader>
///   <item>
///     <term>Embedder</term>
///     <description>all-MiniLM-L6-v2 ONNX from sentence-transformers on HuggingFace</description>
///   </item>
///   <item>
///     <term>NER</term>
///     <description>bert-base-NER ONNX from dslim on HuggingFace</description>
///   </item>
///   <item>
///     <term>LLM</term>
///     <description>Qwen2.5-3B-Instruct Q4_K_M GGUF from Qwen on HuggingFace</description>
///   </item>
/// </list>
/// </summary>
public sealed class ModelBootstrap
{
    private readonly HttpClient _http;

    /// <summary>Initialises the bootstrap with the supplied <see cref="HttpClient"/>.</summary>
    public ModelBootstrap(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Ensures the sentence-embedder ONNX model is present in
    /// <see cref="AppPaths.ModelCacheDir"/> and returns its absolute path.
    /// </summary>
    /// <remarks>
    /// Source: <c>https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx</c>
    /// Local filename: <c>all-MiniLM-L6-v2.onnx</c>
    /// </remarks>
    public Task<string> EnsureEmbedderAsync(CancellationToken ct) =>
        EnsureAsync(
            url: "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx",
            localName: "all-MiniLM-L6-v2.onnx",
            ct);

    /// <summary>
    /// Ensures the NER ONNX model is present in
    /// <see cref="AppPaths.ModelCacheDir"/> and returns its absolute path.
    /// </summary>
    /// <remarks>
    /// Source: <c>https://huggingface.co/dslim/bert-base-NER/resolve/main/onnx/model.onnx</c>
    /// Local filename: <c>bert-base-NER.onnx</c>
    /// </remarks>
    public Task<string> EnsureNerAsync(CancellationToken ct) =>
        EnsureAsync(
            url: "https://huggingface.co/dslim/bert-base-NER/resolve/main/onnx/model.onnx",
            localName: "bert-base-NER.onnx",
            ct);

    /// <summary>
    /// Ensures the LLM GGUF model is present in
    /// <see cref="AppPaths.ModelCacheDir"/> and returns its absolute path.
    /// </summary>
    /// <remarks>
    /// Source: <c>https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf</c>
    /// Local filename: <c>qwen2.5-3b-instruct-q4_k_m.gguf</c>
    /// </remarks>
    public Task<string> EnsureLlmAsync(CancellationToken ct) =>
        EnsureAsync(
            url: "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf",
            localName: "qwen2.5-3b-instruct-q4_k_m.gguf",
            ct);

    private async Task<string> EnsureAsync(string url, string localName, CancellationToken ct)
    {
        var dest = Path.Combine(AppPaths.ModelCacheDir, localName);
        if (File.Exists(dest))
            return dest;

        Directory.CreateDirectory(AppPaths.ModelCacheDir);

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var fs = File.Create(dest);
        await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);

        return dest;
    }
}
