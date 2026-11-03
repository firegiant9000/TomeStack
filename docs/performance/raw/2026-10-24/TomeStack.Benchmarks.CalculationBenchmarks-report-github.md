```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
Intel Core Ultra 9 275HX 2.70GHz, 1 CPU, 24 logical and 24 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3


```
| Method    | Family    | Mean     | Error     | StdDev    | Median   | Min      | Max      | P95      | Gen0    | Gen1    | Allocated |
|---------- |---------- |---------:|----------:|----------:|---------:|---------:|---------:|---------:|--------:|--------:|----------:|
| **Calculate** | **srd-5.1**   | **1.078 ms** | **0.0186 ms** | **0.0155 ms** | **1.081 ms** | **1.039 ms** | **1.105 ms** | **1.096 ms** | **78.1250** | **18.5547** |   **1.38 MB** |
| **Calculate** | **srd-5.2.1** | **1.094 ms** | **0.0175 ms** | **0.0155 ms** | **1.095 ms** | **1.067 ms** | **1.127 ms** | **1.115 ms** | **81.0547** | **20.5078** |   **1.42 MB** |
