using System;
using Microsoft.ML.OnnxRuntime;

namespace MarkdownViewer.Lab.Services.Ml;

/// <summary>
/// The ONNX execution provider to use for ML inference.
/// Probe walks CUDA -> DirectML -> CoreML -> CPU in priority order.
/// </summary>
public enum ExecutionProvider
{
    Cuda,
    DirectML,
    CoreML,
    Cpu
}

/// <summary>
/// Probes the available ONNX Runtime execution providers at startup and selects the best one.
/// CPU probe always succeeds. Non-CPU probes attempt the AppendExecutionProvider_* call and catch.
/// </summary>
public sealed class ExecutionProviderProbe
{
    public ExecutionProvider Selected { get; }
    public string Reason { get; }

    private ExecutionProviderProbe(ExecutionProvider selected, string reason)
    {
        Selected = selected;
        Reason = reason;
    }

    /// <summary>
    /// Probe available execution providers and return the best match.
    /// </summary>
    /// <param name="requested">
    /// Explicit EP to try first. If null, the platform default order is used:
    /// Linux: Cuda -> Cpu, Windows: DirectML -> Cpu, macOS: CoreML -> Cpu.
    /// </param>
    /// <param name="strict">
    /// When true and <paramref name="requested"/> is not available, throws
    /// <see cref="InvalidOperationException"/> rather than falling through to CPU.
    /// </param>
    /// <returns>A probe result with the selected EP and a reason string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="strict"/> is true and the requested EP is unavailable.
    /// </exception>
    public static ExecutionProviderProbe Probe(ExecutionProvider? requested = null, bool strict = false)
    {
        var order = requested switch
        {
            ExecutionProvider.Cuda     => new[] { ExecutionProvider.Cuda },
            ExecutionProvider.DirectML => new[] { ExecutionProvider.DirectML },
            ExecutionProvider.CoreML   => new[] { ExecutionProvider.CoreML },
            ExecutionProvider.Cpu      => new[] { ExecutionProvider.Cpu },
            null => OperatingSystem.IsLinux()
                        ? new[] { ExecutionProvider.Cuda, ExecutionProvider.Cpu }
                    : OperatingSystem.IsWindows()
                        ? new[] { ExecutionProvider.DirectML, ExecutionProvider.Cpu }
                    : OperatingSystem.IsMacOS()
                        ? new[] { ExecutionProvider.CoreML, ExecutionProvider.Cpu }
                    : new[] { ExecutionProvider.Cpu },
            _ => new[] { ExecutionProvider.Cpu }
        };

        foreach (var ep in order)
        {
            if (TryProbeEp(ep, out var failReason))
                return new ExecutionProviderProbe(ep, ep == ExecutionProvider.Cpu ? "cpu-default" : $"{ep}-available");

            if (strict && requested.HasValue && requested.Value == ep)
                throw new InvalidOperationException($"{ep} not available: {failReason}");
        }

        // Fallthrough: always land on CPU.
        return new ExecutionProviderProbe(ExecutionProvider.Cpu, "fell-through-to-cpu");
    }

    /// <summary>
    /// Attempts to configure a SessionOptions with the given EP.
    /// Returns true on success; false and sets <paramref name="failReason"/> on failure.
    /// CPU always returns true.
    /// </summary>
    private static bool TryProbeEp(ExecutionProvider ep, out string failReason)
    {
        failReason = string.Empty;
        try
        {
            using var opts = new SessionOptions();
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
                    // CPU is always available; no provider call required.
                    break;
            }
            return true;
        }
        catch (Exception ex)
        {
            failReason = ex.Message;
            return false;
        }
    }
}
