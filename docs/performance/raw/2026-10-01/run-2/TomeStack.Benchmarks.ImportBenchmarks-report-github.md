```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-DEVIKB : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  LaunchCount=1  
UnrollFactor=1  WarmupCount=2  

```
| Method  | Mean     | Error     | StdDev    | Median   | Min      | Max      | P95      | Gen0      | Gen1      | Allocated |
|-------- |---------:|----------:|----------:|---------:|---------:|---------:|---------:|----------:|----------:|----------:|
| Preview | 33.07 ms |  0.701 ms |  0.779 ms | 33.06 ms | 32.02 ms | 34.48 ms | 34.36 ms | 3000.0000 | 1000.0000 |  66.89 MB |
| Apply   | 65.73 ms | 12.338 ms | 14.208 ms | 66.51 ms | 51.04 ms | 83.52 ms | 80.66 ms | 3000.0000 | 1000.0000 |  72.32 MB |
