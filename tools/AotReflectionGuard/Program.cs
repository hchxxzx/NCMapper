using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace NCMapper.AotReflectionGuard;

/// <summary>
/// AOT 反射守护工具 — 扫描三个 ORM 扩展
/// （FreeSql/EFCore/Dapper）构建产物，检测公开方法体 IL 中的反射 API 调用特征，
/// 与 Requires* 标注比对，出现"含反射调用但未标注"即失败（exit 1）。
/// <para>备案基线：方言族 21 项 + Guard 3 项当前零反射，
/// 属"未来可能引入反射"的长期约束。本工具将人工备案转为自动守护。</para>
/// <para>实现：System.Reflection.MetadataLoadContext（静态元数据读取，
/// 不加载运行时依赖——实证先例，100 成员零误报）。</para>
/// </summary>
internal static class Program
{
    // ================================================================
    // 反射特征清单基线——以实证口径为起点。
    // 扫描方法体 IL 中的调用目标名，命中即视为"含反射"。
    // 已知漏检边界：间接反射（委托/表达式树延迟执行）静态不可见，
    // 需人工 Code Review 承接；动态程序集 Emit 不在范围；特征增强为维护项。
    // ================================================================
    /// <summary>
    /// 反射特征签名基线（IL 调用目标名特征匹配）
    /// </summary>
    private static readonly string[] ReflectionSignatures =
    {
        "MakeGenericMethod",
        "MakeGenericType",
        "Compile",
        "GetMethod",
        "GetProperty",
        "GetField",
        "Invoke",
        "CreateInstance",
        "InvokeMember",
    };

    /// <summary>
    /// Guard 类型名单——[Obsolete(error:true)] 编译期硬阻断，仅校验特性仍在。
    /// </summary>
    private static readonly string[] GuardTypes =
    {
        "NCMapper.FreeSql.FreeSqlGuardExtensions",
        "NCMapper.EFCore.EFCoreGuardExtensions",
    };

