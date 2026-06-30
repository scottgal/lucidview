using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Ml;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ml;

public class LlamaSharpSynthesizerTests
{
    /// <summary>
    /// Verifies that <see cref="LlamaSharpSynthesizer.LoadAsync"/> throws a typed
    /// <see cref="FileNotFoundException"/> with the canonical guidance message when
    /// the model path does not exist. This runs without any model file on disk.
    /// </summary>
    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFoundWithGuidance()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid()}.gguf");

        var act = async () =>
            await LlamaSharpSynthesizer.LoadAsync(
                new LlamaSharpOptions(missingPath),
                CancellationToken.None);

        await act.Should()
            .ThrowAsync<FileNotFoundException>()
            .WithMessage("*LAB_LLM_PATH*");
    }

    /// <summary>
    /// End-to-end streaming test: loads the model from <c>LAB_LLM_PATH</c>,
    /// synthesises a short completion, and asserts at least one token is returned.
    /// Gated behind <see cref="TestCategories.RequiresLlm"/> so CI skips it.
    /// </summary>
    [Fact]
    [Trait("Category", TestCategories.RequiresLlm)]
    public async Task SynthesizeAsync_ReturnsSomeTokens()
    {
        var path = Environment.GetEnvironmentVariable("LAB_LLM_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return; // env var not set or file missing — skip gracefully

        await using var synth = await LlamaSharpSynthesizer.LoadAsync(
            new LlamaSharpOptions(path, ContextSize: 512),
            CancellationToken.None);

        var sb = new StringBuilder();
        await foreach (var token in synth.SynthesizeAsync(
                           "Say hello.",
                           new SynthesisOptions(MaxTokens: 16),
                           CancellationToken.None))
        {
            sb.Append(token);
        }

        sb.Length.Should().BeGreaterThan(0, "at least one token should be generated");
    }

    /// <summary>
    /// Verifies that cancellation propagates correctly into the inference loop
    /// and results in an <see cref="OperationCanceledException"/> (requires model).
    /// </summary>
    [Fact]
    [Trait("Category", TestCategories.RequiresLlm)]
    public async Task SynthesizeAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var path = Environment.GetEnvironmentVariable("LAB_LLM_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        await using var synth = await LlamaSharpSynthesizer.LoadAsync(
            new LlamaSharpOptions(path, ContextSize: 512),
            CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        var act = async () =>
        {
            await foreach (var _ in synth.SynthesizeAsync(
                               "Generate a very long essay about everything.",
                               new SynthesisOptions(MaxTokens: 2048),
                               cts.Token))
            {
                // consume
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
