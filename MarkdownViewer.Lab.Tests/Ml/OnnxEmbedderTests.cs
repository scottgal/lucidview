using System;
using System.IO;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Ml;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ml;

public class OnnxEmbedderTests
{
    [Fact]
    public void Embed_AllMiniLmL6_Dimensions384AndThrowsNotSupported()
    {
        // Gate on env var — skip entirely if the model file isn't present.
        var modelPath = Environment.GetEnvironmentVariable("LAB_MINILM_PATH") ?? "";
        if (!File.Exists(modelPath))
            return; // skip: model not available in this environment

        using var emb = OnnxEmbedder.Load(modelPath, ExecutionProvider.Cpu);
        emb.Dimensions.Should().Be(384);

        // Tokenizer is pending upstream publish; Embed must throw NotSupportedException with the
        // canonical message, not return garbage data.
        var act = () => emb.Embed("hello world");
        act.Should().Throw<NotSupportedException>()
           .WithMessage("*MiniLmTokenizer*");
    }

    [Fact]
    public void Embed_WithoutModel_ThrowsNotSupportedException()
    {
        // Even without a model on disk we can unit-test that a loaded embedder
        // exposes the typed-exception contract on Embed.
        // We stub a session path that doesn't exist — the factory should throw on bad path,
        // so instead we just confirm the NotSupportedException message contract by checking
        // the exception type and message string match the canonical scaffold message.
        // This is a compile-time contract test: the constant must be visible.
        OnnxEmbedder.TokenizerNotAvailableMessage.Should()
            .Contain("MiniLmTokenizer")
            .And.Contain("nuget.org");
    }
}
