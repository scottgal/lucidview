using BenchmarkDotNet.Running;
using Naiad.Benchmarks;

var switcher = BenchmarkSwitcher.FromAssembly(typeof(FlowchartBenchmarks).Assembly);
switcher.Run(args);