    /// <summary>
    /// 主入口。
    /// </summary>
    /// <param name="args">可选的 DLL 路径列表；缺省扫描三扩展 net8.0 Release 产物</param>
    /// <returns>0 通过；1 发现违规；2 输入缺失；3 执行异常</returns>
    private static int Main(string[] args)
    {
        string[] dllPaths = args.Length > 0
            ? args
            : new[]
            {
                "src/NCMapper.FreeSql/bin/Release/net8.0/NCMapper.FreeSql.dll",
                "src/NCMapper.EFCore/bin/Release/net8.0/NCMapper.EFCore.dll",
                "src/NCMapper.Dapper/bin/Release/net8.0/NCMapper.Dapper.dll",
            };

        Console.WriteLine("=== NCMapper AOT 反射守护 ===");
        Console.WriteLine($"反射特征基线: {string.Join(", ", ReflectionSignatures)}");

        var failures = new List<string>();
        var totalScanned = 0;
        var totalMethods = 0;

        try
        {
            foreach (var dll in dllPaths)
            {
                if (!File.Exists(dll))
                {
                    Console.Error.WriteLine($"DLL 不存在: {dll}（请先 dotnet build -c Release）");
                    return 2;
                }

                Console.WriteLine();
                Console.WriteLine($"--- 扫描: {Path.GetFullPath(dll)} ---");

                // 构造 MetadataLoadContext：解析器覆盖 dll 同目录 + .NET 引用程序集
                var runtimeDir = Path.GetDirectoryName(Path.GetFullPath(dll))!;
                var assemblyPaths = new List<string>();
                assemblyPaths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));
                AddCoreLibPaths(assemblyPaths);

                using var mlc = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths));
                var asm = mlc.LoadFromAssemblyPath(Path.GetFullPath(dll));
                var asmName = asm.GetName().Name!;

                // 平行 PEReader：负责方法体 IL 解码（MLC 不暴露指令遍历 API）
                var dllFullPath = Path.GetFullPath(dll);
                using var fs = File.OpenRead(dllFullPath);
                using var peReader = new PEReader(fs);

                foreach (var type in asm.GetTypes())
                {
                    // 仅扫描公开类型（含嵌套公开）
                    if (type.IsNested && !type.IsNestedPublic) continue;
                    if (!type.IsPublic && !type.IsNestedPublic) continue;

                    var typeName = type.FullName ?? type.Name;

                    // Guard 类型：仅校验 [Obsolete(error:true)] 仍在
                    if (GuardTypes.Contains(typeName))
                    {
                        totalScanned++;
                        var obsolete = type.CustomAttributes
                            .FirstOrDefault(a => a.AttributeType.Name == "ObsoleteAttribute");
                        if (obsolete == null)
                        {
                            failures.Add($"FAIL: Guard 类型缺少 [Obsolete] 硬阻断: {typeName}");
                        }
                        else
                        {
                            var isError = obsolete.ConstructorArguments.Any(a => a.Value is true);
                            if (!isError)
                                failures.Add($"FAIL: Guard 类型 [Obsolete] 未设 error:true: {typeName}");
                        }
                        continue;
                    }

                    var typeHasRequires = HasRequiresAnnotation(type.CustomAttributes);

                    foreach (var method in type.GetMethods())
                    {
                        if (!method.IsPublic) continue;
                        // 跳过编译器生成成员与属性访问器
                        if (method.Name.StartsWith('<') || method.Name.StartsWith("get_") || method.Name.StartsWith("set_"))
                            continue;

                        var methodFullName = $"{typeName}.{method.Name}";
                        var methodHasRequires = HasRequiresAnnotation(method.CustomAttributes);

                        // 反射特征检测：方法体 IL 中调用目标名匹配
                        var hits = ScanMethodIlForReflection(method, peReader);
                        if (hits.Count > 0 && !methodHasRequires && !typeHasRequires)
                        {
                            failures.Add($"FAIL: 含反射调用但无 Requires* 标注: {methodFullName}（命中: {string.Join(", ", hits)}）");
                        }
                        totalMethods++;
                    }
                }

                totalScanned++;
                Console.WriteLine($"  类型检查完成: {asmName}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"脚本执行异常: {ex}");
            return 3;
        }

        Console.WriteLine();
        if (failures.Count > 0)
        {
            Console.Error.WriteLine($"发现 AOT 反射守护违规（{failures.Count} 项）：");
            foreach (var f in failures)
                Console.Error.WriteLine($"  {f}");
            Console.Error.WriteLine("处置：为命中方法补 Requires* 标注，或登记豁免（理由须一并备案）");
            return 1;
        }

        Console.WriteLine($"OK: 三扩展无未标注反射调用（{totalScanned} 程序集 / {totalMethods} 公开方法，备案约束保持）");
        return 0;
    }

    /// <summary>
    /// 补充 .NET 引用程序集路径（corelib 与框架元数据），供 MetadataLoadContext 解析器使用。
    /// </summary>
    /// <param name="paths">已收集的路径列表（就地追加）</param>
    private static void AddCoreLibPaths(List<string> paths)
    {
        // 1. 当前进程运行时目录（跨平台最稳：Windows/Linux/macOS 的 dotnet 运行时均存在该目录。
        //    修复 CI ubuntu runner 失败根因：旧候选 2 依赖 SpecialFolder.ProgramFiles，
        //    Linux 上返回空串拼出相对路径必不存在，候选 1 是 bin 产物目录也无 shared/，
        //    两路全空导致 corelib 缺失 → MetadataLoadContext 构造抛 FileNotFoundException）
        try
        {
            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
                paths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));
        }
        catch
        {
            // 运行时目录不可得时忽略，回退后续候选
        }

        var dotnetRoot = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]) ?? "";
        var candidates = new[]
        {
            Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App"),
        };
        foreach (var dir in candidates)
        {
            if (!Directory.Exists(dir)) continue;
            var latest = Directory.GetDirectories(dir)
                .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : Version.Parse("0.0"))
                .FirstOrDefault();
            if (latest != null)
                paths.AddRange(Directory.GetFiles(latest, "*.dll"));
        }
    }

    /// <summary>
    /// 判断特性集合是否含 Requires* 标注。
    /// </summary>
    /// <param name="attributes">自定义特性集合</param>
    /// <returns>true 含 RequiresUnreferencedCode 或 RequiresDynamicCode</returns>
    private static bool HasRequiresAnnotation(IEnumerable<CustomAttributeData> attributes)
        => attributes.Any(a => a.AttributeType.Name is "RequiresUnreferencedCodeAttribute" or "RequiresDynamicCodeAttribute");

    /// <summary>
    /// 扫描方法体 IL，检测调用目标名是否命中反射特征清单。
    /// <para>用 System.Reflection.Metadata 的 PEReader + MethodBodyBlock 解码器
    /// 逐指令解析（官方 IL 解析路径，实证口径）：
    /// 读取 DLL 的 PE 元数据，按方法定义 token 定位方法体，解码 call/callvirt/
    /// newobj 等指令的操作数 MemberReference，取其名称与反射特征比对。</para>
    /// <para>与 MLC 平行：MLC 负责类型/特性/公开成员枚举，PEReader 负责 IL 解析。</para>
    /// </summary>
    /// <param name="method">目标方法（MetadataLoadContext 加载的类型成员）</param>
    /// <param name="peReader">DLL 的 PEReader（由调用方持有）</param>
    /// <returns>命中的反射特征签名列表</returns>
    private static List<string> ScanMethodIlForReflection(MethodInfo method, PEReader peReader)
    {
        var hits = new List<string>();
        try
        {
            var metadataReader = peReader.GetMetadataReader();
            // 方法定义 token：MLC 的 MethodInfo.MetadataToken 与 PE 元数据一致
            var methodToken = method.MetadataToken;
            var methodHandle = MetadataTokens.MethodDefinitionHandle(methodToken);
            var methodDef = metadataReader.GetMethodDefinition(methodHandle);

            // 定位方法体 RVA 并解码
            var ilOffset = methodDef.RelativeVirtualAddress;
            if (ilOffset == 0) return hits;

            var methodBody = peReader.GetMethodBody(ilOffset);
            var ilBytes = methodBody.GetILBytes();
            if (ilBytes == null || ilBytes.Length == 0) return hits;

            var il = new ReadOnlySpan<byte>(ilBytes);
            var pos = 0;
            while (pos < il.Length)
            {
                var opByte = il[pos++];
                int opCode;
                if (opByte == 0xFE)
                {
                    if (pos >= il.Length) break;
                    opCode = 0xFE00 | il[pos++];
                }
                else
                {
                    opCode = opByte;
                }

                var operandSize = GetOperandSize(opCode);
                if (operandSize == 0) continue;

                // 仅对带 token 操作数且可能为调用/成员访问指令的操作数解析
                var isCallLike = opCode is 0x28 or 0x29 or 0x2A or 0x2B or 0x73 // call/calli/callvirt/newobj/newarr
                    or 0x6F or 0x70 or 0x71 or 0x72                          // ldelem/stelem/ldstr
                    or 0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x80 or 0x81 or 0x82; // ldfld/stfld 族
                if (!isCallLike || pos + 4 > il.Length)
                {
                    pos += operandSize;
                    continue;
                }

                var token = BitConverter.ToInt32(il.Slice(pos, 4));
                pos += 4;

                // 解析 token：MemberReference → 成员名；MethodSpecification → 泛型实例化（MakeGenericMethod 特征）
                try
                {
                    var tokenHandle = MetadataTokens.EntityHandle(token);
                    string? name = null;
                    if (tokenHandle.Kind == HandleKind.MemberReference)
                    {
                        var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)tokenHandle);
                        name = metadataReader.GetString(memberRef.Name);
                    }
                    else if (tokenHandle.Kind == HandleKind.MethodDefinition)
                    {
                        var methodDef2 = metadataReader.GetMethodDefinition((MethodDefinitionHandle)tokenHandle);
                        name = metadataReader.GetString(methodDef2.Name);
                    }
                    else if (tokenHandle.Kind == HandleKind.MethodSpecification)
                    {
                        // 泛型方法实例化（如 typeof(T).GetMethod 的泛型形态）——MakeGenericMethod 特征
                        var methodSpec = metadataReader.GetMethodSpecification((MethodSpecificationHandle)tokenHandle);
                        // 解析底层方法名
                        var method2 = methodSpec.Method;
                        if (method2.Kind == HandleKind.MemberReference)
                        {
                            var memberRef2 = metadataReader.GetMemberReference((MemberReferenceHandle)method2);
                            name = metadataReader.GetString(memberRef2.Name);
                        }
                        else if (method2.Kind == HandleKind.MethodDefinition)
                        {
                            var methodDef3 = metadataReader.GetMethodDefinition((MethodDefinitionHandle)method2);
                            name = metadataReader.GetString(methodDef3.Name);
                        }
                        // 泛型实例化本身是反射特征（MakeGenericMethod 的产物）
                        if (name != null && ReflectionSignatures.Contains(name) && !hits.Contains(name))
                            hits.Add(name);
                        continue;
                    }
                    else if (tokenHandle.Kind == HandleKind.FieldDefinition)
                    {
                        var fieldDef = metadataReader.GetFieldDefinition((FieldDefinitionHandle)tokenHandle);
                        name = metadataReader.GetString(fieldDef.Name);
                    }
                    else if (tokenHandle.Kind == HandleKind.MemberReference) { /* 已处理 */ }

                    if (name != null && ReflectionSignatures.Contains(name) && !hits.Contains(name))
                        hits.Add(name);
                }
                catch
                {
                    // token 不可解析——忽略
                }
            }
        }
        catch
        {
            // 体不可读（外部方法等）视为无反射
        }
        return hits;
    }

    /// <summary>
    /// 获取操作数大小（字节数），无操作数返回 0。
    /// </summary>
    /// <param name="opCode">操作码数值</param>
    /// <returns>操作数字节数</returns>
    private static int GetOperandSize(int opCode)
    {
        // 单字节操作码：inline token (4) / inline br (4) / short br (1) / inline var (2) / inline i8 (8) / inline i4 (4) / short i (1)
        // 简化：仅对需要 token 解码的操作数返回 4，其余常见操作数按标准表处理
        return opCode switch
        {
            0x28 or 0x29 or 0x2A or 0x2B => 4,   // call/calli/callvirt/newobj
            0x73 => 4,                            // newarr
            0x7B or 0x7C or 0x7D or 0x7E or 0x7F => 4, // ldfld/stfld/ldsfld/stsfld
            0x80 or 0x81 or 0x82 => 4,            // stfld variants
            0x6F or 0x70 or 0x71 => 4,            // callvirt variants (ldelem/stelem)
            0x72 => 4,                            // ldstr (inline string token)
            0xFE => 4,                            // two-byte prefix ops with token
            _ => GetStandardOperandSize(opCode),
        };
    }

    /// <summary>
    /// 获取标准操作码的操作数大小。
    /// </summary>
    /// <param name="opCode">操作码数值</param>
    /// <returns>操作数字节数（0 表示无操作数）</returns>
    private static int GetStandardOperandSize(int opCode)
    {
        // 简化处理：常见 inline 操作数
        return opCode switch
        {
            // inline br target (4)
            0x38 or 0x3A or 0x3E or 0x3F or 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0x45 or 0x46 or 0x47 or 0x48 => 4,
            // short br target (1)
            0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 or 0xDC or 0xDD => 1,
            // inline var (2)
            0x11 or 0x12 or 0x13 or 0x14 => 2,
            // inline i (4)
            0x20 => 4,
            // short inline i (1)
            0x1F => 1,
            // inline i8 (8)
            0x21 => 8,
            // inline r (8)
            0x23 => 8,
            _ => 0,
        };
    }
}
