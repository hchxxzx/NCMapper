// ============================================================================
// NCMapper.CrashProbe 崩溃类红测子进程 runner
//
// 用途：在独立进程内触发自引用容器类型的深拷贝分类/拷贝场景。
//   CLR 下 StackOverflowException 不可 catch，xUnit 进程内无法捕获 0xC00000FD，
//   父进程（NCMapper.Tests 的 Red_B01 用例）经 RedTestRunner.Process.Start 启动本程序，
// 以「退出码 + stdout 协议」双校验（契约要点 2）。
//
// 用法：NCMapper.CrashProbe <selfref-list|selfref-dict> <协议输出文件>
//
// 协议（定义的 Key 契约，逐行 Key=Value 写入输出文件，AutoFlush 逐行落盘）
//   classify=<Cyclic|Acyclic|Unknown>  — DeepCopyTypeClassifier.Classify(自引用容器类型) 的结果
//   copyCheck=<OK|FAIL>               — 深拷贝完整性校验（非空 + 元素数一致 + 环回边引用一致）
//
// 修复后期望行为（红测断言口径）：exit 0 + classify=Cyclic + copyCheck=OK。
// 当前基线（e59f1c9）落红形态（留档，执行期实跑证据）：
//   DeepCopyTypeClassifier.CollectEdgeTargets/CollectContainerEdges（:227-231/:235-239/:271-281）
//   容器边内联递归无终止保护 —— Classify 调用自身即栈溢出，进程以 0xC00000FD 崩溃
//   （Windows 下 Process.ExitCode 有符号形态 -1073741571），且崩溃发生在协议输出之前，
//   协议输出文件为空（无任何 Key=Value 行）。父进程断言「exit 0 + 协议双 Key」必然失败 = 预期落红。
//
// 注意 1：DeepCopyTypeClassifier 为 internal 类（Tests 经 InternalsVisibleTo 直访，
//   本工程不经 InternalsVisibleTo —— 为不触碰 src/ 生产代码，此处平移 VerifyReview 的
//   反射调用模式访问分类器）。
// 注意 2：协议经文件而非 stdout 管道输出 —— 实测发现「Process 管道重定向 + 栈溢出崩溃」
//   组合下崩溃进程挂起不退出（CLR 崩溃转储写管道死锁）；文件输出（AutoFlush 逐行）+
//   父进程 CreateNoWindow 不重定向，崩溃退出码可保真传递（0xC00000FD 实证）。
// ============================================================================
using System.Reflection;
using NCMapper;

var exitCode = args.Length switch
{
    2 when args[0] == "selfref-list" => RunSelfRefListCase(args[1]),
    2 when args[0] == "selfref-dict" => RunSelfRefDictCase(args[1]),
    _ => PrintUsage(),
};
return exitCode;

// ---------- 用例 1：class SelfRefList : List<SelfRefList>（自引用列表容器） ----------
// 崩溃锚点：CollectEdgeTargets 的 GetCollectionElementType 分支（:235-239）
//   CollectEdgeTargets(B) → GetCollectionElementType(B)==B → CollectEdgeTargets(B) → 无限递归
//   （执行期实跑崩溃栈实证：DeepCopyTypeClassifier.CollectEdgeTargets 递归栈帧无限重复）
static int RunSelfRefListCase(string outFile)
{
    using var w = new StreamWriter(outFile) { AutoFlush = true };
    var shape = ClassifyReflect(typeof(SelfRefList));
    WriteProtocol(w, "classify", shape);

    var src = new SelfRefList();
    src.Add(src); // 自引用：容器以自身为元素

    var copy = Mapper.DeepCopy(src);
    var ok = copy is not null
             && copy.Count == 1
             && ReferenceEquals(copy[0], copy);
    WriteProtocol(w, "copyCheck", ok ? "OK" : "FAIL");
    return 0;
}

// ---------- 用例 2：class SelfRefDict : Dictionary<string, SelfRefDict>（自引用字典容器） ----------
// 崩溃锚点：CollectEdgeTargets/CollectContainerEdges 的字典 K/V 解包分支（:227-231/:271-275）
//   TryGetDictionaryKeyValueType(SelfRefDict) 经 GetInterfaces 命中 IDictionary<string,SelfRefDict>
//   → K/V 双边中 V=SelfRefDict → CollectEdgeTargets(SelfRefDict) → 同一解包 → 无限递归
static int RunSelfRefDictCase(string outFile)
{
    using var w = new StreamWriter(outFile) { AutoFlush = true };
    var shape = ClassifyReflect(typeof(SelfRefDict));
    WriteProtocol(w, "classify", shape);

    var src = new SelfRefDict();
    src["self"] = src; // 自引用：字典以自身为值

    var copy = Mapper.DeepCopy(src);
    var ok = copy is not null
             && copy.Count == 1
             && copy.TryGetValue("self", out var self)
             && ReferenceEquals(self, copy);
    WriteProtocol(w, "copyCheck", ok ? "OK" : "FAIL");
    return 0;
}

// ---------- 协议输出（AutoFlush 逐行落盘：崩溃场景下已写行保留，未写行即「无协议」证据） ----------
static void WriteProtocol(StreamWriter w, string key, string value)
    => w.WriteLine($"{key}={value}");

// ---------- 反射访问 internal DeepCopyTypeClassifier（不触碰 src/，平移 VerifyReview 模式） ----------
static string ClassifyReflect(Type type)
{
    var asm = typeof(Mapper).Assembly;
    var classifier = asm.GetType("NCMapper.DeepCopyTypeClassifier")
        ?? throw new InvalidOperationException("NCMapper.DeepCopyTypeClassifier 未找到");
    var classify = classifier.GetMethod("Classify", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("DeepCopyTypeClassifier.Classify(Type) 未找到");
    var result = classify.Invoke(null, new object[] { type });
    return result?.ToString() ?? "null";
}

static int PrintUsage()
{
    Console.Error.WriteLine("用法: NCMapper.CrashProbe <selfref-list|selfref-dict> <协议输出文件>");
    return 2;
}

// 自引用容器类型（承载形态）
// 自引用列表：元素类型指向容器自身
sealed class SelfRefList : List<SelfRefList>;

// 自引用字典：值类型指向容器自身（契约要点 2 的 JsonNode 形态）
sealed class SelfRefDict : Dictionary<string, SelfRefDict>;
