using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Running;

namespace TomeStack.Benchmarks;

/// <summary>
/// Roadmap T4. Run from the repository root, in Release, with no debugger and on AC power:
/// <c>dotnet run -c Release --project benchmarks/TomeStack.Benchmarks -- --filter *</c>. The method and the results are
/// in docs/performance.md. Measure only: a surprising number becomes a follow-up item, not a change here.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        // BenchmarkDotNet finds this project from the current directory; start from the repository root wherever it runs.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TomeStack.slnx")))
            root = root.Parent;
        if (root is not null)
            Directory.SetCurrentDirectory(root.FullName);

        var config = DefaultConfig.Instance
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(StatisticColumn.Median, StatisticColumn.P95, StatisticColumn.Min, StatisticColumn.Max)
            .AddExporter(MarkdownExporter.GitHub, CsvExporter.Default);
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
