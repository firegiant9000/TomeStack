```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3


```
| Method                                           | Mean      | Error     | StdDev    | Median    | Min       | Max       | P95       | Gen0     | Gen1     | Gen2    | Allocated |
|------------------------------------------------- |----------:|----------:|----------:|----------:|----------:|----------:|----------:|---------:|---------:|--------:|----------:|
| Calculate_with_nine_forms_and_sixteen_techniques |  1.172 ms | 0.0077 ms | 0.0060 ms |  1.173 ms |  1.161 ms |  1.182 ms |  1.179 ms |  83.9844 |  23.4375 |       - |   1.48 MB |
| Turn_preview                                     | 16.617 ms | 0.1650 ms | 0.1544 ms | 16.590 ms | 16.417 ms | 16.860 ms | 16.825 ms | 468.7500 | 218.7500 | 15.6250 |   8.02 MB |
| Damage_preview_with_responses                    | 33.119 ms | 0.3142 ms | 0.2785 ms | 33.178 ms | 32.663 ms | 33.674 ms | 33.486 ms | 906.2500 | 437.5000 |       - |   15.8 MB |
