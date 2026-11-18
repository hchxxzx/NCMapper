using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCMapper.SourceGen;

/// <summary>Fluent 属性声明种类</summary>
internal enum FluentDeclarationKind
{
    /// <summary>显式属性映射（Map，2 lambda）</summary>
    Map,

    /// <summary>忽略（Ignore，1 lambda；v1.4 起 dest+Direction 方向重载——Direction 值入声明模型）</summary>
    Ignore,

    /// <summary>脱敏（Mask&lt;TMask,TProp&gt;，1 lambda + TMask）</summary>
    Mask,

    /// <summary>树形映射（MapTree，1 lambda，源侧同名解析）</summary>
    MapTree,

    /// <summary>JSON 字符串展开（FromJson，1 lambda—— 搭车；物化消费 pm.IsFromJson）</summary>
    FromJson,

    /// <summary>格式化（Format，1 lambda + 格式串；物化消费 pm.FormatString，与特性 [MapFormat] 同承载）</summary>
    Format,
}

/// <summary>
/// 解析期单条链快照（符号态——仅 transform 阶段存活，经 MappingSnapshotBuilder 物化后丢弃，不进增量缓存）
/// </summary>
internal sealed class FluentSnapshot
{
    /// <summary>源类型符号（For&lt;S,T&gt; 实参 S）</summary>
    public INamedTypeSymbol SourceType { get; }

    /// <summary>目标类型符号（For&lt;S,T&gt; 实参 T）</summary>
    public INamedTypeSymbol TargetType { get; }

    /// <summary>For invocation 位置（F11 仲裁 Fluent 侧位置）</summary>
    public Microsoft.CodeAnalysis.Location ForLocation { get; }

