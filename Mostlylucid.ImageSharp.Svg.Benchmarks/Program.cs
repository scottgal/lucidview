using BenchmarkDotNet.Running;
using Mostlylucid.ImageSharp.Svg.Benchmarks;

if (args.Length > 0 && args[0] == "alloc")
{
    return AllocationProfile.Run();
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
