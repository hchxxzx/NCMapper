using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PublicApiGenerator;

// PublicApiDump —— PublicApi 基线（test/NCMapper.Tests/PublicApi.*.approved.txt）再生成器。
//
// 文本口径必须与 test/NCMapper.Tests/PublicApiTests.cs 的 GeneratePublicApi(Assembly) 逐字一致：
//   生成原始文本 → 过滤程序集级 TFM/AssemblyMetadata 行 → '\n' join → TrimEnd
//
// 尾换行（--no-eol）：库内五份基线的末字节形态本就不统一（实测 NCMapper/Dapper/SourceGen 无尾换行，
//   EFCore/FreeSql 有尾换行）。测试断言两侧均先 TrimEnd，故两种形态等价；本开关用于让输出与目标基线
//   **字节全等**（默认写单个尾换行，--no-eol 则不写）。
//
// 用法：PublicApiDump <目标程序集 dll> <输出 txt> [--no-eol]
// 详见同目录 README.md（构建命令、五份基线清单、依赖解析坑与纪律）。

if (args.Length < 2)
{
    Console.Error.WriteLine("用法: PublicApiDump <目标程序集 dll> <输出 txt> [--no-eol]");
    return 2;
}

var noEol = args.Length > 2 && args[2] == "--no-eol";

var asm = Assembly.LoadFrom(args[0]);
var rawApi = ApiGenerator.GeneratePublicApi(asm);
var lines = rawApi.Split('\n').Where(line =>
    !line.StartsWith("[assembly: System.Runtime.Versioning.TargetFramework") &&
    !line.StartsWith("[assembly: System.Reflection.AssemblyMetadata"));
var text = string.Join("\n", lines).TrimEnd();
File.WriteAllText(args[1], noEol ? text : text + "\n", new System.Text.UTF8Encoding(false));
Console.WriteLine("WROTE " + args[1] + " lines=" + (text.Split('\n').Length) + " eol=" + (noEol ? "none" : "LF"));
return 0;