    /// <summary>
    /// 显式属性声明表（按目标属性名）。 起承载升级为维度分槽合并
    ///（<see cref="FluentPropertyDeclaration"/> 来源/变换/忽略槽；Kind=末写承载——组装层单分派
    /// 消费面不变；同槽重复 F70 / 真冲突 F59 写入前发射——003-P2-1 产物 last-wins 同构）。
    /// </summary>
    public Dictionary<string, FluentPropertyDeclaration> Declarations { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// ConvertBack 反向声明平行表（按目标属性名；，与 Declarations
    /// 分立不合并：合并会破坏"后写覆盖"语义的方向正交性，Convert+ConvertBack 同名合法共存）。
    /// 同属性重复 ConvertBack 后写覆盖（builder 惯例，与 Declarations 同款）。
    /// </summary>
    public Dictionary<string, FluentBackDeclaration> BackDeclarations { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Convert 正向声明平行表（按目标属性名；，与
    /// Declarations/BackDeclarations 分立不合并：同名锚点 Map/ConvertBack/Convert
    /// 各自独立可选、正向转换与显式路径/反向写回正交共存）。同属性重复后写覆盖（builder 惯例）。
    /// </summary>
    public Dictionary<string, FluentForwardDeclaration> ForwardDeclarations { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// FromSource 补充源绑定声明列表（按子源类型 + 目标属性名；，选型 X）。
    /// 与 Declarations/BackDeclarations/ForwardDeclarations 四表分立不合并——FromSource 声明的主源锚定在
    /// For&lt;S,T&gt; 主链、子源经 FromSource&lt;TSub&gt; 声明，物化期填 MappingSnapshot.ComposeMappings
    ///（零映射引擎改动全量复用，G3 字节门）。同子源同 dest 重复 → F29（物化期发射）。
    /// </summary>
    public List<ComposeFluentDeclaration> ComposeDeclarations { get; } = new();

    /// <summary>对级 NoAutoInclude（禁用自动匹配/自动包含）</summary>
    public bool NoAutoInclude { get; set; }

    /// <summary>
    /// 树构建配置三元组—— <c>.Tree()</c> 终端声明；
    /// null = 未调用（opt-in：消费端树挂载位判定恒 false，不得取默认三元组）。
    /// 同链重复调用后写覆盖（对级配置惯例）。
    /// </summary>
    public (string IdProperty, string ParentIdProperty, string ChildrenProperty)? TreeConfig { get; set; }

    /// <summary>
    /// 属性级 NoAutoInclude(dest) 目标属性名集
    /// 与对级 <see cref="NoAutoInclude"/> 分立承载（语义粒度不同：对级=禁自动匹配，属性级=仅导航收集跳过）。
    /// 重复声明幂等（HashSet）。
    /// </summary>
    public System.Collections.Generic.HashSet<string> NoAutoIncludeTargets { get; } = new(System.StringComparer.Ordinal);

    /// <summary>AfterMapping 方法引用（"{ContainingType}.{Name}"；未声明为 null）</summary>
    public string? AfterMappingMethodRef { get; set; }

    /// <summary>
    /// BeforeMapping 方法引用（"{ContainingType}.{Name}"；未声明为 null）——
    /// 镜像 <see cref="AfterMappingMethodRef"/> 形态。
    /// </summary>
    public string? BeforeMappingMethodRef { get; set; }

    /// <summary>
    /// MapWhen 主链条件守卫声明表（按目标属性名—— 单条件，
    /// 重复声明 F44 提取期拒绝）。物化期（MappingSnapshotBuilder.Build）锚点解析消费：
    /// pm 命中 → ConditionMethodRef 注入；落空 → F43；仅 Compose 覆盖 → F45。
    /// </summary>
    public Dictionary<string, FluentWhenDeclaration> WhenDeclarations { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// FromSource 块内 MapWhen 条件守卫声明列表
    /// 锚定 (子源类型, 目标属性名)；物化期注入 ComposePropertyMapping 的条件四字段
    /// （发射落点 = Update_{Sub}_To_{Target} 逐属性赋值层）。
    /// </summary>
    public List<BlockWhenDeclaration> BlockWhenDeclarations { get; } = new();

    /// <summary>
    /// 待解析 MapWhen invocation 列表（第三遍延迟解析
    /// 谓词入参形态判定/源侧类型校验需感知先行 Map src（主链 Declarations / 块内
    /// ComposeDeclarations），链序无关）。链尾统一消费后清空语义（不进增量缓存）。
    /// </summary>
    public List<PendingWhenInvocation> PendingWhenInvocations { get; } = new();

    /// <summary>
    /// 多态子类型对登记列表（对级—— /，仿
    /// ComposeDeclarations 分立承载：一对多集合语义，不与属性级四表合并）。物化期
    /// （MappingSnapshotBuilder.Build）转 SubclassPairMapping → 快照 SubclassPairs；
    /// 完全同对重复 → 物化期 FQN 二元组查表去重（F47—— 诊断接线）。
    /// 符号态不进增量缓存（transform 出口值化，与 ComposeDeclarations 同生命周期）。
    /// </summary>
    public List<FluentSubclassDeclaration> SubclassDeclarations { get; } = new();

    /// <summary>
    /// MapCtorParam 显式构造器形参绑定声明列表（对级
    /// 20260906- /，仿 ComposeDeclarations 集合承载：不与属性级五表合并）。
    /// 形态 1（源 lambda）/ 形态 2（源属性名字符串）统一登记；提取期单点解析源属性符号
    ///（B-1——物化期零重复解析），形态 2 拼错/无公共 getter → F51 提取期发射。
    /// 物化期（MappingSnapshotBuilder.Build）消费： 参数集校验/ 矛盾判定
    ///后建 (形参名→源属性名) 表传 Materialize。符号态不进增量缓存
    ///（transform 出口值化，与 SubclassDeclarations 同生命周期）。
    /// </summary>
    public List<FluentCtorParamBinding> CtorParamBindings { get; } = new();

    public FluentSnapshot(INamedTypeSymbol sourceType, INamedTypeSymbol targetType, Microsoft.CodeAnalysis.Location forLocation)
    {
        SourceType = sourceType;
        TargetType = targetType;
        ForLocation = forLocation;
    }
}

/// <summary>
/// 待解析 MapWhen invocation（第三遍延迟解析载体
/// 主链/块内统一排队；解析在链尾统一执行后即被消费，不进增量缓存）。
/// </summary>
internal sealed class PendingWhenInvocation
{
    /// <summary>MapWhen invocation 节点</summary>
    public InvocationExpressionSyntax Invocation { get; }

    /// <summary>形态判定基准源类型（主链 = fs.SourceType；块内 = 块子源类型）</summary>
    public INamedTypeSymbol SourceTypeForShape { get; }

    /// <summary>是否为 FromSource 块内声明</summary>
    public bool IsBlock { get; }

    /// <summary>块子源类型（IsBlock=true 时非 null）</summary>
    public INamedTypeSymbol? BlockSubSource { get; }

    /// <summary>
    /// 新形态（Member 三段式）段锚 dest——<c>.Member(x).MapWhen(...)</c>
    /// 谓词单参、锚点出 Member 头；null = 旧形态（dest 在 args[0]，解析期取）。
    /// 段锚在 Member 开段时已过 F55/F06 门——第三遍消费免重解析免重复诊断。
    /// </summary>
    public LambdaParseResult? MemberDest { get; }

    public PendingWhenInvocation(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol sourceTypeForShape,
        bool isBlock,
        INamedTypeSymbol? blockSubSource,
        LambdaParseResult? memberDest = null)
    {
        Invocation = invocation;
        SourceTypeForShape = sourceTypeForShape;
        IsBlock = isBlock;
        BlockSubSource = blockSubSource;
        MemberDest = memberDest;
    }
}

/// <summary>
/// 解析期单条多态子类型对登记（符号态——物化后丢弃，不进增量缓存，
/// 与 FluentWhenDeclaration 同生命周期）。物化期（MappingSnapshotBuilder.Build）转
/// SubclassPairMapping → 快照 SubclassPairs（值化 TypeRef 二元组）；完全同对重复 →
/// FQN 二元组查表去重（F47—— 诊断接线）。
/// </summary>
internal sealed class FluentSubclassDeclaration
{
    /// <summary>子类源类型符号（TSrcDerived 泛型实参）</summary>
    public INamedTypeSymbol SourceType { get; }

    /// <summary>子类目标类型符号（TTargetDerived 泛型实参）</summary>
    public INamedTypeSymbol TargetType { get; }

    /// <summary>声明位置（诊断定位——F47 重复登记仲裁）</summary>
    public Microsoft.CodeAnalysis.Location Location { get; }

    public FluentSubclassDeclaration(
        INamedTypeSymbol sourceType,
        INamedTypeSymbol targetType,
        Microsoft.CodeAnalysis.Location location)
    {
        SourceType = sourceType;
        TargetType = targetType;
        Location = location;
    }
}

/// <summary>
/// 解析期单条构造器形参绑定声明（符号态——物化后丢弃，不进增量缓存，
/// 与 FluentSubclassDeclaration 同生命周期）。物化期（MappingSnapshotBuilder.Build）消费：
/// 参数集校验/ 矛盾判定/ 建 (形参名→源属性名) 表传 Materialize。
/// </summary>
internal sealed class FluentCtorParamBinding
{
    /// <summary>目标构造器形参名（MapCtorParam args[0]，原样登记——大小写归一在建表期）</summary>
    public string ParamName { get; }

    /// <summary>源属性符号（提取期解析产物——形态 1 lambda 单段 / 形态 2 FindProperty，B-1 单点）</summary>
    public IPropertySymbol SourceProperty { get; }

    /// <summary>声明位置（F51/F52/F53 诊断定位）</summary>
    public Microsoft.CodeAnalysis.Location Location { get; }

    public FluentCtorParamBinding(string paramName, IPropertySymbol sourceProperty, Microsoft.CodeAnalysis.Location location)
    {
        ParamName = paramName;
        SourceProperty = sourceProperty;
        Location = location;
    }
}
/// <summary>
/// Fluent When 声明 — 条件谓词解析产物（A 形态命名静态方法组 / B 形态内联 lambda 转录）。
/// <para>A 形态（命名静态方法组）：<see cref="MethodRef"/> = "全限定类型::方法名"，
/// <see cref="TranscribedBody"/>/<see cref="TranscribedParamName"/> 为 null；
/// B 形态（内联 lambda 转录）：<see cref="MethodRef"/> 为 null（物化期计算生成类型限定名——
/// 需 (S,T) 对上下文），转录产物在 <see cref="TranscribedBody"/>/<see cref="TranscribedParamName"/>。</para>
/// <para>物化期 Build 消费：锚点解析（F43/F44/F45）+ pm/ComposePropertyMapping 条件四字段注入。</para>
/// </summary>
internal sealed class FluentWhenDeclaration
{
    /// <summary>锚点目标属性名（dest 单段）</summary>
    public string AnchorPropertyName { get; }

    /// <summary>A 形态方法引用（"全限定类型::方法名"；B 形态 null）</summary>
    public string? MethodRef { get; }

    /// <summary>B 形态转录产物（符号级限定名重写后的表达式体；A 形态 null）</summary>
    public string? TranscribedBody { get; }

    /// <summary>B 形态生成方法形参名（转录体内标识符按此名重写；A 形态 null）</summary>
    public string? TranscribedParamName { get; }

    /// <summary>谓词入参形态（T1 字段值 / T2 整源）</summary>
    public ConditionShape Shape { get; }

    /// <summary>声明位置（诊断定位）</summary>
    public Microsoft.CodeAnalysis.Location Location { get; }

    public FluentWhenDeclaration(
        string anchorPropertyName,
        string? methodRef,
        string? transcribedBody,
        string? transcribedParamName,
        ConditionShape shape,
        Microsoft.CodeAnalysis.Location location)
    {
        AnchorPropertyName = anchorPropertyName;
        MethodRef = methodRef;
        TranscribedBody = transcribedBody;
        TranscribedParamName = transcribedParamName;
        Shape = shape;
        Location = location;
    }
}

/// <summary>
/// FromSource 块内 MapWhen 声明（(子源类型, 目标属性名) 锚定）
/// 物化期按 (SubSourceType, TargetPropertyName) 匹配 ComposePropertyMapping 注入条件四字段。
/// </summary>
internal sealed class BlockWhenDeclaration
{
    /// <summary>块子源类型</summary>
    public INamedTypeSymbol SubSourceType { get; }

    /// <summary>锚点目标属性名</summary>
    public string TargetPropertyName { get; }

    /// <summary>守卫声明</summary>
    public FluentWhenDeclaration When { get; }

    public BlockWhenDeclaration(INamedTypeSymbol subSourceType, string targetPropertyName, FluentWhenDeclaration when)
    {
        SubSourceType = subSourceType;
        TargetPropertyName = targetPropertyName;
        When = when;
    }
}

/// <summary>
/// 解析期单属性声明（符号态）
/// </summary>
internal sealed class FluentPropertyDeclaration
{
    /// <summary>声明种类</summary>
    public FluentDeclarationKind Kind { get; }

    /// <summary>源路径段（Map/Convert 显式路径；Ignore/Mask/MapTree/FromJson 为 null——物化时按名自动匹配）</summary>
    public List<PropertyPathSegment>? SrcSegments { get; }

    /// <summary>脱敏实现类型符号（Mask only；泛型约束已保证实现 IMask）</summary>
    public INamedTypeSymbol? MaskType { get; }

    /// <summary>声明位置（诊断定位）</summary>
    public Microsoft.CodeAnalysis.Location Location { get; }

    /// <summary>
    /// 方向性忽略值（Ignore Kind only）
    /// 枚举底层 int（0=Both/1=ToDto/2=ToEntity，与 PropertyRef.MapIgnoreDirection 采集同口径）；
    /// null = 双向（既有 .Ignore(dest) 单参默认语义，additive）
    /// </summary>
    public int? Direction { get; }

    /// <summary>
    /// 豁免门——overrideIgnore 实参（Map/Mask 声明携带；
    /// 物化期置位 Target.HasOverrideIgnore，正反向双链全局忽略豁免）
    /// </summary>
    public bool OverrideIgnore { get; }

    /// <summary>
    /// 格式化串（Format Kind only；物化期经 F64/F65 校验后置位
    /// pm.FormatString，与特性 [MapFormat] 双通道同承载）
    /// </summary>
    public string? FormatString { get; }

    /// <summary>
    /// 变换槽-JSON 展开标志（声明分槽）：FromJson 写入置位；与
    /// Mask/Format 子槽正交共存于同一声明（FromJson×Mask/Format 为 F59 真冲突③④——写入前
    /// 检测点拦截，组合不可达；FromJson×MapTree 不在真冲突清单——标志与 MapTreeDeclared
    /// 可并存， 组合构建消费）。单占场景 Kind=FromJson 即承载（旧面单操作声明
    /// FromJsonDeclared=true 与 Kind 一致——统一读面）。
    /// </summary>
    public bool FromJsonDeclared { get; }

    /// <summary>
    /// 变换槽-树递归标志（声明分槽）：MapTree 写入置位；与
    /// Mask/Format 互斥（F59 真冲突②）。单占场景 Kind=MapTree 即承载（统一读面同上）。
    /// </summary>
    public bool MapTreeDeclared { get; }

    public FluentPropertyDeclaration(
        FluentDeclarationKind kind,
        List<PropertyPathSegment>? srcSegments,
        INamedTypeSymbol? maskType,
        Microsoft.CodeAnalysis.Location location,
        int? direction = null,
        bool overrideIgnore = false,
        string? formatString = null,
        bool fromJsonDeclared = false,
        bool mapTreeDeclared = false)
    {
        Kind = kind;
        SrcSegments = srcSegments;
        MaskType = maskType;
        Location = location;
        Direction = direction;
        OverrideIgnore = overrideIgnore;
        FormatString = formatString;
        FromJsonDeclared = fromJsonDeclared;
        MapTreeDeclared = mapTreeDeclared;
    }
}

/// <summary>
/// FromSource 补充源绑定声明形态。
/// </summary>
internal enum ComposeDeclarationForm
{
    /// <summary>单字段速记（双 lambda object 占位，dest 单段锚点 + src 嵌套纯属性路径）</summary>
    Shorthand,

    /// <summary>分组块（Action&lt;FluentTypeConfig&lt;TSub,T&gt;&gt;，块内=For 受限方法集）</summary>
    Block,
}
