using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NCMapper.SourceGen;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// 注册表冻结断言（步骤 4/5/6 +）——
/// 把的 2 型 union 断言升级为全类型统一断言：Compared ∪ Stripped = 反射全集（未登记新字段即红）+
/// 三列完整性 + 类型级结构断言（容器元素类型集 ⊆ 注册表类型集，闭合登记遗漏）。
/// <para>变异验证见本文件末变异段（注册表层/结构层变异，命名用 Registry 前缀，与既有 EquivalenceMutationTests 比较器层 mutation 不冲突——评审 001 P2-1）。</para>
/// </summary>
public class SnapshotFieldRegistryTests
{
    /// <summary>A-2 计数守门基准：MappingSnapshot 容器元素类型（原始枚举 ∩ Namespace=="NCMapper.SourceGen"）
    /// 去重类型数 = 6（边界实读数：PropertyMappingSnapshot / PropertyRef / DeepCopyNodeShape /
    /// ComposeMappingSnapshot / DiagnosticSnapshot / SubclassPairSnapshot；<c>string</c> 因 Namespace 为 null
    /// 自然豁免）。基准为编制期定案值，不由输入派生——护栏自身不得成为下一个 A-2。</summary>
    private const int ExpectedContainerElementTypeCount = 6;
    // ----------------------------------------------------------------
    // A1 冻结断言：SnapshotFieldRegistry.All 每类型 Compared ∪ Stripped = 反射全集
    // 未登记新字段即红（注册表与类型声明漂移的最强护栏）
    // ----------------------------------------------------------------
    [Theory]
    [InlineData(nameof(MappingSnapshot), typeof(MappingSnapshot))]
    [InlineData(nameof(PropertyRef), typeof(PropertyRef))]
    [InlineData(nameof(PropertyMappingSnapshot), typeof(PropertyMappingSnapshot))]
    [InlineData(nameof(DiagnosticSnapshot), typeof(DiagnosticSnapshot))]
    [InlineData(nameof(DeepCopyNodeShape), typeof(DeepCopyNodeShape))]
    [InlineData(nameof(DeepCopyPropertyShape), typeof(DeepCopyPropertyShape))]
    [InlineData(nameof(ComposeMappingSnapshot), typeof(ComposeMappingSnapshot))]
    // （B-4）：SubclassPairSnapshot 入册——A1 冻结断言第 8 类型
    [InlineData(nameof(SubclassPairSnapshot), typeof(SubclassPairSnapshot))]
    public void AllTypes_ComparedUnionStripped_EqualsReflectionFullSet(string typeName, Type snapshotType)
    {
        var regFields = SnapshotFieldRegistry.All.First(t => t.TypeName == typeName).Fields;
        var registered = regFields.Select(r => r.Field).ToHashSet();

        var reflectionProps = snapshotType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        // 注册表登记项必须全部存在于反射全集（防注册表虚标）
        var extra = registered.Except(reflectionProps).ToList();
        Assert.True(extra.Count == 0, $"{typeName}: 注册表登记了反射不存在的字段 [{string.Join(", ", extra)}]");

        // 顶层 MappingSnapshot 特例（登记口径：Compared = 反射全集 − Stripped 免手列，
        // 与比较器 TopLevelDenyList 行为一致）——本类型节只显式列 Stripped 组与少量 Compared 语义锚点行，
        // 反射全集覆盖不逐字段手列；Stripped 精确集由专测
        // MappingSnapshot_Stripped_HasSixEntries_AfterA25Migration 钉死（A-25 转类后实态）。
        // 旧注曾写"仅登记 Stripped 4 项"并引用一个仓库内不存在的专测名（A-25 迁移后未随修订面更新的
        // 幻影名，002 补读发现 / 001 P2-9），本批按实态订正为上方真实方法名；
        // 顶层比较健康性仍由 A 类等价对 L1 回归反证（评审 002 P1-2——顶层字段不进入分支核对红名单）
        if (typeName == nameof(MappingSnapshot)) return;

        // 反射全集必须全部登记（Compared 或 Stripped）——防漏登记
        var missing = reflectionProps.Except(registered).ToList();
        Assert.True(missing.Count == 0, $"{typeName}: 反射存在但注册表未登记的字段 [{string.Join(", ", missing)}]");
    }

