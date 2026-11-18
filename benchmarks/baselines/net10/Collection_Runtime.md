```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.17763.9020/1809/October2018Update/Redstone5)
12th Gen Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX2
  Job-TIBMAN : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX2

IterationCount=15  LaunchCount=5  WarmupCount=3  

```
| Method | Mean     | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Run    | 1.152 μs | 0.0203 μs | 0.0494 μs | 0.2785 | 0.0076 |  10.21 KB |
