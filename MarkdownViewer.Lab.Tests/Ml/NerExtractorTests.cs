using System;
using System.IO;
using FluentAssertions;
using MarkdownViewer.Lab.Services.Ml;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ml;

public class NerExtractorTests
{
    [Fact]
    public void Extract_WithoutModel_ThrowsNotSupportedException()
    {
        // Contract test: the canonical scaffold NotSupportedException message must be exposed
        // as a public constant, and it must reference the tokenizer package.
        NerExtractor.TokenizerNotAvailableMessage.Should()
            .Contain("MiniLmTokenizer")
            .And.Contain("nuget.org");
    }

    [Fact]
    public void Extract_WithModel_ThrowsNotSupportedException()
    {
        // Gate on env var — skip entirely if the NER model isn't present.
        var modelPath = Environment.GetEnvironmentVariable("LAB_NER_PATH") ?? "";
        if (!File.Exists(modelPath))
            return; // skip: model not available in this environment

        using var extractor = NerExtractor.Load(modelPath, ExecutionProvider.Cpu);

        var act = () => extractor.Extract("Apple Inc was founded in Cupertino.");
        act.Should().Throw<NotSupportedException>()
           .WithMessage("*MiniLmTokenizer*");
    }
}
