```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-MMWLAW : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun   : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3


```
| Method         | Job        | IterationCount | LaunchCount | WarmupCount | Mean      | Error      | StdDev    | Gen0      | Gen1     | Gen2     | Allocated  |
|--------------- |----------- |--------------- |------------ |------------ |----------:|-----------:|----------:|----------:|---------:|---------:|-----------:|
| Customers      | Job-MMWLAW | 10             | Default     | 2           | 128.53 ms |   8.764 ms |  5.797 ms |         - |        - |        - |  177.46 KB |
| ProductRanking | Job-MMWLAW | 10             | Default     | 2           | 174.19 ms |  36.206 ms | 23.948 ms | 1333.3333 | 333.3333 |        - | 9631.79 KB |
| ProductCard    | Job-MMWLAW | 10             | Default     | 2           | 116.44 ms |  10.590 ms |  7.005 ms |         - |        - |        - |  360.41 KB |
| CustomerCard   | Job-MMWLAW | 10             | Default     | 2           |  44.86 ms |  12.923 ms |  7.690 ms |         - |        - |        - |  364.29 KB |
| SupplierCard   | Job-MMWLAW | 10             | Default     | 2           |  65.95 ms |  21.969 ms | 14.531 ms |         - |        - |        - |  354.46 KB |
| Suppliers      | Job-MMWLAW | 10             | Default     | 2           | 159.57 ms |   5.728 ms |  3.409 ms |         - |        - |        - |  114.65 KB |
| Customers      | ShortRun   | 3              | 1           | 3           | 118.69 ms |  67.094 ms |  3.678 ms |         - |        - |        - |  177.51 KB |
| ProductRanking | ShortRun   | 3              | 1           | 3           | 165.53 ms | 212.287 ms | 11.636 ms | 1500.0000 | 750.0000 | 250.0000 | 9621.98 KB |
| ProductCard    | ShortRun   | 3              | 1           | 3           | 125.62 ms |  98.576 ms |  5.403 ms |         - |        - |        - |     363 KB |
| CustomerCard   | ShortRun   | 3              | 1           | 3           |  62.53 ms | 394.092 ms | 21.602 ms |         - |        - |        - |  366.67 KB |
| SupplierCard   | ShortRun   | 3              | 1           | 3           |  40.28 ms | 129.561 ms |  7.102 ms |         - |        - |        - |  367.25 KB |
| Suppliers      | ShortRun   | 3              | 1           | 3           | 149.87 ms |  97.225 ms |  5.329 ms |         - |        - |        - |  114.58 KB |