    // ----------------------------------------------------------------
    // A2 三列完整性：Stripped 项理由非空；迁出条件为空须显式"长期/暂无"
    // ----------------------------------------------------------------
    [Fact]
    public void AllStrippedEntries_HaveNonEmptyReasonAndMigrationCondition()
    {
        foreach (var (typeName, fields) in SnapshotFieldRegistry.All)
        {
            var stripped = fields.Where(r => r.Role == FieldRole.Stripped).ToList();
            foreach (var entry in stripped)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Reason),
                    $"{typeName}.{entry.Field}: Stripped 项 Reason 为空");
                Assert.False(string.IsNullOrWhiteSpace(entry.MigrationCondition),
                    $"{typeName}.{entry.Field}: Stripped 项 MigrationCondition 为空");
            }
        }
    }

    // ----------------------------------------------------------------
    // A3 类型级结构断言（A-2 改写后口径——落地 / 注释订正）：
    // 单层判据 = MappingSnapshot 自有容器属性（List / IReadOnlyList / Dictionary / IReadOnlyDictionary）
    // 的元素类型中，Namespace == "NCMapper.SourceGen" 者必须存在于注册表 All 类型集；
    // string / 基元（Namespace 为 null）自然豁免；计数守门基准由 ExpectedContainerElementTypeCount
    // 常量钉死（边界实读数，不由输入派生）。
    // 不覆盖二层容器类型（TypeRef / ConstructorRef / ConstructorParameterRef / EnumMemberRef）——
    // 该族不入 L1 册（native Equals 参与，递归判据首跑大面积红），其域护栏登记另议。
    // 同批副产品：CompareTopLevelField 的 case 分派类型集与本判据同源做集合相等（防新容器类型
    // 落 default 分支走引用相等产假红）。
    // ----------------------------------------------------------------
    [Fact]
    public void ContainerElementTypes_AreSubsetOfRegistryTypes()
    {
        // MappingSnapshot 顶层声明的容器元素类型（递归一层：List<T> / Dictionary<K,V> 元素类型）
        var containerElementTypes = EnumerateContainerElementTypes();

        // A-2 真断言改写——原实现先以 registryTypeNames 过滤
        // 再断言 Contains(elemTypeName, registryTypeNames)：过滤谓词与断言谓词同集，恒真（A-2 实锤）。
        // 新判据 = 单一条件命名空间：凡 Namespace == "NCMapper.SourceGen" 的容器元素类型必须入册；
        // string/基元 Namespace 为 null 自然豁免。真实核查与自证共用 RunContainerElementCheck 纯函数（#38）。
        var failures = RunContainerElementCheck(
            containerElementTypes,
            SnapshotFieldRegistry.All.Select(t => t.TypeName).ToHashSet(),
            ExpectedContainerElementTypeCount);

        Assert.True(failures.Count == 0,
            "A3/A-2 容器元素类型断言 FAILED:\n" + string.Join("\n", failures));
    }

    /// <summary>容器元素类型原始枚举（单层：MappingSnapshot 自有公共实例属性的 List/IReadOnlyList 元素与
    /// Dictionary/IReadOnlyDictionary 值类型；不去重按类型集）。真实核查与自证共用同一枚举入口。</summary>
    private static HashSet<Type> EnumerateContainerElementTypes()
    {
        var containerElementTypes = new HashSet<Type>();
        foreach (var prop in typeof(MappingSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propType = prop.PropertyType;
            // List<T> → T; IReadOnlyList<T> → T; Dictionary<K,V> → V
            if (propType.IsGenericType)
            {
                var genericDef = propType.GetGenericTypeDefinition();
                if (genericDef == typeof(List<>) || genericDef == typeof(IReadOnlyList<>))
                    containerElementTypes.Add(propType.GetGenericArguments()[0]);
                else if (genericDef == typeof(Dictionary<,>) || genericDef == typeof(IReadOnlyDictionary<,>))
                    containerElementTypes.Add(propType.GetGenericArguments()[1]);
            }
        }
        return containerElementTypes;
    }

    /// <summary>
    /// A-2 纯核查（真实核查与自证共用）：
    /// ① 判据——容器元素类型中 <c>Type.Namespace == "NCMapper.SourceGen"</c> 者必须存在于注册表 All 类型集
    /// （命名空间单条件，弃后缀判据——string/基元 Namespace 为 null 自然豁免）；
    /// ② 计数守门——过滤后类型集大小须等于基准（边界实读 6：PropertyMappingSnapshot /
    ///   PropertyRef / DeepCopyNodeShape / ComposeMappingSnapshot / DiagnosticSnapshot / SubclassPairSnapshot）
    ///  ——护栏自身不得成为下一个 A-2（001）。
    /// 判据为**单层**：只枚举 MappingSnapshot 自有容器属性；二层容器元素（TypeRef/ConstructorRef/
    /// ConstructorParameterRef/EnumMemberRef）不入 L1 册（native Equals 参与），域护栏登记。
    /// </summary>
    internal static List<string> RunContainerElementCheck(
        IEnumerable<Type> containerElementTypes,
        ICollection<string> registryTypeNames,
        int expectedElementTypeNameCount)
    {
        var failures = new List<string>();

        var namespaceScoped = containerElementTypes
            .Where(t => t.Namespace == "NCMapper.SourceGen")
            .ToList();

        if (namespaceScoped.Count != expectedElementTypeNameCount)
            failures.Add($"计数守门: 容器元素类型（原始枚举 ∩ Namespace==\"NCMapper.SourceGen\"）= {namespaceScoped.Count} ≠ 基准 {expectedElementTypeNameCount}（边界实读数）——容器属性面或判据已漂移，须同批对账，护栏自身不得成为下一个 A-2");

        var unregistered = namespaceScoped
            .Where(t => !registryTypeNames.Contains(t.Name))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        if (unregistered.Count > 0)
            failures.Add("容器元素类型未入册：" + string.Join(", ", unregistered.Select(t => t.Name)) +
                "——新增 NCMapper.SourceGen 容器元素类型须在 SnapshotFieldRegistry 登记类型节（单层判据；二层容器域护栏另议，边界）");

        return failures;
    }

    /// <summary>A-2 自证：容器枚举副本注入"命名空间命中但未入册"的类型（LocationSnapshot——
    /// NCMapper.SourceGen 单值属性承载类型，不在 L1 册）→ 必红（证伪恒真形态，#38 共用纯函数路径）。</summary>
    [Fact]
    public void SelfProof_A2_ContainerElementTypeMissingFromRegistry_Detected()
    {
        var withFake = EnumerateContainerElementTypes();
        withFake.Add(typeof(LocationSnapshot)); // 模拟：新增 NCMapper.SourceGen 容器元素类型未登记

        var failures = RunContainerElementCheck(
            withFake,
            SnapshotFieldRegistry.All.Select(t => t.TypeName).ToHashSet(),
            ExpectedContainerElementTypeCount);

        Assert.Contains(failures, f => f.Contains("容器元素类型未入册") && f.Contains(nameof(LocationSnapshot)));
        Assert.Contains(failures, f => f.Contains("计数守门"));
    }

    // ----------------------------------------------------------------
    // A3 副产品：顶层 case 分派穷举断言（顺带产出 / 001）——
    // CompareTopLevelField 的 case IReadOnlyList<X> 类型集 == 容器元素类型集（同为 ∩
    // Namespace == "NCMapper.SourceGen" 过滤后集，与 A-2 判据同源，防原始集含 string 致假红）。
    // 新容器类型落 default 分支走引用相等会产假红，本断言把它变成"红且指名道姓"。
    // 解析复用 ComparerFieldCoverageTests 的 internal 读源/解析基建（单点读取机制，003 P2-4）。
    // ----------------------------------------------------------------
    [Fact]
    public void CompareTopLevelField_CaseDispatchTypes_EqualsContainerElementTypes()
    {
        var root = ComparerFieldCoverageTests.ParseSourceWithNoErrors(
            ComparerFieldCoverageTests.ReadRepoRelativeSource(ComparerFieldCoverageTests.ComparerSourcePath));
        var cls = ComparerFieldCoverageTests.LocateClass(root, "SnapshotEquivalenceComparer");
        var method = ComparerFieldCoverageTests.LocateMethod(cls, "CompareTopLevelField")
            ?? throw new InvalidOperationException("CompareTopLevelField 定位失败——顶层分派穷举断言无法执行");

        var failures = RunCaseDispatchCheck(
            ExtractCaseContainerTypeNames(method),
            EnumerateContainerElementTypes()
                .Where(t => t.Namespace == "NCMapper.SourceGen")
                .Select(t => t.Name)
                .ToList());

        Assert.True(failures.Count == 0,
            "顶层 case 分派穷举断言 FAILED:\n" + string.Join("\n", failures));
    }

    /// <summary>从 CompareTopLevelField 方法体提取 <c>case IReadOnlyList&lt;X&gt; …</c> 的 X 类型名集。</summary>
    internal static List<string> ExtractCaseContainerTypeNames(MethodDeclarationSyntax method)
    {
        var names = new List<string>();
        foreach (var label in method.DescendantNodes().OfType<CasePatternSwitchLabelSyntax>())
        {
            if (label.Pattern is DeclarationPatternSyntax decl
                && decl.Type is GenericNameSyntax generic
                && generic.Identifier.Text == "IReadOnlyList"
                && generic.TypeArgumentList.Arguments.Count == 1)
            {
                names.Add(generic.TypeArgumentList.Arguments[0].ToString());
            }
        }
        return names;
    }

    /// <summary>case 分派穷举纯核查（双向集合相等）。</summary>
    internal static List<string> RunCaseDispatchCheck(
        IEnumerable<string> caseTypeNames,
        IEnumerable<string> containerElementTypeNames)
    {
        var cases = new HashSet<string>(caseTypeNames, StringComparer.Ordinal);
        var elements = new HashSet<string>(containerElementTypeNames, StringComparer.Ordinal);

        var onlyCase = cases.Except(elements).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var onlyElement = elements.Except(cases).OrderBy(n => n, StringComparer.Ordinal).ToList();

        var failures = new List<string>();
        if (onlyCase.Count > 0 || onlyElement.Count > 0)
        {
            failures.Add($"CompareTopLevelField case 类型集与容器元素类型集不相等——仅 case 有 [{string.Join(", ", onlyCase)}] / 仅容器有 [{string.Join(", ", onlyElement)}]" +
                "（容器属性新增而未加 case → 落 default 走引用相等产假红；须同批补分派分支）");
        }
        return failures;
    }

    /// <summary>自证：case 副本缺一项 → 穷举断言必红（与真实核查共用纯函数）。</summary>
    [Fact]
    public void SelfProof_A2_CaseDispatchMissingContainerType_Detected()
    {
        var cases = ExtractCaseContainerTypeNames(
            ComparerFieldCoverageTests.LocateMethod(
                ComparerFieldCoverageTests.LocateClass(
                    ComparerFieldCoverageTests.ParseSourceWithNoErrors(
                        ComparerFieldCoverageTests.ReadRepoRelativeSource(ComparerFieldCoverageTests.ComparerSourcePath)),
                    "SnapshotEquivalenceComparer"),
                "CompareTopLevelField")!);

        var elements = EnumerateContainerElementTypes()
            .Where(t => t.Namespace == "NCMapper.SourceGen")
            .Select(t => t.Name)
            .ToList();

        // 内存变异：删掉一个 case 类型（模拟新增容器类型未加分派分支）
        var mutatedCases = cases.Where(c => c != "DiagnosticSnapshot").ToList();

        var failures = RunCaseDispatchCheck(mutatedCases, elements);

        Assert.Contains(failures, f => f.Contains("仅容器有") && f.Contains("DiagnosticSnapshot"));
    }

    // ----------------------------------------------------------------
    // A1 顶层专测：MappingSnapshot Stripped 项 = 6（增 TreeConfig/PropertyNoAutoIncludeTargets、
    // 增 HasComposeMappings——通道性承载，运行时行为断言覆盖；
    // AfterMappingMethodRef 迁出 Stripped；
    // ForInvocationLocation 入册（6→7）；
    // NoAutoInclude 迁出 Stripped（→ A-25 转类，7→6——承载字段
    //   自此参与 L1 字段级比较，方法名按实数订正）
    // ----------------------------------------------------------------
    [Fact]
    public void MappingSnapshot_Stripped_HasSixEntries_AfterA25Migration()
    {
        var stripped = SnapshotFieldRegistry.MappingSnapshot
            .Where(r => r.Role == FieldRole.Stripped)
            .Select(r => r.Field)
            .ToList();

        // ForInvocationLocation 入册（通道固有 Location 剥离）→ 6→7
        // NoAutoInclude 转 Compared → 7→6
        Assert.Equal(6, stripped.Count);
        Assert.Contains("IsFluentForward", stripped);
        Assert.Contains("TargetIdentifierLocation", stripped);
        Assert.Contains("ForInvocationLocation", stripped);
        Assert.Contains("TreeConfig", stripped);
        Assert.Contains("PropertyNoAutoIncludeTargets", stripped);
        Assert.Contains("HasComposeMappings", stripped);
        // 迁移断言：AfterMappingMethodRef 已不在 Stripped 集
        Assert.DoesNotContain("AfterMappingMethodRef", stripped);
        // A-25 转类断言（F5 假绿防线）：NoAutoInclude 已迁出 Stripped 且入 Compared
        Assert.DoesNotContain("NoAutoInclude", stripped);
        var compared = SnapshotFieldRegistry.MappingSnapshot
            .Where(r => r.Role == FieldRole.Compared)
            .Select(r => r.Field)
            .ToList();
        Assert.Contains("NoAutoInclude", compared);
        Assert.Equal(3, compared.Count);   // BeforeMappingMethodRef / AfterMappingMethodRef / NoAutoInclude
    }

    // ----------------------------------------------------------------
    // BeforeMappingMethodRef 显式 Compared 登记（任务书验收 #6——
    // 双侧等价对覆盖）；AfterMappingMethodRef 迁移（Stripped→Compared，
    // 7→6）——双钩子承载字段同为顶层显式 Compared 行
    // ----------------------------------------------------------------
    [Fact]
    public void MappingSnapshot_BothLifecycleHookRefs_RegisteredAsCompared()
    {
        var before = SnapshotFieldRegistry.MappingSnapshot.SingleOrDefault(r => r.Field == "BeforeMappingMethodRef");
        Assert.NotNull(before);
        Assert.Equal(FieldRole.Compared, before!.Role);

        var after = SnapshotFieldRegistry.MappingSnapshot.SingleOrDefault(r => r.Field == "AfterMappingMethodRef");
        Assert.NotNull(after);
        Assert.Equal(FieldRole.Compared, after!.Role);
    }

    // ----------------------------------------------------------------
    // A2 落差声明专测：PropertyRef ConverterBack quartet 定案声明
    // Q3 复审闭合，MigrationCondition 由相对性落差声明
    //（"本方案 E-1 不达成该迁出条件"）换为绝对定案表述（维持剥离 + 实读依据）——
    // 断言同步改为定案标识（依据句 + 复审闭合标记），语义不变：四件套必须携带
    // 迁出评审结论且不可回退为不定表述。
    // ----------------------------------------------------------------
    [Fact]
    public void PropertyRef_ConvertBackQuartet_HasFalloffDeclaration()
    {
        var quartet = new[] { "HasConvertBackUsing", "ConverterBackDeclaringType", "ConverterBackMethodName", "ConverterBackShape" };
        var regFields = SnapshotFieldRegistry.PropertyRef
            .Where(r => quartet.Contains(r.Field) && r.Role == FieldRole.Stripped)
            .ToList();

        Assert.Equal(4, regFields.Count);
        foreach (var entry in regFields)
        {
            // 绝对定案标记（闭合：维持剥离 + 依据 + 复审闭合）
            Assert.Contains("维持剥离", entry.MigrationCondition);
            Assert.Contains("闭合", entry.MigrationCondition);
        }
        // 首行（HasConvertBackUsing）承载完整依据句（含双侧填充链实读结论）
        var first = regFields.Single(r => r.Field == "HasConvertBackUsing");
        Assert.Contains("不可镜像统一", first.MigrationCondition);
        Assert.Contains("MappingExtractor", first.MigrationCondition);
        Assert.Contains("MappingSnapshotBuilder", first.MigrationCondition);
    }

    // ----------------------------------------------------------------
    // A4 跨轴域完整性（2026-10-11 元数据一致性调查定案）：L1 册
    // （SnapshotFieldRegistry）的每个类型，必须在 Equals 册
    // （SnapshotConservationTests.FieldCoverageExpectations，值族 Models.Snapshots.cs 条目）中存在。
    // 两册类型域漂移（一侧加类型另一侧未跟）即红——三册两两一致性网的最后一条类型域边。
    // ----------------------------------------------------------------
    [Fact]
    public void A4_RegistryTypes_AllExistInEqualsCoverageExpectations()
    {
        var failures = RunA4_DomainCompleteness(
            SnapshotFieldRegistry.All.Select(t => t.TypeName),
            EqualsExpectationsValueFamily().Select(e => e.TypeName));

        Assert.True(failures.Count == 0,
            "A4 FAILED:\n" + string.Join("\n", failures));
    }

    // ----------------------------------------------------------------
    // A5 跨轴分类蕴含（2026-10-11 调查定案——单出口方案，无例外通道）：
    // 同类型下 L1 Compared ⊆ EqualsCover，且与 EqualsExcluded / ComputedProps 零交集
    //（矛盾分类即红）。
    // 语义不变量：缓存敏感度 ⊇ 等价比较面——L1 Compared 字段属双通道共享语义契约，
    // 生成管线可能消费；缓存盲字段在源码仅改该字段时不触发增量失效（陈旧发射风险），
    // 而等价门却在比较它，故 Compared × 缓存盲为非法形态。
    // 失败处置二选一（用户 2026-10-11 定案）：① 并入 EqualsCover（缓存敏感化）
    // ② 退出 L1 比较（注册表行改 Stripped 并登记 Reason/MigrationCondition）。
    // 注：本断言不拦"登记齐但漏比较器体"形态（FormatString 类缺陷——比较器两侧
    // 两册登记均正确、仅手写投影缺失），该形态由 B3 比较器体扫描护栏承载，两者不可互替。
    // ----------------------------------------------------------------
    [Fact]
    public void A5_ComparedFields_SubsetOfEqualsCover_NoContradictoryClassification()
    {
        var failures = RunA5_ComparedSubsetAndNoContradiction(
            BuildComparedByType(),
            EqualsExpectationsValueFamily().ToDictionary(e => e.TypeName, StringComparer.Ordinal));

        Assert.True(failures.Count == 0,
            "A5 FAILED:\n" + string.Join("\n", failures));
    }

    // ----------------------------------------------------------------
    // A4/A5 数据源与纯核查逻辑（真实核查与自证共用同一代码路径——#22/#12.3 反假测试纪律）
    // ----------------------------------------------------------------

    /// <summary>Equals 册值族条目（SourceFile == "Models.Snapshots.cs"）。
    /// 符号族（Models.cs：DiagnosticInfo/MappingInfo/PropertyMapping）为生成期模型，
    /// 语义域不同（跨 Compilation 转嫁排除已注释在案），不进蕴含断言域。</summary>
    private static IEnumerable<SnapshotConservationTests.FieldCoverageExpectation> EqualsExpectationsValueFamily()
        => SnapshotConservationTests.FieldCoverageExpectations
            .Where(e => e.SourceFile == "Models.Snapshots.cs");

    /// <summary>L1 Compared per-type 真实数据源（真实核查与自证共用）。
    /// MappingSnapshot 特例：注册表仅显式登记 Stripped 6 + Compared 3，余为
    /// 反射 − denylist 自动 Compared——denylist 复用
    /// <see cref="SnapshotEquivalenceComparer.TopLevelDenyList"/>（已从注册表 Stripped 派生），
    /// 保证断言事实集与比较器实际比较集为同一份定义，不另立第二份推导。</summary>
    internal static Dictionary<string, IReadOnlyList<string>> BuildComparedByType()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (typeName, fields) in SnapshotFieldRegistry.All)
        {
            if (typeName == nameof(MappingSnapshot))
            {
                var deny = SnapshotEquivalenceComparer.TopLevelDenyList;
                result[typeName] = typeof(MappingSnapshot)
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => p.Name)
                    .Where(n => !deny.Contains(n))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
            }
            else
            {
                result[typeName] = fields
                    .Where(r => r.Role == FieldRole.Compared)
                    .Select(r => r.Field)
                    .ToList();
            }
        }
        return result;
    }

    /// <summary>A4 纯核查逻辑：L1 册每个类型必须存在于 Equals 册值族类型域。</summary>
    internal static List<string> RunA4_DomainCompleteness(
        IEnumerable<string> registryTypeNames,
        IEnumerable<string> equalsRegistryTypeNames)
    {
        var equalsTypes = new HashSet<string>(equalsRegistryTypeNames, StringComparer.Ordinal);
        return registryTypeNames
            .Where(t => !equalsTypes.Contains(t))
            .Select(t => $"A4: L1 册类型 '{t}' 不在 Equals 册值族（FieldCoverageExpectations 的 Models.Snapshots.cs 条目）中——两册类型域漂移：SnapshotFieldRegistry 新增类型须同步登记 SnapshotConservationTests.FieldCoverageExpectations")
            .ToList();
    }

    /// <summary>A5 纯核查逻辑：同类型下 Compared ⊆ EqualsCover，且与
    /// EqualsExcluded / ComputedProps 零交集。类型域缺失由 A4 承载，本函数对缺失类型跳过。</summary>
    internal static List<string> RunA5_ComparedSubsetAndNoContradiction(
        IReadOnlyDictionary<string, IReadOnlyList<string>> comparedByType,
        IReadOnlyDictionary<string, SnapshotConservationTests.FieldCoverageExpectation> equalsByType)
    {
        var failures = new List<string>();
        foreach (var (typeName, comparedFields) in comparedByType)
        {
            if (!equalsByType.TryGetValue(typeName, out var exp))
                continue; // 域完整性由 A4 承载

            var cover = new HashSet<string>(exp.EqualsCover, StringComparer.Ordinal);
            var excluded = new HashSet<string>(exp.EqualsExcluded, StringComparer.Ordinal);
            var computed = new HashSet<string>(exp.ComputedProps, StringComparer.Ordinal);

            foreach (var field in comparedFields)
            {
                if (cover.Contains(field))
                    continue; // 合法：参与 L1 比较且缓存敏感

                var disposition =
                    "  处置二选一（单出口，无例外通道——2026-10-11 用户定案）：\n" +
                    $"  ① 并入 EqualsCover（缓存敏感化——{typeName}.Equals/GetHashCode 同步覆盖 + FieldCoverageExpectations 期望登记）\n" +
                    $"  ② 退出 L1 比较（SnapshotFieldRegistry.{typeName} 该字段改 Stripped 并登记 Reason/MigrationCondition）";

                if (excluded.Contains(field))
                    failures.Add($"A5: {typeName}.{field} L1 Compared 但 Equals 册登记为 EqualsExcluded（矛盾分类——缓存盲 + 显式排除并存）\n{disposition}");
                else if (computed.Contains(field))
                    failures.Add($"A5: {typeName}.{field} L1 Compared 但 Equals 册登记为 ComputedProps（矛盾分类——派生冗余字段不应参与 L1 比较）\n{disposition}");
                else
                    failures.Add($"A5: {typeName}.{field} L1 Compared 但不在 EqualsCover（缓存盲区——增量管线对该字段变更不触发失效，等价门却比较它）\n{disposition}");
            }
        }
        return failures;
    }

    // ----------------------------------------------------------------
    // A4/A5 自证（内存变异——与真实核查同一纯函数代码路径；沿
    // EquivalenceRegistryIntegrityTests.SelfProof_* 范式，常驻可执行非注释桩）
    // ----------------------------------------------------------------

    /// <summary>A5 自证 ①：Compared 字段从 EqualsCover 副本移除（模拟缓存盲登记）→ 必红。</summary>
    [Fact]
    public void SelfProof_A5_ComparedFieldMissingFromEqualsCover_Detected()
    {
        // 变异基座：PropertyMappingSnapshot.FormatString（两册今日均正确登记——
        // 移除后即模拟"L1 说比、缓存轴说不敏感"的非法形态）
        var equalsByType = EqualsExpectationsValueFamily().ToDictionary(
            e => e.TypeName,
            e => e.TypeName == nameof(PropertyMappingSnapshot)
                ? e with { EqualsCover = e.EqualsCover.Where(f => f != nameof(PropertyMappingSnapshot.FormatString)).ToArray() }
                : e,
            StringComparer.Ordinal);

        var failures = RunA5_ComparedSubsetAndNoContradiction(BuildComparedByType(), equalsByType);

        Assert.Contains(failures, f => f.Contains(nameof(PropertyMappingSnapshot.FormatString)) && f.Contains("不在 EqualsCover"));
    }

    /// <summary>A5 自证 ②：Compared 字段挪进 EqualsExcluded 副本（矛盾分类）→ 必红。</summary>
    [Fact]
    public void SelfProof_A5_ComparedFieldInEqualsExcluded_Detected()
    {
        var equalsByType = EqualsExpectationsValueFamily().ToDictionary(
            e => e.TypeName,
            e => e.TypeName == nameof(PropertyMappingSnapshot)
                ? e with
                {
                    EqualsCover = e.EqualsCover.Where(f => f != nameof(PropertyMappingSnapshot.FormatString)).ToArray(),
                    EqualsExcluded = e.EqualsExcluded.Append(nameof(PropertyMappingSnapshot.FormatString)).ToArray()
                }
                : e,
            StringComparer.Ordinal);

        var failures = RunA5_ComparedSubsetAndNoContradiction(BuildComparedByType(), equalsByType);

        Assert.Contains(failures, f => f.Contains(nameof(PropertyMappingSnapshot.FormatString)) && f.Contains("EqualsExcluded"));
    }

    /// <summary>A4 自证：Equals 册副本删掉一个交叠类型（模拟两册类型域漂移）→ 必红。</summary>
    [Fact]
    public void SelfProof_A4_RegistryTypeMissingFromEqualsExpectations_Detected()
    {
        // 模拟：Equals 册值族丢失 DiagnosticSnapshot 条目
        var equalsTypes = EqualsExpectationsValueFamily()
            .Select(e => e.TypeName)
            .Where(t => t != nameof(DiagnosticSnapshot));

        var failures = RunA4_DomainCompleteness(
            SnapshotFieldRegistry.All.Select(t => t.TypeName), equalsTypes);

        Assert.Contains(failures, f => f.Contains(nameof(DiagnosticSnapshot)));
    }

    // ----------------------------------------------------------------
    // 变异验证（反向验证）——注册表层/结构层变异
    // 命名用 Registry 前缀（与既有 EquivalenceMutationTests 比较器层 mutation 区分——评审 001 P2-1）
    // 执行期临时启用：取消注释 [Fact] 后跑应红，还原后复绿
    // ----------------------------------------------------------------

    // 变异①：注册表删一字段（登记遗漏）→ 冻结断言红（AllTypes_ComparedUnionStripped_EqualsReflectionFullSet）
    // 预期红：对应类型的 missing 断言失败（"反射存在但注册表未登记的字段 [...]"）
    // [Fact]  // 执行期临时启用
    // public void Mutation1_RegistryFieldRemoved_FreezeAssertionFails()
    // {
    //     // 临时从 SnapshotFieldRegistry.PropertyRef 删一个 Stripped 项（如 HasMapIgnore）
    //     // → AllTypes_ComparedUnionStripped_EqualsReflectionFullSet 的 PropertyRef 行应红
    // }

    // 变异②：Stripped 理由清空 → 完整性红（AllStrippedEntries_HaveNonEmptyReasonAndMigrationCondition）
    // 预期红：对应字段的 Reason 为空断言失败
    // [Fact]
    // public void Mutation2_RegistryReasonCleared_IntegrityAssertionFails()
    // {
    //     // 临时把某 Stripped 项 Reason 设空 → 完整性断言应红
    // }

    // 变异③：共享遍历门改回独立实现 → 等价对/字节不变红（结构性收敛回归）
    // 预期红：侧 A 类等价对 + 字节不变门红
    // 归属：EquivalenceTests.Traversal.cs 变异段（演练），本文件仅登记映射

    // 变异④：删注册表一个嵌套类型 → 类型级结构断言红
    // 预期红：ContainerElementTypes_AreSubsetOfRegistryTypes 双向断言失败
    // [Fact]
    // public void Mutation4_RegistryNestedTypeRemoved_StructureAssertionFails()
    // {
    //     // 临时从 SnapshotFieldRegistry.All 删一个嵌套类型 → 结构断言应红
    // }
}
