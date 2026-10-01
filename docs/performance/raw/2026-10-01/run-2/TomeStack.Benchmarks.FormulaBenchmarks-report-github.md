```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3


```
| Method      | Mean      | Error     | StdDev    | Median    | Min       | Max       | P95       | Gen0   | Allocated |
|------------ |----------:|----------:|----------:|----------:|----------:|----------:|----------:|-------:|----------:|
| ParseAll    | 14.552 μs | 0.2837 μs | 0.3484 μs | 14.433 μs | 13.990 μs | 15.498 μs | 15.192 μs | 3.0212 |  55.57 KB |
| EvaluateAll |  2.977 μs | 0.0588 μs | 0.0653 μs |  2.958 μs |  2.892 μs |  3.067 μs |  3.065 μs | 0.6180 |  11.41 KB |
