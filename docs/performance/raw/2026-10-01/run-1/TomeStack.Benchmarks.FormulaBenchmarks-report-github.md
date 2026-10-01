```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3


```
| Method      | Mean      | Error     | StdDev    | Median    | Min       | Max       | P95       | Gen0   | Allocated |
|------------ |----------:|----------:|----------:|----------:|----------:|----------:|----------:|-------:|----------:|
| ParseAll    | 12.429 μs | 0.2272 μs | 0.1897 μs | 12.413 μs | 12.100 μs | 12.788 μs | 12.674 μs | 3.0212 |  55.57 KB |
| EvaluateAll |  2.650 μs | 0.0579 μs | 0.1680 μs |  2.629 μs |  2.287 μs |  3.086 μs |  2.940 μs | 0.6218 |  11.41 KB |
