```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.17763.9020/1809/October2018Update/Redstone5)
12th Gen Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX2
  Job-TIBMAN : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX2

IterationCount=15  LaunchCount=5  WarmupCount=3  

```
| Method | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------- |---------:|---------:|---------:|---------:|-------:|----------:|
| Run    | 13.73 ns | 0.480 ns | 1.197 ns | 13.22 ns | 0.0026 |      96 B |
