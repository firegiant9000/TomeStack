```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-DEVIKB : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  LaunchCount=1  
UnrollFactor=1  WarmupCount=2  

```
| Method  | Mean     | Error    | StdDev   | Median   | Min      | Max      | P95      | Gen0      | Gen1      | Allocated |
|-------- |---------:|---------:|---------:|---------:|---------:|---------:|---------:|----------:|----------:|----------:|
| Preview | 38.53 ms | 2.790 ms | 3.214 ms | 39.39 ms | 34.36 ms | 43.25 ms | 42.89 ms | 4000.0000 | 1000.0000 |  67.28 MB |
| Apply   | 63.66 ms | 5.235 ms | 5.818 ms | 61.39 ms | 57.86 ms | 77.03 ms | 75.16 ms | 4000.0000 | 1000.0000 |  72.72 MB |
