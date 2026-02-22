using BenchmarkDotNet.Running;
using Naiad.Benchmarks.Original;

var switcher = BenchmarkSwitcher.FromAssembly(typeof(DagreOriginalBenchmarks).Assembly);
switcher.Run(args);
