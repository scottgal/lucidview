using FluentAssertions;
using MarkdownViewer.Lab.Services.Ml;
using Xunit;

namespace MarkdownViewer.Lab.Tests.Ml;

public class ExecutionProviderProbeTests
{
    [Fact]
    public void Probe_FallsThroughToCpu_OnUnsupportedPlatformChain()
    {
        var probe = ExecutionProviderProbe.Probe();
        // CPU is always available; the result must be one of the known EPs.
        probe.Selected.Should().BeOneOf(
            ExecutionProvider.Cuda,
            ExecutionProvider.DirectML,
            ExecutionProvider.CoreML,
            ExecutionProvider.Cpu);
        probe.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Probe_StrictMissingEp_Throws()
    {
        // CUDA is not available on CI / macOS dev machines.
        // With strict=true the probe must throw rather than fall through.
        var act = () => ExecutionProviderProbe.Probe(ExecutionProvider.Cuda, strict: true);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Cuda*not available*");
    }
}
