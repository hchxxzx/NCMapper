using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Check6 —— 公开面<b>签名级</b>覆盖门禁（Fluent 签名面 + 特性属性面 + ORM 终端面 + SQL 方言适配面；
/// 建立两面；计数下限 <c>UNDERCOUNT</c> 由扩展；
/// ORM 终端面由扩面——该面为<b>文本计数下限面</b>不承载覆盖率语义；
/// 方言适配面由入面，粒度为<b>类型级实例化行使</b>）。
/// <para><b>契约</b>：每个公开签名（按形态键区分重载）与每个特性面公开属性，须在测试中<b>至少有一处实际调用点</b>
/// （锚点 = <c>file::method</c>），且其<b>行使方法数不低于台账登记下限</b>。五类红（双向对账 + 计数下限，见类头注）：
/// <list type="bullet">
/// <item><c>UNREGISTERED</c> —— 扫描面 ⊖ 台账（新增公开签名/属性未登台账）；</item>
/// <item><c>STALE</c> —— 台账 ⊖ 扫描面（公开面已撤而台账未清）；</item>
/// <item><c>UNCOVERED</c> —— 台账行锚点为显式未覆盖标记（行在台账、覆盖未落地）；</item>
/// <item><c>ANCHOR</c> —— 锚点文件不存在或方法名未出现在该文件；</item>
/// <item><c>UNDERCOUNT</c> —— 行使该签名的测试方法数低于台账登记下限（计数下限门禁）。</item>
/// </list></para>
///
/// <para><b>形态键（签名级判别的核心）</b>：<c>(方法级类型参数个数, 规范化参数类型序列)</c>。
/// 两者均可从 <see cref="MethodInfo"/> 直接读取，<b>跨编译器稳定</b>，且对 46 重载<b>单射</b>。
/// 早期设计用语义键（T0/T1/T2/T3）依赖<b>调用点实参绑定</b>——反射侧开放泛型形参未解析，
/// 该特征<b>算不出来</b>；且非单射（<c>Convert&lt;TSource&gt;(M)</c> 与 <c>Convert(M)</c> 首参同为 TSource
/// 会折叠成同一键，导致某重载零调用而门禁绿——恰是 B10 要消灭的盲区）。语义标签仅作注记列。</para>
///
/// <para><b>规范化三规则</b>：① 开放泛型形参统一替换为位置占位记号（方法级 <c>!n</c> = 第 n 个方法级类型参数；
/// <b>类级 <c>^n</c> = 第 n 个类级类型参数</b>——类级<b>必须按位置编号</b>，若全部归一为同一记号则
/// <c>ConvertBack(Func&lt;TProp,TEntityProp&gt;)</c> 与 <c>ConvertBack(Func&lt;TTarget,TEntityProp&gt;)</c>
/// 折叠成同键，四重载塌成三）；② <c>bool overrideIgnore = false</c> 等带默认值的可选参数<b>在键中剔除</b>
/// （否则同一重载因调用方式产生两键）；③ 类型名去 <c>`n</c> arity 后缀
/// （与 <see cref="FluentSurfaceIntegrityTests.SimpleTypeName"/> 同一归一函数）。</para>
///
/// <para><b>检出力边界（拦 / 不拦，明写）</b>——
/// <b>拦</b>：新增签名/属性未登台账（UNREGISTERED）；已撤未清（STALE）；台账行锚点仍为未覆盖标记
/// （UNCOVERED）；锚点失效（ANCHOR）；形态键集与扫描面<b>非双射</b>（漏重载折叠 / 台账漏行）；
/// 行使测试方法数低于台账登记下限（UNDERCOUNT）。
/// <b>不拦</b>：锚点对应调用点是否<b>真实行使</b>了该签名（含形态键所指重载）——锚点校验是<b>存在性弱校验</b>；
/// 形态键语义等价性（不同键若行为同构不判）；被测运行时行为质量（断言强度、假绿，由防假测试承担）。</para>
///
/// <para><b>重载级辨异检出力边界（明写）</b>Dapper 11 组重载对
/// （<c>CommandDefinition</c> 形参行 vs <c>String</c> 形参行）已按<b>实参起始形态</b>辨异——CD 行只认
/// <c>Cmd(</c> 辅助调用或 <c>new CommandDefinition</c> 内联，STR 行只认引号字面量（逐字串／raw 三引号），
/// 两组对同一调用点互斥命中（CD∩STR = 0）；<c>QueryMultiple</c> 两行按<b>泛型元数</b>（2 元／3 元）辨异。
/// <b>不拦（已知限制，E6 登记）</b>：<b>标识符实参形态</b>（如 <c>(cmd)</c>／<c>(AllSql)</c>——先
/// <c>new CommandDefinition</c> 赋值给局部变量再传参）两组皆不命中＝不可辨，故每组「真实调用点数 &gt; 可辨计入数」；
/// 不可辨调用点逐条点名 <c>DapperAsyncTerminalTests.cs:62-68</c>（Query）、<c>:270-277</c>／<c>:281-286</c>／
/// <c>:296-343</c>（QueryAsync）。下限按<b>可辨命中数</b>取保守值（零余量），真实调用点数与可辨计入数的对照
/// 登记于台账（<b>本注不写死数字</b>——写死即不可复算）。<c>new CommandDefinition</c> 内联分支为前瞻防御，
/// 当前 0 命中、承重靠 <c>Cmd(</c>。<b>下限下降归因</b>：本册 22 行下限全部低于旧口径——旧口径两组共享全量计数，
/// 新口径按辨异分流各计其一并放过不可辨形态，下降系<b>辨异分流</b>所致而非漏计（GF-T3a：模式与下限同段重算）。
/// <b>X10 未清偿</b>：Fluent 面 9 组同面同名共享模式（<c>FluentTypeConfig.MapCtorParam/Tree</c>、
/// <c>MemberConfig.Convert/ConvertBack/MapWhen</c>、<c>MemberHead.Convert/ConvertBack/Ignore/MapWhen</c>）
/// 属 F8 锁定语义，本册不改其模式，逐组登记于 <see cref="KnownOverloadCollisions"/>；解锁须方案层裁定
/// 重载级辨异口径并重算下限（与 <c>OVERLOAD-SHARED-PATTERN</c> 判据的豁免边界一致）。</para>
///
/// <para><b>计数口径边界（UNDERCOUNT，明写）</b>：计数 = 「行使该签名的测试方法数」，为<b>文本近似口径</b>——
/// 以 Roslyn 语法树界定 <c>[Fact]/[Theory]/[Property]</c> 方法，按台账 <c>CountPattern</c> 命中方法体文本计数
/// （见 <see cref="CoverageCountScanner"/>）；<b>非编译级语义</b>、不辨接收者类型/重载归属；
/// <b>不拦 theory 展开倍率</b>（同一 <c>[Theory]</c> 的多组 <c>InlineData</c> 只计 1）；同名签名行共享模式故计数相同。
/// 门禁<b>只对「相对自身登记下限的下降」负责</b>，不主张绝对精确——台账下限 = <b>各面首跑时点</b>的实测值
/// （ORM 面 80 行 = 本次首跑实测，构造上零余量；既有两面下限为其各自首跑时点值，
/// <b>实测高于下限属正常态</b>，不视为自述矛盾），任何下调须在同 commit 的变更日志登记理由（B2 特化，防静默放宽）。</para>
///
/// <para><b>ORM 终端面（第三面）边界，四要素明写</b>：
/// ① <b>文本近似口径</b>——ORM 终端计数同为测试行使方法数的文本近似值，<b>不承载覆盖率语义</b>
/// （覆盖率仍 observe-only），其作用仅是使「某 ORM 终端的计数下限」对门禁可见；
/// ② <b>同名辨异能力与限制</b>——三扩展终端同名（<c>Single</c>/<c>First</c>/<c>ToPagedList</c> 各一份）
/// 且与 LINQ 同名，缺省 Name 派生模式会把下限灌至 LINQ 同名量级而门禁对该终端<b>实际失效</b>
/// （灌高量级与辨异后的下降由 <see cref="SelfProof15_SameNameTerminals_CountIsolationAndFailureDetection"/>
/// 每次运行实测，<b>本注不写死数字</b>——写死即不可复算）。现以<b>三重辨异</b>压制：
/// (a) CountPattern 携接收者上下文链前缀，且 FreeSql 三形态按<b>类型元数精确</b>区分——
/// <c>Select&lt;T&gt;</c> / <c>Select&lt;A,B&gt;</c> / <c>Select&lt;A,B,C&gt;</c>，段内禁逗号，
/// 故单源模式不吞 compose 调用、compose 模式不吞三源调用；
/// (b) 面级文件过滤器 <see cref="OrmSurfaceFileFilters"/>（限定对应扩展的用例文件子集，口径 = 仓库相对路径正则）；
/// (c) 台账完整性判据 <c>PATTERN-COLLISION</c>（见 <see cref="FindLedgerCompletenessFailures"/>）：
/// <b>不同面的两行不得共享同一 CountPattern</b>——跨面同模式等价于把两行台账登记成一行，
/// 删光其中一面的用例另一面下限仍撑起该行，门禁对该面实际失效（自证 ⑱ 钉红该形态）。
/// 已知限制：链前缀<b>单独</b>不隔离跨面（EFCore 链模式在 FreeSql 调用上同样命中），过滤器为必要第二道；
/// 跨语句的 selector 变量持有形态（<c>var sel = …AsDto(); sel.Query(…)</c>）不被链前缀计入——
/// 下限因此偏保守，本册据此为三源 <c>FirstAsync</c> 补单链用例（<c>T1_07_三表Compose异步FirstAsync_单链形态</c>）
/// 而非按 0 值行排除；
/// ③ <b>不拦 theory 展开</b>（与既有面同口径）；
/// ④ <b>被排除面与替代可见性手段</b>——入口扩展类（<c>NCMapperDapperExtensions</c> 等四类）、
/// guard 面两类不入面，逐条理由见 <see cref="OrmExcludedSurface"/>；其可见性由<b>公开面快照门</b>
/// （<c>PublicApi.*.approved.txt</c> 变更即 diff）与 EFCore guard 的<b>编译期错误</b>
/// （<c>[Obsolete(…, error: true)]</c>）承载；入面类型内若出现行使数为 0 的终端，
/// 按 0 值行闭合规则移入 <see cref="OrmZeroCountExcluded"/>（不得填 0、不得删行留红）；
/// 移入前须<b>换口径复核</b>（放宽过滤器再测），排除理由须是「真零引用」而非「过滤器太窄」。
/// <b>方言适配类自起入面</b>粒度为<b>类型级实例化行使</b>（见
/// <see cref="DialectInSurfaceTypes"/> 与 <see cref="EnumerateDialectSurface"/>）：每具体方言类一行，
/// 计 <c>new XxxDialect(</c> 的行使测试方法数，静态入口 <c>SqlDialectResolver</c> 计
/// <c>SqlDialectResolver.Resolve</c>；抽象基类 <c>LimitOffsetDialect</c> 不可实例化故仍排除。</para>
///
/// <para><b>检出力边界 —— X11 成员级隐形残留（明写）</b>：方言面按<b>类型级</b>入面，故
/// <b>只使「该类被实例化」可见</b>；<c>CountSql</c>／<c>PageSql</c>／<c>ParameterPrefix</c> 等
/// <b>成员级</b>调用点仍不进台账——某方言类被构造但从不调用其分页方法时，本门禁<b>不告警</b>
/// （该维度由方言单元用例的断言强度与公开面快照门承载，不由本计数面承载）。
/// 成因：方言分支<b>显式抑制</b>成员行（否则 <c>MySqlDialect</c>／<c>PostgreSqlDialect</c>／
/// <c>SqliteDialect</c> 零方法声明、<c>Db2Dialect</c>／<c>OracleDialect</c>／<c>SqlServerDialect</c> 的
/// public 方法会被自动产出，与类型级一行重复 → <c>UNREGISTERED</c>）。</para>
///
/// <para><b>锚点校验不区分接收者类型/重载归属</b>：把 <c>MemberConfig.MapTree</c> 行锚到任一含
/// <c>.MapTree(</c> 文本却只调 <c>MemberHead</c> 版的文件，ANCHOR 即通过 → 门禁绿而覆盖为假。
/// 该维度由「缺口行锚点<b>必须挂行为断言型用例</b>」（对生成产物内容或运行时行为的 Assert，
/// 禁挂纯诊断型或仅编译通过型主锚）+ 人工对账承载。</para>
///
/// <para><b>vacuous 护栏</b>：任一扫描面返回 0 条、或计数源返回 0 行即判红——防扫描面路径变更（类型改名 / 反射口径漂移）
/// 致恒绿。</para>
///
/// <para><b>计数源防护面（登记簿 #43 /）</b>门禁类自身文件（本类 + <c>CoverageCountScanner</c>）
/// <b>不参与计数语料</b>——计数源遍历全 <c>test/</c> 树，门禁类既是扫描器调用者又是自证宿主，
/// 其方法体若含台账模式文本即会<b>自灌下限</b>、抵消「调用点减少」这一维检出力。
/// 防护面非空转 / 排除面精确 / 现网零灌入三段判据由
/// <see cref="SelfProof19_SelfFileExclusion_IsLoadBearing"/> 每次运行实测（<b>本注不写死命中数</b>）。</para>
/// </para>
///
/// <para><b>台账首态（已订正）</b>：全量 57 行（Fluent 面 46 + 属性面 11）现<b>全部为实锚</b>——
/// 缺口行已由（Fluent 面 8 行）与（属性面 7 行）补齐，常量
/// <see cref="ExpectedUncoveredRowsAtFirstRun"/> 已归零（0）。<b>历史</b>：建立册首态曾为 15 行
/// （8 签名 + 7 属性）显式未覆盖标记 <see cref="UncoveredMarker"/>，以「先证红」形态承载缺口；
/// 该历史形态已随补齐退役，本段保留以防误读。</para>
///
/// <para><b>Check6 无需 CI 挂载</b>——本类是 xUnit 用例，随 <c>local-ci-check.ps1</c> 的 <c>test</c> 腿
/// 自动执行。</para>
/// </summary>
public class SignatureCoverageIntegrityTests
{
    /// <summary>台账「行已在册、覆盖未落地」的显式标记（不用空串——空串易与「漏填」混淆）。</summary>
    internal const string UncoveredMarker = "UNCOVERED";

    // =================================================================
    // 台账数据面（46 Fluent + 11 属性 = 57 行）
    // =================================================================

    /// <summary>
    /// 签名覆盖台账行：<c>(扫描面, 名称, 形态键, 锚点, 下限, 计数模式)</c>。
    /// <para><c>锚点</c> = <c>file::method</c>，或 <see cref="UncoveredMarker"/> 表示尚未覆盖。</para>
    /// <para><c>下限（MinCount）</c> = 该签名「行使测试方法数」的登记下限，取值 = <b>首跑实测值</b>
    /// （2026-10-13，见下注释块）；<c>计数模式（CountPattern）</c> = 计数扫描所用正则——
    /// Fluent 面缺省派生 <c>\.Name\s*[(<]</c>，属性面（本表 11 行）为特例手写 <c>\bName\s*=</c>。</para>
    /// </summary>
    /// <remarks>
    /// <b>下限取值纪律（2026-10-13 首跑，口径 = <see cref="CoverageCountScanner"/> 方法级文本扫描）</b>：
    /// 下限 = 首跑实测值（零余量设计，保首跑绿）；任何下调须在同 commit 变更日志登记理由（B2 特化）。
    /// 与普查口径（MTP 清单 / 20261013-001 报告）不同族，数值差异属预期——本列为<b>文本近似口径</b>。
    /// </remarks>
    internal static readonly (string Surface, string Name, string ShapeKey, string Anchor, int MinCount, string CountPattern)[] SignatureCoverageRows =
    {
        // ---- Fluent 签名面：FluentMappingConfig（1）----
        ("AfterIgnore", "NoAutoInclude", "0|[]", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberGrammarTests.cs::", 11, @"\.NoAutoInclude\s*[(<]"),
        ("FluentMappingConfig", "For", "2|[]", "test/NCMapper.FuzzTests/FluentRuntimeProperties.cs::FfMapper", 388, @"\.For\s*[(<]"),
        ("FluentTypeConfig", "AfterMapping", "0|[Action<^0,^1>]", "test/NCMapper.Tests/Runtime/FluentRuntimeHooksTests.cs::", 9, @"\.AfterMapping\s*[(<]"),
        ("FluentTypeConfig", "BeforeMapping", "0|[Action<^0,^1>]", "test/NCMapper.Tests/Runtime/FluentRuntimeHooksTests.cs::", 11, @"\.BeforeMapping\s*[(<]"),
        ("FluentTypeConfig", "FromSource", "1|[Action<FluentTypeConfig<!0,^1>>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::", 45, @"\.FromSource\s*[(<]"),
        ("FluentTypeConfig", "MapCtorParam", "0|[String,String]", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::Row1_Shape1_NoPropertyAnchor_BindsSourceValue", 24, @"\.MapCtorParam\s*[(<]"),
        ("FluentTypeConfig", "MapCtorParam", "1|[String,Expression<Func<^0,!0>>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::Row2_Shape2_StringOverload_BindsSourceValue", 24, @"\.MapCtorParam\s*[(<]"),
        ("FluentTypeConfig", "MapSubclass", "2|[]", "test/NCMapper.SourceGen.Tests/MapSubclass/MapSubclassDeclarationTests.cs::", 11, @"\.MapSubclass\s*[(<]"),
        ("FluentTypeConfig", "Member", "1|[Expression<Func<^1,!0>>]", "test/NCMapper.FuzzTests/FluentRuntimeProperties.cs::FfMapper", 345, @"\.Member\s*[(<]"),
        ("FluentTypeConfig", "NoAutoMatch", "0|[]", "test/NCMapper.SourceGen.Tests/Diagnostics/NoAutoMatchDiagnosticsTests.cs::", 8, @"\.NoAutoMatch\s*[(<]"),
        ("FluentTypeConfig", "Tree", "0|[String,String,String]", "test/NCMapper.SourceGen.Tests/Fluent/FluentFamilyCoverageTests.cs::MapTree_WithPairLevelTree_Coexists", 7, @"\.Tree\s*[(<]"),
        ("FluentTypeConfig", "Tree", "0|[]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.TreeConfig.cs::A17_TreeConfig_AttributeMatchesFluent", 7, @"\.Tree\s*[(<]"),
        ("MemberConfig", "Convert", "0|[Func<^2>]", "test/NCMapper.Tests/Runtime/FluentRuntimeConvertTests.cs::Convert_T0_MethodGroup_EndToEnd", 83, @"\.Convert\s*[(<]"),
        ("MemberConfig", "Convert", "1|[Func<!0,^2>]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.MemberCombination.cs::A24_FromPlusConvert_AttributeChannel_Matches_FluentChannel", 83, @"\.Convert\s*[(<]"),
        ("MemberConfig", "Convert", "0|[Func<^0,^2>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberConfigSegmentCoverageTests.cs::Convert_T2_WholeSource_ConfigSegmentEmitsCall", 83, @"\.Convert\s*[(<]"),
        ("MemberConfig", "Convert", "1|[Func<^0,!0,^2>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberConfigSegmentCoverageTests.cs::Convert_T3_TwoInputs_ConfigSegmentEmitsCall", 83, @"\.Convert\s*[(<]"),
        ("MemberConfig", "ConvertBack", "1|[Func<!0>]", "test/NCMapper.Tests/Runtime/FluentRuntimeConfigSegmentConvertBackTests.cs::ConvertBack_T0_ConfigSegment_ConsumedAtRuntime", 52, @"\.ConvertBack\s*[(<]"),
        ("MemberConfig", "ConvertBack", "1|[Func<^1,!0>]", "test/NCMapper.Tests/Runtime/FluentRuntimeConfigSegmentConvertBackTests.cs::ConvertBack_T2_ConfigSegment_ConsumedAtRuntime", 52, @"\.ConvertBack\s*[(<]"),
        ("MemberConfig", "ConvertBack", "1|[Func<^1,^2,!0>]", "test/NCMapper.Tests/Runtime/FluentRuntimeConfigSegmentConvertBackTests.cs::ConvertBack_T3_ConfigSegment_ConsumedAtRuntime", 52, @"\.ConvertBack\s*[(<]"),
        ("MemberConfig", "ConvertBack", "1|[Func<^2,!0>]", "test/NCMapper.Tests/Runtime/FluentRuntimeConfigSegmentConvertBackTests.cs::ConvertBack_T1_ConfigSegment_ConsumedAtRuntime", 52, @"\.ConvertBack\s*[(<]"),
        ("MemberConfig", "Format", "0|[String]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.MemberCombination.cs::A24_FromPlusFormat_AttributeChannel_Matches_FluentChannel", 15, @"\.Format\s*[(<]"),
        ("MemberConfig", "FromJson", "0|[]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.MemberCombination.cs::A24_FromPlusFromJson_AttributeChannel_Matches_FluentChannel", 13, @"\.FromJson\s*[(<]"),
        ("MemberConfig", "MapTree", "0|[]", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberConfigSegmentCoverageTests.cs::MapTree_ConfigSegment_EmitsRecursion", 16, @"\.MapTree\s*[(<]"),
        ("MemberConfig", "MapWhen", "0|[Func<^2,Boolean>]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.MemberCombination.cs::A24_FromPlusMapWhen_AttributeChannel_Matches_FluentChannel", 42, @"\.MapWhen\s*[(<]"),
        ("MemberConfig", "MapWhen", "1|[Func<!0,Boolean>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberFoolProofTests.cs::", 42, @"\.MapWhen\s*[(<]"),
        ("MemberConfig", "Mask", "1|[]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.MemberCombination.cs::A24_FromPlusMask_AttributeChannel_Matches_FluentChannel", 114, @"\.Mask\s*[(<]"),
        ("MemberConfig", "NoAutoInclude", "0|[]", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberConfigSegmentCoverageTests.cs::NoAutoInclude_ConfigSegment_PropertyTargets_RegisteredInSnapshot", 11, @"\.NoAutoInclude\s*[(<]"),
        ("MemberHead", "Convert", "0|[Func<^0,^2>]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.ConvertStatic.cs::", 83, @"\.Convert\s*[(<]"),
        ("MemberHead", "Convert", "0|[Func<^2>]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.ConvertStatic.cs::", 83, @"\.Convert\s*[(<]"),
        ("MemberHead", "Convert", "1|[Func<!0,^2>]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.ConvertStatic.cs::", 83, @"\.Convert\s*[(<]"),
        ("MemberHead", "Convert", "1|[Func<^0,!0,^2>]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.ConvertStatic.cs::", 83, @"\.Convert\s*[(<]"),
        ("MemberHead", "ConvertBack", "1|[Func<!0>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackTests.cs::", 52, @"\.ConvertBack\s*[(<]"),
        ("MemberHead", "ConvertBack", "1|[Func<^1,!0>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackTests.cs::", 52, @"\.ConvertBack\s*[(<]"),
        ("MemberHead", "ConvertBack", "1|[Func<^1,^2,!0>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackTests.cs::", 52, @"\.ConvertBack\s*[(<]"),
        ("MemberHead", "ConvertBack", "1|[Func<^2,!0>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackTests.cs::", 52, @"\.ConvertBack\s*[(<]"),
        ("MemberHead", "Format", "0|[String]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.MapFormat.cs::", 15, @"\.Format\s*[(<]"),
        ("MemberHead", "From", "1|[Expression<Func<^0,!0>>]", "test/NCMapper.FuzzTests/FluentRuntimeProperties.cs::FfMapper", 181, @"\.From\s*[(<]"),
        ("MemberHead", "FromJson", "0|[]", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertTests.cs::", 13, @"\.FromJson\s*[(<]"),
        ("MemberHead", "FromSource", "1|[Expression<Func<!0,Object>>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::", 45, @"\.FromSource\s*[(<]"),
        ("MemberHead", "Ignore", "0|[Direction]", "test/NCMapper.Tests/Runtime/FluentRuntimeIgnoreIndexPathTests.cs::", 35, @"\.Ignore\s*[(<]"),
        ("MemberHead", "Ignore", "0|[]", "test/NCMapper.Tests/Runtime/FluentRuntimeIgnoreIndexPathTests.cs::", 35, @"\.Ignore\s*[(<]"),
        ("MemberHead", "MapTree", "0|[]", "test/NCMapper.SourceGen.Tests/Fluent/FluentFamilyCoverageTests.cs::", 16, @"\.MapTree\s*[(<]"),
        ("MemberHead", "MapWhen", "0|[Func<^2,Boolean>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberCombinationTests.cs::", 42, @"\.MapWhen\s*[(<]"),
        ("MemberHead", "MapWhen", "1|[Func<!0,Boolean>]", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberCombinationTests.cs::", 42, @"\.MapWhen\s*[(<]"),
        ("MemberHead", "Mask", "1|[]", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.Mask.cs::", 114, @"\.Mask\s*[(<]"),
        ("MemberHead", "NoAutoInclude", "0|[]", "test/NCMapper.SourceGen.Tests/Fluent/FluentFamilyCoverageTests.cs::", 11, @"\.NoAutoInclude\s*[(<]"),
        // ---- 属性面：MapDefaultsAttribute（11；计数模式为特例手写——命名实参赋值形态）----
        ("MapDefaultsAttribute", "IgnoreNamePrefixes", "String[]", "test/NCMapper.SourceGen.Tests/Fluent/MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreNamePrefixes_Suppressed", 4, @"\bIgnoreNamePrefixes\s*="),
        ("MapDefaultsAttribute", "IgnoreNamePrefixesToEntity", "String[]", "test/NCMapper.SourceGen.Tests/Fluent/MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreNamePrefixesToEntity_ForwardStillMaps", 2, @"\bIgnoreNamePrefixesToEntity\s*="),
        ("MapDefaultsAttribute", "IgnoreNameSuffixes", "String[]", "test/NCMapper.SourceGen.Tests/Fluent/MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreNameSuffixes_Suppressed", 3, @"\bIgnoreNameSuffixes\s*="),
        ("MapDefaultsAttribute", "IgnoreNameSuffixesToEntity", "String[]", "test/NCMapper.SourceGen.Tests/Fluent/MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreNameSuffixesToEntity_ForwardStillMaps", 2, @"\bIgnoreNameSuffixesToEntity\s*="),
        ("MapDefaultsAttribute", "IgnoreNames", "String[]", "test/NCMapper.SourceGen.Tests/GlobalConfigReaderTests.cs::", 46, @"\bIgnoreNames\s*="),
        ("MapDefaultsAttribute", "IgnoreNamesToEntity", "String[]", "test/NCMapper.SourceGen.Tests/ReverseDirectCallGenTests.cs::", 7, @"\bIgnoreNamesToEntity\s*="),
        ("MapDefaultsAttribute", "IgnoreTypes", "Type[]", "test/NCMapper.SourceGen.Tests/GlobalConfigReaderTests.cs::", 2, @"\bIgnoreTypes\s*="),
        ("MapDefaultsAttribute", "IgnoreTypesToEntity", "Type[]", "test/NCMapper.SourceGen.Tests/Fluent/MapDefaultsAttributePathCoverageTests.cs::AttrPath_IgnoreTypesToEntity_ForwardStillMaps", 2, @"\bIgnoreTypesToEntity\s*="),
        ("MapDefaultsAttribute", "NamingStrategy", "NamingStrategy", "test/NCMapper.SourceGen.Tests/MapDefaultsFluentInteractionTests.cs::", 30, @"\bNamingStrategy\s*="),
        ("MapDefaultsAttribute", "Postfixes", "String[]", "test/NCMapper.SourceGen.Tests/Fluent/MapDefaultsAttributePathCoverageTests.cs::AttrPath_Postfixes_StrippedSuffix_Matches", 6, @"\bPostfixes\s*="),
        ("MapDefaultsAttribute", "Prefixes", "String[]", "test/NCMapper.SourceGen.Tests/Fluent/MapDefaultsAttributePathCoverageTests.cs::AttrPath_Prefixes_StrippedPrefix_Matches", 12, @"\bPrefixes\s*="),
        // ---- ORM 终端面：扫描面与台账同源于公开面快照；MinCount = 本面首跑实测值（构造上零余量）。
        //      CountPattern 携接收者上下文链前缀，且 FreeSql 三形态按类型元数精确区分（段内禁逗号）：
        //        Dapper   = AsDto<T>()…
        //        EFCore   = AsDto()…
        //        FreeSql  = Select<T>()…AsDto()… / Select<A,B>()…AsDto()… / Select<A,B,C>()…AsDto()…
        //      叠加面级文件过滤器 OrmSurfaceFileFilters（链前缀单独不足以隔离跨面，过滤器为第二道防线）。
        //      跨面两行不得共享同一 CountPattern（PATTERN-COLLISION 判据，自证 ⑱）——见类头注边界②。
        //  Dapper 11 组重载级辨异：CD 行（CommandDefinition 形参）与 STR 行
        //   （String 形参）模式互斥——CD 行只认实参起始 `Cmd(` 辅助调用或 `new CommandDefinition` 内联，
        //   STR 行只认实参起始引号（逐字串 @?"" 或 raw 三引号）。两组对同一调用点不同时命中（CD∩STR=0）。
        //   `new CommandDefinition` 内联分支为前瞻防御（实测 0 命中，承重靠 `Cmd(`，见类头注检出力边界）；
        //   标识符实参形态（如 (cmd)/(AllSql)）两组皆不命中＝不可辨，按保守下限登记（E6）。
        //   QueryMultiple 两行按泛型元数辨异（2 元 / 3 元），非 CD/STR 轴。下限 = 辨异后首跑实测（零余量）。
        ("DapperDtoSelector", "Query", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::Query_ConvertUsing_Applied", 2, @"AsDto<[^>]*>[^;]*\.Query\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "Query", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::Query_ConvertUsing_Applied", 10, @"AsDto<[^>]*>[^;]*\.Query\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QueryAsync", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryAsync_EmptyResult_ReturnsEmpty", 1, @"AsDto<[^>]*>[^;]*\.QueryAsync\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QueryAsync", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryAsync_EmptyResult_ReturnsEmpty", 3, @"AsDto<[^>]*>[^;]*\.QueryAsync\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QueryFirst", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryFirst_NoResult_Throws", 2, @"AsDto<[^>]*>[^;]*\.QueryFirst\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QueryFirst", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryFirst_NoResult_Throws", 2, @"AsDto<[^>]*>[^;]*\.QueryFirst\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QueryFirstAsync", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryFirstAsync_NoResult_Throws", 2, @"AsDto<[^>]*>[^;]*\.QueryFirstAsync\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QueryFirstAsync", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryFirstAsync_NoResult_Throws", 2, @"AsDto<[^>]*>[^;]*\.QueryFirstAsync\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QueryFirstOrDefault", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryFirstOrDefault_Found_Returns", 2, @"AsDto<[^>]*>[^;]*\.QueryFirstOrDefault\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QueryFirstOrDefault", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryFirstOrDefault_Found_Returns", 2, @"AsDto<[^>]*>[^;]*\.QueryFirstOrDefault\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QueryFirstOrDefaultAsync", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryFirstOrDefaultAsync_Found_Returns", 2, @"AsDto<[^>]*>[^;]*\.QueryFirstOrDefaultAsync\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QueryFirstOrDefaultAsync", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QueryFirstOrDefaultAsync_Found_Returns", 2, @"AsDto<[^>]*>[^;]*\.QueryFirstOrDefaultAsync\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QueryMultiple", "2|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoMultiResultTests.cs::QueryMultiple_FirstResultSet_Empty_ReturnsEmptyList", 5, @"AsDto<[^>]*>[^;]*\.QueryMultiple\s*<[^>,]*,[^>,]*>\s*\("),
        ("DapperDtoSelector", "QueryMultiple", "3|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoMultiResultTests.cs::QueryMultiple_FirstResultSet_Empty_ReturnsEmptyList", 2, @"AsDto<[^>]*>[^;]*\.QueryMultiple\s*<[^>,]*,[^>,]*,[^>,]*>\s*\("),
        ("DapperDtoSelector", "QuerySingle", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QuerySingle_ExactlyOne_Returns", 1, @"AsDto<[^>]*>[^;]*\.QuerySingle\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QuerySingle", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QuerySingle_ExactlyOne_Returns", 3, @"AsDto<[^>]*>[^;]*\.QuerySingle\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QuerySingleAsync", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QuerySingleAsync_Multiple_Throws", 1, @"AsDto<[^>]*>[^;]*\.QuerySingleAsync\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QuerySingleAsync", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QuerySingleAsync_Multiple_Throws", 3, @"AsDto<[^>]*>[^;]*\.QuerySingleAsync\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QuerySingleOrDefault", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QuerySingleOrDefault_Found_Returns", 1, @"AsDto<[^>]*>[^;]*\.QuerySingleOrDefault\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QuerySingleOrDefault", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QuerySingleOrDefault_Found_Returns", 3, @"AsDto<[^>]*>[^;]*\.QuerySingleOrDefault\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QuerySingleOrDefaultAsync", "1|[CommandDefinition]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QuerySingleOrDefaultAsync_Found_Returns", 1, @"AsDto<[^>]*>[^;]*\.QuerySingleOrDefaultAsync\s*<[^>]*>\s*\(\s*(?:Cmd\s*\(|new\s+CommandDefinition)"),
        ("DapperDtoSelector", "QuerySingleOrDefaultAsync", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::QuerySingleOrDefaultAsync_Found_Returns", 3, @"AsDto<[^>]*>[^;]*\.QuerySingleOrDefaultAsync\s*<[^>]*>\s*\(\s*(?:@?""|"""""""")"),
        ("DapperDtoSelector", "QueryUnbufferedAsync", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsyncTerminalTests.cs::QueryUnbufferedAsync_BreakMidEnumeration_ReleasesReader", 4, @"AsDto<[^>]*>[^;]*\.QueryUnbufferedAsync\s*[(<]"),
        ("DapperDtoSelector", "ToChunkDto", "1|[String,Object,Int32,Action<List<!0>>]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::ToChunkDto_DoneThrowsException_Propagates", 4, @"AsDto<[^>]*>[^;]*\.ToChunkDto\s*[(<]"),
        ("DapperDtoSelector", "ToChunkDtoAsync", "1|[String,Object,Int32,Func<List<!0>,Task>]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::ToChunkDtoAsync_DoneThrowsException_Propagates", 3, @"AsDto<[^>]*>[^;]*\.ToChunkDtoAsync\s*[(<]"),
        ("DapperDtoSelector", "ToDictionary", "2|[String,Object,Func<^0,!0>]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::ToDictionary_DuplicateKey_Throws", 4, @"AsDto<[^>]*>[^;]*\.ToDictionary\s*[(<]"),
        ("DapperDtoSelector", "ToDictionaryAsync", "2|[String,Object,Func<^0,!0>]", "test/NCMapper.IntegrationTests/DapperAsDtoTerminalTests.cs::ToDictionaryAsync_Works", 1, @"AsDto<[^>]*>[^;]*\.ToDictionaryAsync\s*[(<]"),
        ("DapperDtoSelector", "ToLookup", "2|[String,Object,Func<^0,!0>]", "test/NCMapper.IntegrationTests/DapperAsDtoTerminalTests.cs::ToLookup_ByAgeGroup_CorrectGroups", 2, @"AsDto<[^>]*>[^;]*\.ToLookup\s*[(<]"),
        ("DapperDtoSelector", "ToLookupAsync", "2|[String,Object,Func<^0,!0>]", "test/NCMapper.IntegrationTests/DapperAsDtoTerminalTests.cs::ToLookupAsync_Works", 1, @"AsDto<[^>]*>[^;]*\.ToLookupAsync\s*[(<]"),
        ("DapperDtoSelector", "ToPagedList", "1|[String,Object,Int32,Int32]", "test/NCMapper.IntegrationTests/DapperAsDtoTerminalTests.cs::AsDto_Trace_PrefixCoversQueryTreePagingPaths", 10, @"AsDto<[^>]*>[^;]*\.ToPagedList\s*[(<]"),
        ("DapperDtoSelector", "ToPagedListAsync", "1|[String,Object,Int32,Int32]", "test/NCMapper.IntegrationTests/DapperAsDtoTerminalTests.cs::ToPagedListAsync_PageSize_Zero_Throws", 3, @"AsDto<[^>]*>[^;]*\.ToPagedListAsync\s*[(<]"),
        ("DapperDtoSelector", "ToTreeList", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoTerminalTests.cs::AsDto_Trace_PrefixCoversQueryTreePagingPaths", 5, @"AsDto<[^>]*>[^;]*\.ToTreeList\s*[(<]"),
        ("DapperDtoSelector", "ToTreeListAsync", "1|[String]", "test/NCMapper.IntegrationTests/DapperAsDtoTerminalTests.cs::ToTreeListAsync_Works", 1, @"AsDto<[^>]*>[^;]*\.ToTreeListAsync\s*[(<]"),
        ("EFCoreDtoSelector", "AsTracking", "0|[]", "test/NCMapper.IntegrationTests/EFCoreDegradationBoundaryTests.cs::AsTracking_Explicit_Keeps_Tracking", 1, @"AsDto\(\)[^;]*\.AsTracking\s*[(<]"),
        ("EFCoreDtoSelector", "First", "1|[]", "test/NCMapper.IntegrationTests/EFCoreProjectionTests.cs::EFCore_AsDto_First_MultiTable_Works", 4, @"AsDto\(\)[^;]*\.First\s*[(<]"),
        ("EFCoreDtoSelector", "FirstAsync", "1|[]", "test/NCMapper.IntegrationTests/EFCoreProjectionTests.cs::EFCore_AsDto_FirstAsync_Works", 1, @"AsDto\(\)[^;]*\.FirstAsync\s*[(<]"),
        ("EFCoreDtoSelector", "Single", "1|[]", "test/NCMapper.IntegrationTests/EFCoreSingleProjectionTests.cs::Single_多条记录_抛InvalidOperationException", 4, @"AsDto\(\)[^;]*\.Single\s*[(<]"),
        ("EFCoreDtoSelector", "SingleAsync", "1|[]", "test/NCMapper.IntegrationTests/EFCoreSingleProjectionTests.cs::SingleAsync_正常单条映射正确", 2, @"AsDto\(\)[^;]*\.SingleAsync\s*[(<]"),
        ("EFCoreDtoSelector", "ToChunkDto", "1|[Int32,Action<List<!0>>]", "test/NCMapper.IntegrationTests/EFCoreChunkDtoTests.cs::T002_投影路径_Size为1", 6, @"AsDto\(\)[^;]*\.ToChunkDto\s*[(<]"),
        ("EFCoreDtoSelector", "ToChunkDtoAsync", "1|[Int32,Func<List<!0>,Task>]", "test/NCMapper.IntegrationTests/EFCoreChunkDtoTests.cs::T003_CancellationToken取消_不再触发回调", 5, @"AsDto\(\)[^;]*\.ToChunkDtoAsync\s*[(<]"),
        ("EFCoreDtoSelector", "ToDictionary", "2|[Func<^0,!0>]", "test/NCMapper.IntegrationTests/EFCoreDictionaryProjectionTests.cs::ToDictionary_基础字典映射正确", 4, @"AsDto\(\)[^;]*\.ToDictionary\s*[(<]"),
        ("EFCoreDtoSelector", "ToDictionaryAsync", "2|[Func<^0,!0>]", "test/NCMapper.IntegrationTests/EFCoreDictionaryProjectionTests.cs::ToDictionaryAsync_异步字典映射正确", 2, @"AsDto\(\)[^;]*\.ToDictionaryAsync\s*[(<]"),
        ("EFCoreDtoSelector", "ToList", "1|[]", "test/NCMapper.IntegrationTests/DualEngineProjectionEquivalenceTests.cs::CollectionProjection_EFCore_Equals_FreeSql", 30, @"AsDto\(\)[^;]*\.ToList\s*[(<]"),
        ("EFCoreDtoSelector", "ToListAsync", "1|[]", "test/NCMapper.IntegrationTests/EFCoreChunkDtoTests.cs::T003_投影路径_异步与ToListAsync一致性", 4, @"AsDto\(\)[^;]*\.ToListAsync\s*[(<]"),
        ("EFCoreDtoSelector", "ToLookup", "2|[Func<^0,!0>]", "test/NCMapper.IntegrationTests/EFCoreLookupProjectionTests.cs::ToLookup_基础分组映射正确", 4, @"AsDto\(\)[^;]*\.ToLookup\s*[(<]"),
        ("EFCoreDtoSelector", "ToPagedList", "1|[Int32,Int32]", "test/NCMapper.IntegrationTests/EFCoreProjectionTests.cs::EFCore_ToPagedListAsync_SameAsSync", 4, @"AsDto\(\)[^;]*\.ToPagedList\s*[(<]"),
        ("EFCoreDtoSelector", "ToPagedListAsync", "1|[Int32,Int32]", "test/NCMapper.IntegrationTests/EFCoreProjectionTests.cs::EFCore_ToPagedListAsync_SameAsSync", 1, @"AsDto\(\)[^;]*\.ToPagedListAsync\s*[(<]"),
        ("EFCoreDtoSelector", "ToTreeList", "1|[]", "test/NCMapper.IntegrationTests/DapperEfCoreTreeConsistencyTests.cs::Dapper_EFCore_同Dto同数据_嵌套结果一致", 14, @"AsDto\(\)[^;]*\.ToTreeList\s*[(<]"),
        ("EFCoreDtoSelector", "ToTreeListAsync", "1|[]", "test/NCMapper.IntegrationTests/EFCoreTreeTests.cs::ToTreeListAsync_Empty_返回空列表", 2, @"AsDto\(\)[^;]*\.ToTreeListAsync\s*[(<]"),
        ("FreeSqlComposeDtoSelector", "First", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlComposeNullFilterTests.cs::NT002_双表降级_First命中orphan返回null且Trace告知", 1, @"Select<[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.First\s*[(<]"),
        ("FreeSqlComposeDtoSelector", "FirstAsync", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlComposeProjectionTests.cs::T1_06_双表Compose异步降级", 1, @"Select<[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.FirstAsync\s*[(<]"),
        ("FreeSqlComposeDtoSelector", "ToChunkDto", "1|[Int32,Action<List<!0>>]", "test/NCMapper.IntegrationTests/FreeSqlChunkDtoCompletenessTests.cs::T001_双表ToChunkDto_与ToList一致性", 5, @"Select<[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToChunkDto\s*[(<]"),
        ("FreeSqlComposeDtoSelector", "ToChunkDtoAsync", "1|[Int32,Func<List<!0>,Task>]", "test/NCMapper.IntegrationTests/FreeSqlChunkAsyncTests.cs::Compose双表_ToChunkDtoAsync_已取消令牌抛OCE", 3, @"Select<[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToChunkDtoAsync\s*[(<]"),
        ("FreeSqlComposeDtoSelector", "ToList", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlChunkDtoCompletenessTests.cs::T001_双表ToChunkDto_与ToList一致性", 12, @"Select<[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToList\s*[(<]"),
        ("FreeSqlComposeDtoSelector", "ToListAsync", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlComposeNullFilterTests.cs::NT002_双表降级_ToListAsync_null过滤", 1, @"Select<[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToListAsync\s*[(<]"),
        ("FreeSqlComposeDtoSelector", "ToPagedList", "1|[Int32,Int32]", "test/NCMapper.IntegrationTests/FreeSqlComposePagingTests.cs::NT004_双表投影分页_第一页切片与TotalCount正确", 5, @"Select<[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToPagedList\s*[(<]"),
        ("FreeSqlComposeDtoSelector", "ToPagedListAsync", "1|[Int32,Int32]", "test/NCMapper.IntegrationTests/FreeSqlComposePagingTests.cs::NT004_双表异步分页_投影路径与同步一致", 1, @"Select<[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToPagedListAsync\s*[(<]"),
        ("FreeSqlComposeDtoSelector3", "First", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlComposeNullFilterTests.cs::NT002_三表降级_First命中orphan返回null且Trace告知", 1, @"Select<[^>,]*,[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.First\s*[(<]"),
        ("FreeSqlComposeDtoSelector3", "FirstAsync", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlComposeProjectionTests.cs::T1_07_三表Compose异步FirstAsync_单链形态", 1, @"Select<[^>,]*,[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.FirstAsync\s*[(<]"),
        ("FreeSqlComposeDtoSelector3", "ToChunkDto", "1|[Int32,Action<List<!0>>]", "test/NCMapper.IntegrationTests/FreeSqlChunkDtoCompletenessTests.cs::T001_三表ToChunkDto_与ToList一致性", 5, @"Select<[^>,]*,[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToChunkDto\s*[(<]"),
        ("FreeSqlComposeDtoSelector3", "ToChunkDtoAsync", "1|[Int32,Func<List<!0>,Task>]", "test/NCMapper.IntegrationTests/FreeSqlChunkAsyncTests.cs::Compose三表_ToChunkDtoAsync_投影路径", 2, @"Select<[^>,]*,[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToChunkDtoAsync\s*[(<]"),
        ("FreeSqlComposeDtoSelector3", "ToList", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlChunkDtoCompletenessTests.cs::T001_三表ToChunkDto_与ToList一致性", 5, @"Select<[^>,]*,[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToList\s*[(<]"),
        ("FreeSqlComposeDtoSelector3", "ToListAsync", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlComposeNullFilterTests.cs::NT002_三表降级_ToListAsync_null过滤", 1, @"Select<[^>,]*,[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToListAsync\s*[(<]"),
        ("FreeSqlComposeDtoSelector3", "ToPagedList", "1|[Int32,Int32]", "test/NCMapper.IntegrationTests/FreeSqlComposePagingTests.cs::NT004_三表投影分页_切片与三表字段正确", 2, @"Select<[^>,]*,[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToPagedList\s*[(<]"),
        ("FreeSqlComposeDtoSelector3", "ToPagedListAsync", "1|[Int32,Int32]", "test/NCMapper.IntegrationTests/FreeSqlComposePagingTests.cs::NT004_三表异步分页_降级路径与同步一致", 1, @"Select<[^>,]*,[^>,]*,[^>,]*>[^;]*AsDto[^;]*\.ToPagedListAsync\s*[(<]"),
        ("FreeSqlDtoSelector", "First", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlAutoIncludeTests.cs::T1_09_AutoInclude_On_LoadsNestedNavigation", 9, @"Select<[^>,]*>[^;]*AsDto[^;]*\.First\s*[(<]"),
        ("FreeSqlDtoSelector", "FirstAsync", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlProjectionTests.cs::FreeSql_AsDto_FirstAsync_Works", 1, @"Select<[^>,]*>[^;]*AsDto[^;]*\.FirstAsync\s*[(<]"),
        ("FreeSqlDtoSelector", "Single", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlSingleContractTests.cs::Single_投影腿_单条_返回映射值", 6, @"Select<[^>,]*>[^;]*AsDto[^;]*\.Single\s*[(<]"),
        ("FreeSqlDtoSelector", "SingleAsync", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlSingleContractTests.cs::SingleAsync_投影腿_单条_返回映射值", 6, @"Select<[^>,]*>[^;]*AsDto[^;]*\.SingleAsync\s*[(<]"),
        ("FreeSqlDtoSelector", "ToChunkDto", "1|[Int32,Action<List<!0>>]", "test/NCMapper.IntegrationTests/FreeSqlChunkDtoCompletenessTests.cs::ToChunkDto_NormalPath_IncludeNavigations", 10, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToChunkDto\s*[(<]"),
        ("FreeSqlDtoSelector", "ToChunkDtoAsync", "1|[Int32,Func<List<!0>,Task>]", "test/NCMapper.IntegrationTests/FreeSqlChunkAsyncTests.cs::ToChunkDtoAsync_已取消令牌_首页取数前抛OCE", 6, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToChunkDtoAsync\s*[(<]"),
        ("FreeSqlDtoSelector", "ToDictionary", "2|[Func<^0,!0>]", "test/NCMapper.IntegrationTests/FreeSqlDictionaryProjectionTests.cs::ToDictionary_基础字典映射正确", 4, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToDictionary\s*[(<]"),
        ("FreeSqlDtoSelector", "ToDictionaryAsync", "2|[Func<^0,!0>]", "test/NCMapper.IntegrationTests/FreeSqlDictionaryProjectionTests.cs::ToDictionaryAsync_异步字典映射正确", 2, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToDictionaryAsync\s*[(<]"),
        ("FreeSqlDtoSelector", "ToList", "1|[]", "test/NCMapper.IntegrationTests/EFCoreNavAndFreeSqlIncludeTests.cs::MapNoAutoInclude_Property_NotIncluded_IsEmpty", 39, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToList\s*[(<]"),
        ("FreeSqlDtoSelector", "ToListAsync", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlEdgeCaseTests.cs::T3_03_大量数据烟雾测试", 2, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToListAsync\s*[(<]"),
        ("FreeSqlDtoSelector", "ToLookup", "2|[Func<^0,!0>]", "test/NCMapper.IntegrationTests/FreeSqlChunkAsyncTests.cs::ToLookup_分组映射正确", 2, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToLookup\s*[(<]"),
        ("FreeSqlDtoSelector", "ToPagedList", "1|[Int32,Int32]", "test/NCMapper.IntegrationTests/FreeSqlProjectionTests.cs::FreeSql_ToPagedListAsync_SameAsSync", 10, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToPagedList\s*[(<]"),
        ("FreeSqlDtoSelector", "ToPagedListAsync", "1|[Int32,Int32]", "test/NCMapper.IntegrationTests/FreeSqlProjectionTests.cs::FreeSql_ToPagedListAsync_SameAsSync", 1, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToPagedListAsync\s*[(<]"),
        ("FreeSqlDtoSelector", "ToTreeList", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlTreeNameMismatchTests.cs::Q8_1_同名通过_三层树结构完整", 14, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToTreeList\s*[(<]"),
        ("FreeSqlDtoSelector", "ToTreeListAsync", "1|[]", "test/NCMapper.IntegrationTests/FreeSqlTreeNameMismatchTests.cs::Q8_3_异步路径_异名同样抛异常", 3, @"Select<[^>,]*>[^;]*AsDto[^;]*\.ToTreeListAsync\s*[(<]"),
        // ---- SQL 方言适配面：**类型级实例化行使**（入面）。
        //      行键串与 CountPattern 均由 EnumerateDialectSurface + DialectCountPattern 机械产出后粘贴（B12），
        //  非手写；MinCount = 本面首跑实测值（构造上零余量，逐行见）。
        //      粒度 = 每具体方言类一行 ctor 行使（Surface = 类型简名、Name = ctor、ShapeKey = 0|[]），
        //      静态入口 SqlDialectResolver 一行 Resolve。抽象 LimitOffsetDialect 不入面（见 OrmExcludedSurface，X5）。
        //      成员级 CountSql/PageSql/ParameterPrefix 调用点**不入面**（X11 隐形残留，见类头注「检出力边界」）。
        ("Db2Dialect", "ctor", "0|[]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::OtherDialects_ParameterPrefix_IsAt", 5, DialectCountPattern("Db2Dialect")),
        ("MySqlDialect", "ctor", "0|[]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::OtherDialects_ParameterPrefix_IsAt", 2, DialectCountPattern("MySqlDialect")),
        ("OracleDialect", "ctor", "0|[]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::OracleDialect_ParameterPrefix_IsColon", 3, DialectCountPattern("OracleDialect")),
        ("PostgreSqlDialect", "ctor", "0|[]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::OtherDialects_ParameterPrefix_IsAt", 2, DialectCountPattern("PostgreSqlDialect")),
        ("SqlServerDialect", "ctor", "0|[]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::AsDto_ExplicitDialect_Overrides", 9, DialectCountPattern("SqlServerDialect")),
        ("SqliteDialect", "ctor", "0|[]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::OtherDialects_ParameterPrefix_IsAt", 2, DialectCountPattern("SqliteDialect")),
        ("SqlDialectResolver", "Resolve", "0|[]", "test/NCMapper.IntegrationTests/DapperAsDtoQueryTests.cs::Resolve_UnwrappedConnection_BehavesAsBefore", 5, DialectCountPattern("SqlDialectResolver")),
    };

    // 计数守门基准（非自证派生——护栏自身不得成为下一个「自证恒绿」载体）。
    private const int ExpectedFluentSignatureRows = 46;
    private const int ExpectedAttributeRows = 11;
    private const int ExpectedOrmTerminalRows = 80;

    /// <summary>
    /// 方言适配面台账行数基准——取值 = <see cref="EnumerateDialectSurface"/> 的<b>实产行数</b>
    /// （每具体方言类一行构造行使 ＋ 静态入口 <c>SqlDialectResolver.Resolve</c> 一行）。
    /// <para><b>不得预设</b>：本值由 <see cref="SignatureCoverage_CountGate_And_ShapeKeyBijection"/>
    /// 与 <see cref="DialectSurfaceCensus_EveryDialectTypeProducesExactlyOneRow"/> 双向核；
    /// 与扫描面实产不一致即红，防手写行数与枚举器漂移（B12）。</para>
    /// </summary>
    private const int ExpectedDialectRows = 7;

    private const int ExpectedTotalRows = ExpectedFluentSignatureRows + ExpectedAttributeRows + ExpectedOrmTerminalRows + ExpectedDialectRows;

    /// <summary>本册缺口行数（建立册首态 8 签名 + 7 属性；补齐后已归零）——台账首态 UNCOVERED 行数基准。</summary>
    private const int ExpectedUncoveredRowsAtFirstRun = 0;

    private static readonly string[] FluentConfigTypeNames =
    {
        "NCMapper.FluentMappingConfig",
        "NCMapper.FluentTypeConfig`2",
        "NCMapper.MemberHead`3",
        "NCMapper.MemberConfig`3",
        "NCMapper.AfterIgnore`2",
    };

    /// <summary>属性面本册覆盖的属性类型（<c>MapDefaultsAttribute</c> 的 11 个公开实例属性）。</summary>
    private static readonly string[] AttributeTypeNames = { "NCMapper.Attributes.MapDefaultsAttribute" };

    // =================================================================
    // 真实核查
    // =================================================================

    /// <summary>五类红双向对账 + 计数下限 + vacuous 护栏。</summary>
    [Fact]
    public void SignatureCoverage_AllRows_ResolvesNoFailure()
    {
        var counts = CoverageCountScanner.CountForRows(
            FindRepoRoot(),
            SignatureCoverageRows.Select(r => (r.Surface, r.Name, r.ShapeKey, r.CountPattern)),
            OrmSurfaceFileFilters);

        var failures = RunSignatureCoverageCheck(
            AllSurfaces(),
            SignatureCoverageRows,
            counts);

        Assert.True(failures.Count == 0,
            "Check6 签名级覆盖门禁 FAILED:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// 台账完整性判据（下限 ≥1、CountPattern 非空、跨面模式不重复）——<b>纯函数</b>，
    /// 既被 <see cref="SignatureCoverage_MinCounts_AllFilledAndPositive"/> 消费，也被自证 ⑰ 消费
    /// （自证必须共用被测判据函数本体，不得在自证内重写判据表达式）。
    /// </summary>
    internal static List<string> FindLedgerCompletenessFailures(
        IEnumerable<(string Surface, string Name, string ShapeKey, string Anchor, int MinCount, string CountPattern)> rows)
    {
        var list = rows.ToList();
        var failures = new List<string>();

        foreach (var r in list.Where(r => r.MinCount < 1))
            failures.Add($"UNDERCOUNT: 下限缺填/非正 → {r.Surface}.{r.Name}@{r.ShapeKey} = {r.MinCount}");
        foreach (var r in list.Where(r => string.IsNullOrWhiteSpace(r.CountPattern)))
            failures.Add($"PATTERN: CountPattern 缺填 → {r.Surface}.{r.Name}@{r.ShapeKey}");

        // 跨面同模式 = 两行台账实为一行（同一批调用点同时撑起两个面的下限）→ 门禁对其中一面实际失效。
        // 同面内同名重载行共享模式属既有口径（类头注「同名签名行共享模式故计数相同」），不在此判红。
        // 既有 Fluent 面的 9 组跨类同名模式（MemberHead/MemberConfig/AfterIgnore/FluentTypeConfig）
        // 由 F8 锁定语义不得改（本册不动既有 57 行），逐条登记于 KnownCrossSurfaceCollisions；
        // **新增**碰撞（含 ORM 面）一律判红。
        var byPattern = list
            .Where(r => !string.IsNullOrWhiteSpace(r.CountPattern))
            .GroupBy(r => r.CountPattern, StringComparer.Ordinal);
        foreach (var g in byPattern)
        {
            if (KnownCrossSurfaceCollisions.Contains(g.Key, StringComparer.Ordinal)) continue;
            var surfaces = g.Select(r => r.Surface).Distinct(StringComparer.Ordinal).ToList();
            if (surfaces.Count > 1)
                failures.Add($"PATTERN-COLLISION: 跨面共享同一 CountPattern（跨面串计，F6）→ {g.Key} 命中面 [{string.Join(", ", surfaces)}]");
        }

        // 重载级辨异：同面内若两行共享同一 CountPattern，等价于把两行台账
        // 登记成一行——删光其中一形态的用例，另一形态下限仍撑起两行，门禁对该重载实际失效。
        // 与 PATTERN-COLLISION 的分工：后者只发射跨面（surfaces>1）碰撞，同面共享由其放过、本判据接管。
        // 作用域＝本册新增/改动行（Dapper 11 组）；既有 Fluent 9 组（F8 锁定面，本册不改其模式）走
        // KnownOverloadCollisions 豁免，逐组理由见该清单（X10）。自证 ㉑ 共用本函数体钉红该形态。
        var bySurface = list
            .Where(r => !string.IsNullOrWhiteSpace(r.CountPattern))
            .GroupBy(r => r.Surface, StringComparer.Ordinal);
        foreach (var sg in bySurface)
        {
            foreach (var pg in sg.GroupBy(r => r.CountPattern, StringComparer.Ordinal))
            {
                if (pg.Count() <= 1) continue;
                var keys = pg.Select(r => r.Surface + "." + r.Name).Distinct(StringComparer.Ordinal).ToList();
                if (keys.All(k => KnownOverloadCollisions.Any(e => string.Equals(e.Key, k, StringComparison.Ordinal)))) continue;
                failures.Add($"OVERLOAD-SHARED-PATTERN: 同面共享同一 CountPattern（重载级串计，）→ {sg.Key} 模式 {pg.Key} 命中行 [{string.Join(", ", pg.Select(r => r.Name + "@" + r.ShapeKey))}]");
            }
        }

        // ── 豁免清单自身完整性────────────────────────────
        // 两项互补判据，缺一不可：
        //   (a) 反向存在性核——清单内每个 Key 必须在实台账（按分组键复算的命中组）中真实存在
        //       否则判红（防"塞废键"：加一个台账里根本没有的键，使豁免永不被命中而白占位）。
        //   (b) 固定白名单核——除既有 9 键外，清单出现任何新增键一律判红
        //       （防"顺手豁免真键"：该键在台账中真实存在，故 (a) 不报警，须靠白名单拦截）。
        var realHitKeys = new HashSet<string>(
            list.Where(r => !string.IsNullOrWhiteSpace(r.CountPattern))
                .GroupBy(r => r.Surface, StringComparer.Ordinal)
                .SelectMany(sg => sg.GroupBy(r => r.CountPattern, StringComparer.Ordinal)
                    .Where(pg => pg.Count() > 1)
                    .SelectMany(pg => pg.Select(r => r.Surface + "." + r.Name)))
                .Distinct(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var entry in KnownOverloadCollisions)
        {
            if (!realHitKeys.Contains(entry.Key))
                failures.Add($"OVERLOAD-EXEMPT-PHANTOM: 豁免清单含实台账中不存在的键（废键）→ {entry.Key}｜理由：{entry.Reason}");
        }

        var frozen = new HashSet<string>(FrozenOverloadWhitelist, StringComparer.Ordinal);
        foreach (var entry in KnownOverloadCollisions)
        {
            if (!frozen.Contains(entry.Key))
                failures.Add($"OVERLOAD-EXEMPT-UNAUTHORIZED: 豁免清单出现固定白名单外的新增键（须方案层裁定并登记理由后同步扩白名单）→ {entry.Key}｜理由：{entry.Reason}");
        }

        return failures;
    }

    /// <summary>计数下限台账完整性：137 行下限全部 ≥1、计数模式非空、且无跨面同模式（验收 1／ F6）。</summary>
    [Fact]
    public void SignatureCoverage_MinCounts_AllFilledAndPositive()
    {
        var failures = FindLedgerCompletenessFailures(SignatureCoverageRows);
        Assert.True(failures.Count == 0, "台账完整性判据 FAILED:\n" + string.Join("\n", failures));
    }

    /// <summary>计数守门：台账 = 46 签名 + 11 属性 + 80 ORM 终端 + 方言面实产行，且形态键集与扫描面<b>双向双射</b>。</summary>
    [Fact]
    public void SignatureCoverage_CountGate_And_ShapeKeyBijection()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var rows = SignatureCoverageRows;

        var failures = new List<string>();
        var fluentRows = rows.Count(r => IsFluentSurface(r.Surface));
        var attrRows = rows.Count(r => IsAttributeSurface(r.Surface));
        var ormRows = rows.Count(r => IsOrmSurface(r.Surface));
        var dialectRows = rows.Count(r => IsDialectSurface(r.Surface));

        if (rows.Length != ExpectedTotalRows)
            failures.Add($"COUNT: 台账行数 {rows.Length} ≠ 基准 {ExpectedTotalRows}（{ExpectedFluentSignatureRows} 签名 + {ExpectedAttributeRows} 属性 + {ExpectedOrmTerminalRows} ORM 终端 + {ExpectedDialectRows} 方言）");
        if (fluentRows != ExpectedFluentSignatureRows)
            failures.Add($"COUNT: 台账 Fluent 面行数 {fluentRows} ≠ {ExpectedFluentSignatureRows}");
        if (attrRows != ExpectedAttributeRows)
            failures.Add($"COUNT: 台账属性面行数 {attrRows} ≠ {ExpectedAttributeRows}");
        if (ormRows != ExpectedOrmTerminalRows)
            failures.Add($"COUNT: 台账 ORM 终端面行数 {ormRows} ≠ {ExpectedOrmTerminalRows}");
        if (dialectRows != ExpectedDialectRows)
            failures.Add($"COUNT: 台账方言适配面行数 {dialectRows} ≠ {ExpectedDialectRows}");
        if (dialect.Count != ExpectedDialectRows)
            failures.Add($"COUNT: 方言扫描面实产行数 {dialect.Count} ≠ 台账行数 {ExpectedDialectRows}（守门常量须与枚举器实产同步）");

        // 形态键 ↔ 扫描面双射（双向）——单向核不到「台账漏行」
        var scannedNorm = fluent.Concat(orm).Concat(dialect).Select(s => s.Key + "@" + s.ShapeKey)
                                .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var keyed = rows.Where(r => !IsAttributeSurface(r.Surface))
                        .Select(r => r.Surface + "." + r.Name + "@" + r.ShapeKey)
                        .OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (var miss in scannedNorm.Except(keyed, StringComparer.Ordinal))
            failures.Add($"BIJECTION: 扫描面有签名但台账无对应行 → {miss}（键集 ↔ 反射枚举集非双射，台账漏行将恒绿）");
        foreach (var extra in keyed.Except(scannedNorm, StringComparer.Ordinal))
            failures.Add($"BIJECTION: 台账有行但扫描面无对应签名 → {extra}（键集 ↔ 反射枚举集非双射，重载折叠或公开面已撤）");

        Assert.True(failures.Count == 0,
            "Check6 计数/双射守门 FAILED:\n" + string.Join("\n", failures));
    }

    /// <summary>台账首态守门：缺口行数 = 0（补齐后已归零），供「先证红」红点身份核对。</summary>
    [Fact]
    public void SignatureCoverage_FirstState_UncoveredRowCount_MatchesBaseline()
    {
        var uncovered = SignatureCoverageRows.Count(r => r.Anchor == UncoveredMarker);

        Assert.True(uncovered == ExpectedUncoveredRowsAtFirstRun,
            $"台账 UNCOVERED 行数 {uncovered} ≠ 基准 {ExpectedUncoveredRowsAtFirstRun}（完成后应归零——全局全绿判据）——" +
            "转绿归属：转 Fluent 面 8 行、转属性面 7 行、终态复验");
    }

    // =================================================================
    // 自证三例（与真实核查共用同一纯核查函数——防假测试纪律）
    // =================================================================

    /// <summary>自证 ①：台账删一行 → 扫描面 ⊖ 台账 → UNREGISTERED 红。</summary>
    [Fact]
    public void SelfProof1_LedgerRowRemoved_DetectedAsUnregistered()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var mutated = SignatureCoverageRows
            .Where(r => !(r.Surface == "MemberHead" && r.Name == "MapTree" && r.ShapeKey == "0|[]"))
            .ToArray();

        var failures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, mutated);

        Assert.Contains(failures, f => f.Contains("UNREGISTERED") && f.Contains("MemberHead.MapTree"));
    }

    /// <summary>自证 ②：锚点指向不存在的文件 → ANCHOR 红。</summary>
    [Fact]
    public void SelfProof2_AnchorPointsToMissingFile_DetectedAsAnchor()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var mutated = SignatureCoverageRows
            .Select(r => r.Name == "Format" && r.Surface == "MemberHead"
                ? (r.Surface, r.Name, r.ShapeKey, "test/NoSuchFile_9F3A.cs::Nope", r.MinCount, r.CountPattern)
                : r)
            .ToArray();

        var failures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, mutated);

        Assert.Contains(failures, f => f.Contains("ANCHOR") && f.Contains("NoSuchFile_9F3A"));
    }

    /// <summary>自证 ③：vacuous 护栏——扫描面返回 0 条即判红（防扫描面路径变更致恒绿）。</summary>
    [Fact]
    public void SelfProof3_EmptySurface_DetectedAsVacuous()
    {
        var failures = RunSignatureCoverageCheck(
            new[]
            {
                new List<(string Key, string Name, string ShapeKey)>(),
                new List<(string Key, string Name, string ShapeKey)>(),
                new List<(string Key, string Name, string ShapeKey)>(),
            },
            SignatureCoverageRows);

        Assert.Contains(failures, f => f.Contains("VACUOUS"));
    }

    // =================================================================
    // 自证扩展（重审计 A-8 乙案补齐：四类红中 STALE/UNCOVERED 原无自证——补齐后
    // 每类红各有至少一例实测检出；G10 用例数补齐的一部分）
    // =================================================================

    /// <summary>自证 ④：台账混入扫描面不存在的幽灵行 → STALE 红（台账 ⊖ 扫描面方向）。</summary>
    [Fact]
    public void SelfProof4_GhostRow_DetectedAsStale()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var mutated = SignatureCoverageRows
            .Append(("MemberHead", "NoSuchMethod", "0|[]", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackTests.cs::", 1, @"\.NoSuchMethod\s*[(<]"))
            .ToArray();

        var failures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, mutated);

        Assert.Contains(failures, f => f.Contains("STALE") && f.Contains("MemberHead.NoSuchMethod"));
    }

    /// <summary>自证 ⑤：台账行 UNCOVERED 标记 → UNCOVERED 红（行在台账、覆盖未落地方向）。</summary>
    [Fact]
    public void SelfProof5_UncoveredRow_DetectedAsUncovered()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var mutated = SignatureCoverageRows
            .Select(r => r.Surface == "MemberHead" && r.Name == "Mask"
                ? (r.Surface, r.Name, r.ShapeKey, UncoveredMarker, r.MinCount, r.CountPattern)
                : r)
            .ToArray();

        var failures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, mutated);

        Assert.Contains(failures, f => f.Contains("UNCOVERED") && f.Contains("MemberHead.Mask"));
    }

    /// <summary>自证 ⑥：属性面行删除 → UNREGISTERED 红（属性面漏登方向）。</summary>
    [Fact]
    public void SelfProof6_AttributeRowRemoved_DetectedAsUnregistered()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var mutated = SignatureCoverageRows
            .Where(r => !(IsAttributeSurface(r.Surface) && r.Name == "Prefixes"))
            .ToArray();

        var failures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, mutated);

        Assert.Contains(failures, f => f.Contains("UNREGISTERED") && f.Contains("MapDefaultsAttribute.Prefixes"));
    }

    /// <summary>自证 ⑦：台账整体清空 → 扫描面全量 UNREGISTERED（非 VACUOUS——扫描面非空时台账缺失逐行报红）。</summary>
    [Fact]
    public void SelfProof7_EmptyLedger_AllUnregistered()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();

        var failures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, Array.Empty<(string, string, string, string, int, string)>());

        Assert.DoesNotContain(failures, f => f.Contains("VACUOUS"));
        Assert.Equal(fluent.Count + attrs.Count + orm.Count + dialect.Count, failures.Count(f => f.Contains("UNREGISTERED")));
    }

    /// <summary>自证 ⑧：锚点缺 "::" 分隔符 → ANCHOR 格式非法红。</summary>
    [Fact]
    public void SelfProof8_MalformedAnchorFormat_DetectedAsAnchor()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var mutated = SignatureCoverageRows
            .Select(r => r.Surface == "MemberHead" && r.Name == "Format"
                ? (r.Surface, r.Name, r.ShapeKey, "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackTests.cs-NoSeparator", r.MinCount, r.CountPattern)
                : r)
            .ToArray();

        var failures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, mutated);

        Assert.Contains(failures, f => f.Contains("ANCHOR") && f.Contains("格式非法"));
    }

    /// <summary>自证 ⑨：属性面扫描面清空（部分 vacuous）→ 11 行孤儿台账 STALE 红——防特性类型改名致属性面台账漂移。</summary>
    [Fact]
    public void SelfProof9_EmptyAttributeSurface_AttributeRowsStale()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();

        var failures = RunSignatureCoverageCheck(
            new[]
            {
                fluent,
                new List<(string Key, string Name, string ShapeKey)>(),
                orm,
                dialect,
            },
            SignatureCoverageRows);

        Assert.DoesNotContain(failures, f => f.Contains("VACUOUS"));
        Assert.Equal(11, failures.Count(f => f.Contains("STALE") && f.Contains("MapDefaultsAttribute.")));
    }

    /// <summary>自证 ⑩：Fluent 面扫描面清空（部分 vacuous）→ 46 行孤儿台账 STALE 红——防反射口径漂移致签名面台账漂移。</summary>
    [Fact]
    public void SelfProof10_EmptyFluentSurface_FluentRowsStale()
    {
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();

        var failures = RunSignatureCoverageCheck(
            new[]
            {
                new List<(string Key, string Name, string ShapeKey)>(),
                attrs,
                orm,
                dialect,
            },
            SignatureCoverageRows);

        Assert.DoesNotContain(failures, f => f.Contains("VACUOUS"));
        Assert.Equal(46, failures.Count(f => f.Contains("STALE")));
    }

    /// <summary>自证 ⑪：锚点 method 段为空 = 仅文件存在性弱校验（契约固化——不因空段误报 ANCHOR）。</summary>
    [Fact]
    public void SelfProof11_AnchorEmptyMethod_FileExistenceOnly_Passes()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var mutated = SignatureCoverageRows
            .Select(r => r.Surface == "MemberHead" && r.Name == "Mask"
                ? (r.Surface, r.Name, r.ShapeKey, "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.Mask.cs::", r.MinCount, r.CountPattern)
                : r)
            .ToArray();

        var failures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, mutated);

        Assert.DoesNotContain(failures, f => f.Contains("ANCHOR") && f.Contains("MemberHead.Mask"));
    }

    /// <summary>自证 ⑫：计数双射守门对「台账漏行」方向实测检出（删一行 → BIJECTION 红）。</summary>
    [Fact]
    public void SelfProof12_MissingLedgerRow_CountGateBijectionRed()
    {
        var mutated = SignatureCoverageRows
            .Where(r => !(r.Surface == "MemberHead" && r.Name == "Mask"))
            .ToArray();

        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var rows = mutated;

        var failures = new List<string>();
        var scannedNorm = fluent.Concat(orm).Concat(dialect).Select(s => s.Key + "@" + s.ShapeKey)
                                .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var keyed = rows.Where(r => !IsAttributeSurface(r.Surface))
                        .Select(r => r.Surface + "." + r.Name + "@" + r.ShapeKey)
                        .OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (var miss in scannedNorm.Except(keyed, StringComparer.Ordinal))
            failures.Add($"BIJECTION: 扫描面有签名但台账无对应行 → {miss}");
        foreach (var extra in keyed.Except(scannedNorm, StringComparer.Ordinal))
            failures.Add($"BIJECTION: 台账有行但扫描面无对应签名 → {extra}");

        Assert.Contains(failures, f => f.Contains("BIJECTION") && f.Contains("MemberHead.Mask"));
    }

    // =================================================================
    // 自证扩展（UNDERCOUNT 计数下限类红）
    // =================================================================

    /// <summary>
    /// 自证 ⑬（双态）：计数下限 <c>UNDERCOUNT</c> 类红——① 人造「高下限（实测 +1）」→ 必红且身份精确；
    /// ② 复原（下限 = 人造计数）→ 零 UNDERCOUNT。（实测值绿态由 <see cref="SignatureCoverage_AllRows_ResolvesNoFailure"/> 承载。）
    /// </summary>
    [Fact]
    public void SelfProof13_UnderCount_HighFloorRed_ThenRestoredGreen()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();

        // 人造计数 = 各行下限（保证除被抬高行外全部达标）
        var counts = SignatureCoverageRows.ToDictionary(
            r => CoverageCountScanner.RowKey(r.Surface, r.Name, r.ShapeKey),
            r => r.MinCount,
            StringComparer.Ordinal);

        // 态①：将 MemberHead.Mask 行下限抬高 1 → 必红且身份精确
        var highFloor = SignatureCoverageRows
            .Select(r => r.Surface == "MemberHead" && r.Name == "Mask"
                ? (r.Surface, r.Name, r.ShapeKey, r.Anchor, r.MinCount + 1, r.CountPattern)
                : r)
            .ToArray();
        var redFailures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, highFloor, counts);
        Assert.Contains(redFailures, f => f.Contains("UNDERCOUNT") && f.Contains("MemberHead.Mask"));
        Assert.Single(redFailures, f => f.Contains("UNDERCOUNT"));

        // 态②：复原（实台账）→ 零 UNDERCOUNT
        var greenFailures = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, SignatureCoverageRows, counts);
        Assert.DoesNotContain(greenFailures, f => f.Contains("UNDERCOUNT"));
    }

    /// <summary>自证 ⑭：计数源为空 → VACUOUS 红（防计数扫描面失效致计数下限门禁恒绿）。</summary>
    [Fact]
    public void SelfProof14_EmptyCounts_DetectedAsVacuous()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();

        var failures = RunSignatureCoverageCheck(
            new[] { fluent, attrs, orm, dialect }, SignatureCoverageRows, new Dictionary<string, int>(StringComparer.Ordinal));

        Assert.Contains(failures, f => f.Contains("VACUOUS"));
    }

    // =================================================================
    // 自证扩展（ORM 终端面——同名串计防线 / 面枚举漏类型 / 0 值行闭合）
    // =================================================================

    /// <summary>
    /// 自证 ⑮（同名串计防线，四态）：三扩展终端同名且与 LINQ 同名——
    /// ① 无辨异（缺省 Name 派生 + 全 test 树）时计数被同一批 LINQ 同名行使灌高（≥20 量级）；
    /// ② 辨异后（链前缀 + 面级文件过滤器）计数显著下降且仍 ≥1；
    /// ③ 若下限按无辨异口径登记，则辨异口径下的真实行使数必触发 <c>UNDERCOUNT</c> 且身份精确
    ///    ——证「防线失效即报警」，防线不是装饰；
    /// ④ <b>承重墙归位</b>：链前缀<b>单独</b>不隔离跨面（EFCore 链模式在 FreeSql 文件上同样命中），
    ///    去掉面级过滤器即被灌高 → 证过滤器是必要的第二道防线；两面锚点文件互不落入对方过滤器
    ///    → 证两面计数由互不相交的文件集决定（跨面串计已被压掉）。
    /// <para>覆盖对象清单含 <c>AsDto</c>（对照项）：入口/guard 不入面后，selector 行不被 <c>AsDto</c> 调用灌入。</para>
    /// <para><b>不主张</b>「无辨异口径下两面计数相等」为证据——两面同用同一模式时该等式恒真，无检出力。</para>
    /// </summary>
    [Fact]
    public void SelfProof15_SameNameTerminals_CountIsolationAndFailureDetection()
    {
        var root = FindRepoRoot();
        var names = new[] { "Single", "First", "ToPagedList", "ToDictionary", "ToChunkDto", "ToList", "AsDto" };

        var naiveSpecs = names
            .SelectMany(n => new[]
            {
                ("FreeSqlDtoSelector", n, "shape", @"\." + n + @"\s*[(<]"),
                ("EFCoreDtoSelector", n, "shape", @"\." + n + @"\s*[(<]"),
            })
            .ToList();
        var naive = CoverageCountScanner.CountForRows(root, naiveSpecs);

        string FsChain(string n) => n == "AsDto" ? @"\." + n + @"\s*[(<]" : @"Select<[^>,]*>[^;]*AsDto[^;]*\." + n + @"\s*[(<]";
        string EfChain(string n) => n == "AsDto" ? @"\." + n + @"\s*[(<]" : @"AsDto\(\)[^;]*\." + n + @"\s*[(<]";

        var isolatedSpecs = names
            .SelectMany(n => new[]
            {
                ("FreeSqlDtoSelector", n, "shape", FsChain(n)),
                ("EFCoreDtoSelector", n, "shape", EfChain(n)),
            })
            .ToList();
        var isolated = CoverageCountScanner.CountForRows(root, isolatedSpecs, OrmSurfaceFileFilters);

        // ④ 只加链前缀、不加面级过滤器
        var chainOnly = CoverageCountScanner.CountForRows(root, isolatedSpecs);

        var failures = new List<string>();
        var fsLoadBearing = 0;
        var efLoadBearing = 0;
        var crossSurfaceHits = 0;
        var fsFilter = OrmSurfaceFileFilters["FreeSqlDtoSelector"];
        var efFilter = OrmSurfaceFileFilters["EFCoreDtoSelector"];

        foreach (var n in names)
        {
            var fsKey = "FreeSqlDtoSelector." + n + "@shape";
            var efKey = "EFCoreDtoSelector." + n + "@shape";

            // ① 无辨异口径必须被灌高，否则本自证为空转
            if (naive[fsKey] <= 20 || naive[efKey] <= 20)
                failures.Add($"串计自证①失效：{n} 无辨异计数 {naive[fsKey]} / {naive[efKey]} 未达灌高量级");

            // ② 辨异后必须下降且仍可用
            if (isolated[fsKey] < 1 || isolated[efKey] < 1)
                failures.Add($"串计自证②失效：{n} 辨异计数为 0（防线过窄致台账不可用）");
            if (isolated[fsKey] >= naive[fsKey] || isolated[efKey] >= naive[efKey])
                failures.Add($"串计自证②失效：{n} 辨异计数未低于无辨异计数（{isolated[fsKey]}/{naive[fsKey]}、{isolated[efKey]}/{naive[efKey]}）");

            // ④ 过滤器只收窄不反向；并统计其对两面各自的承重量
            if (chainOnly[fsKey] < isolated[fsKey] || chainOnly[efKey] < isolated[efKey])
                failures.Add($"串计自证④失效：{n} 加过滤器后反而升高（{chainOnly[fsKey]}/{isolated[fsKey]}、{chainOnly[efKey]}/{isolated[efKey]}）");
            if (chainOnly[fsKey] > isolated[fsKey]) fsLoadBearing++;
            if (chainOnly[efKey] > isolated[efKey]) efLoadBearing++;

            // ④b 跨面灌入实测：把 EFCore 链模式放到 FreeSql 文件集上——
            //     FreeSql 链同样含 `AsDto()…`，故链前缀单独不足以隔离，过滤器不可省
            var crossEf = CoverageCountScanner.CountForRows(
                root,
                new[] { ("EFCoreDtoSelector", n, "shape", EfChain(n)) },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["EFCoreDtoSelector"] = fsFilter })[efKey];
            if (crossEf > 0) crossSurfaceHits++;

            if (n == "AsDto")
                continue;

            // ③ 下限按无辨异口径登记 → 真实口径必触发 UNDERCOUNT 且身份精确
            var mutated = SignatureCoverageRows
                .Select(r => r.Surface == "FreeSqlDtoSelector" && r.Name == n
                    ? (r.Surface, r.Name, r.ShapeKey, r.Anchor, naive[fsKey], r.CountPattern)
                    : r)
                .ToArray();
            var counts = CoverageCountScanner.CountForRows(
                root,
                mutated.Select(r => (r.Surface, r.Name, r.ShapeKey, r.CountPattern)),
                OrmSurfaceFileFilters);
            var red = RunSignatureCoverageCheck(AllSurfaces(), mutated, counts);
            if (!red.Any(f => f.Contains("UNDERCOUNT") && f.Contains("FreeSqlDtoSelector." + n)))
                failures.Add($"串计自证③失效：{n} 抬高下限至无辨异口径后未触发 UNDERCOUNT（防线不报警）");
        }

        // ④c 两道防线的分工按面不同，任一缺失都不得假定安全：
        //     过滤器对两面各自都须至少承重若干次；链前缀单独不隔离（跨面命中实测非零）
        if (fsLoadBearing == 0 || efLoadBearing == 0)
            failures.Add($"串计自证④失效：面级过滤器对某一面完全不承重（FreeSql {fsLoadBearing} / EFCore {efLoadBearing}）→ 防线分工未实测");
        if (crossSurfaceHits == 0)
            failures.Add("串计自证④失效：跨面灌入实测为 0 → 「链前缀单独不足以隔离」的断言无出处");

        Assert.True(failures.Count == 0, "同名串计自证 FAILED:\n" + string.Join("\n", failures));
    }

    /// <summary>自证 ⑯（面枚举漏类型）：入面清单删掉一类 selector → 该类台账行必 STALE 红（防「整面恒绿」）。</summary>
    [Fact]
    public void SelfProof16_OrmSurfaceTypeMissing_DetectedAsStale()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();
        var ormWithoutEfCore = orm
            .Where(s => !s.Key.StartsWith("EFCoreDtoSelector.", StringComparison.Ordinal))
            .ToList();

        Assert.True(ormWithoutEfCore.Count < orm.Count, "漏类型自证空转：EFCoreDtoSelector 未在 ORM 扫描面内");

        var failures = RunSignatureCoverageCheck(
            new[] { fluent, attrs, ormWithoutEfCore, dialect }, SignatureCoverageRows);

        Assert.Contains(failures, f => f.Contains("STALE") && f.Contains("EFCoreDtoSelector."));
    }

    /// <summary>
    /// 自证 ⑰（0 值行闭合规则）：ORM 行删除 → UNREGISTERED 红且身份精确；
    /// 行下限填 0 → 完整性用例判红（不得以填 0 绕过；不得删行留红）。
    /// </summary>
    [Fact]
    public void SelfProof17_ZeroCountRowClosure_BothBypassesDetected()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var dialect = EnumerateDialectSurface();

        // 绕行①：删行 → UNREGISTERED
        var target = SignatureCoverageRows.First(r => r.Surface == "DapperDtoSelector" && r.Name == "QueryUnbufferedAsync");
        var removed = SignatureCoverageRows.Where(r => r != target).ToArray();
        var unregistered = RunSignatureCoverageCheck(new[] { fluent, attrs, orm, dialect }, removed);
        Assert.Contains(unregistered, f => f.Contains("UNREGISTERED") && f.Contains("DapperDtoSelector.QueryUnbufferedAsync"));

        // 绕行②：留行填 0 → 台账完整性判据必红（共用被测判据函数本体 FindLedgerCompletenessFailures）
        var zeroFilled = SignatureCoverageRows
            .Select(r => r.Surface == target.Surface && r.Name == target.Name && r.ShapeKey == target.ShapeKey
                ? (r.Surface, r.Name, r.ShapeKey, r.Anchor, MinCount: 0, r.CountPattern)
                : r)
            .ToArray();
        var red2 = FindLedgerCompletenessFailures(zeroFilled);
        Assert.Contains(red2, f => f.Contains("UNDERCOUNT")
                                   && f.Contains("DapperDtoSelector.QueryUnbufferedAsync"));
        Assert.DoesNotContain(FindLedgerCompletenessFailures(SignatureCoverageRows),
            f => f.Contains("UNDERCOUNT"));
    }

    /// <summary>
    /// 自证 ⑱（跨面同模式 = 跨面串计，F6）：把 compose 面的某行模式复制给 compose-3 面
    /// （即历史上出现过的形态：两面的同名行 CountPattern 逐字符相同）→ 完整性判据必报
    /// <c>PATTERN-COLLISION</c>；实台账在该判据下零命中。
    /// <para>存在理由：跨面同模式等价于把两行台账登记成一行——删光其中一面的全部用例，另一面的下限仍撑起该行，
    /// 门禁对该面实际失效。本自证把该形态永久钉红。</para>
    /// </summary>
    [Fact]
    public void SelfProof18_IdenticalPatternAcrossSurfaces_DetectedAsCollision()
    {
        var donor = SignatureCoverageRows.First(r => r.Surface == "FreeSqlComposeDtoSelector");
        var mutated = SignatureCoverageRows
            .Select(r => r.Surface == "FreeSqlComposeDtoSelector3" && r.Name == donor.Name
                ? (r.Surface, r.Name, r.ShapeKey, r.Anchor, r.MinCount, donor.CountPattern)
                : r)
            .ToArray();

        var red = FindLedgerCompletenessFailures(mutated);
        Assert.Contains(red, f => f.Contains("PATTERN-COLLISION") && f.Contains("FreeSqlComposeDtoSelector3"));
        Assert.DoesNotContain(FindLedgerCompletenessFailures(SignatureCoverageRows),
            f => f.Contains("PATTERN-COLLISION"));
    }

    /// <summary>
    /// 自证 ⑲（计数源自引用面 · 门禁类自身文件排除的<b>承重实测</b>登记簿 #43 /）：
    /// 三段判据，缺一即红——
    /// ① <b>防护非空转</b>：门禁类文件确实向语料贡献测试方法（含门禁类时语料方法数严格大于缺省语料），
    ///    否则文件名排除面已漂移（改名/挪目录 → 防护形同虚设）；
    /// ② <b>排除面精确</b>：两语料之差集内的文件全部落在 <see cref="CoverageCountScanner.SelfFileNames"/>，
    ///    不得波及其他任何文件；
    /// ③ <b>现网零灌入</b>：对全部台账模式，门禁类方法体在两种语料下的命中数相等——
    ///    若不等，说明门禁类已开始用方法体承载台账模式文本、防护正在替它挡灌入（须把该文本移出方法体或改锚，
    ///    不得靠防护吞掉）。
    /// <para>存在理由：计数源遍历全 <c>test/</c> 树，门禁类既是扫描器调用者又是自证宿主——
    /// 其方法体若含 <c>.MapTree(</c> 一类模式即会静默抬高对应行下限，门禁自灌下限使「调用点减少」这一维检出力被抵消。</para>
    /// </summary>
    [Fact]
    public void SelfProof19_SelfFileExclusion_IsLoadBearing()
    {
        var root = FindRepoRoot();
        var guarded = CoverageCountScanner.ExtractTestMethodBodies(root);
        var unguarded = CoverageCountScanner.ExtractTestMethodBodies(root, includeSelfFiles: true);

        Assert.NotEmpty(guarded);

        // ① 防护非空转
        Assert.True(unguarded.Count > guarded.Count,
            $"门禁类文件未向语料贡献任何测试方法（含门禁类 {unguarded.Count} / 缺省 {guarded.Count}）" +
            "→ SelfFileNames 与实际文件名漂移，防护面空转");

        // ② 排除面精确
        var removed = unguarded.Except(guarded).ToList();
        Assert.NotEmpty(removed);
        foreach (var m in removed)
        {
            var name = Path.GetFileName(m.File.Replace('\\', '/'));
            Assert.True(CoverageCountScanner.SelfFileNames.Contains(name),
                $"排除波及非门禁文件 → {m.File}::{m.Method}");
        }

        // ③ 现网零灌入：逐台账模式比对两语料命中数
        var patterns = SignatureCoverageRows
            .Select(r => r.CountPattern)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var p in patterns)
        {
            var rx = new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            var nGuarded = guarded.Count(m => rx.IsMatch(m.Body));
            var nUnguarded = unguarded.Count(m => rx.IsMatch(m.Body));
            Assert.True(nGuarded == nUnguarded,
                $"门禁类方法体正在灌入台账模式「{p}」（含门禁类 {nUnguarded} / 缺省 {nGuarded}）" +
                "——防护虽挡住下限抬高，但该文本不应存在于门禁类方法体内：移出方法体或改挂真实锚点");
        }
    }

    // =================================================================
    // 自证扩展（SQL 方言适配面——跨面模式碰撞 / 方言分支承重）
    // =================================================================

    /// <summary>
    /// 自证 ⑳(a)（跨面同模式 · 方言面方向）：把某方言行的 <c>CountPattern</c> 复制给<b>另一方言面</b>行
    /// → 完整性判据必报 <c>PATTERN-COLLISION</c>；实台账在该判据下零命中。
    /// <para>与自证 ⑱ 同族但作用面不同：⑱ 钉的是 compose 两 selector 面，本证钉的是本册<b>新增的方言面</b>——
    /// 防「方言面各行共用 <c>new XxxDialect(</c> 派生式而写成同一条模式」这一类跨面串计
    /// （模式相同即两行实为一行，删光一面的用例另一面下限仍撑起该行）。
    /// <b>共用被测判据函数本体</b> <see cref="FindLedgerCompletenessFailures"/>（#219①），不重写判据表达式。</para>
    /// </summary>
    [Fact]
    public void SelfProof20a_DialectPatternAcrossSurfaces_DetectedAsCollision()
    {
        var donor = SignatureCoverageRows.First(r => r.Surface == "Db2Dialect" && r.Name == "ctor");
        var target = SignatureCoverageRows.First(r => r.Surface == "MySqlDialect" && r.Name == "ctor");

        // 态①：把 Db2Dialect 行的模式复制给 MySqlDialect 行 → 必红
        var mutated = SignatureCoverageRows
            .Select(r => r.Surface == target.Surface && r.Name == target.Name && r.ShapeKey == target.ShapeKey
                ? (r.Surface, r.Name, r.ShapeKey, r.Anchor, r.MinCount, donor.CountPattern)
                : r)
            .ToArray();
        var red = FindLedgerCompletenessFailures(mutated);
        Assert.Contains(red, f => f.Contains("PATTERN-COLLISION") && f.Contains("MySqlDialect"));

        // 态②：恢复实台账 → 零 PATTERN-COLLISION
        Assert.DoesNotContain(FindLedgerCompletenessFailures(SignatureCoverageRows),
            f => f.Contains("PATTERN-COLLISION"));
    }

    /// <summary>
    /// 自证 ⑳(b)（方言分支<b>承重自证</b>）：把 dialect 面的行注入扫描面为<b>空</b>
    /// （等价于把分支内的 ctor 节点类型摘掉 → 该面产不出任何行）→ 该面必落
    /// <c>BIJECTION: 台账有行但扫描面无对应签名</c>（证明分支真承重而非装饰）。
    /// <para>判据表达式与 <see cref="SignatureCoverage_CountGate_And_ShapeKeyBijection"/> 同形
    /// （同一双射内核），此处复用的是<b>实台账 + 空方言面</b>这一对输入，不另立判据。</para>
    /// <para>同时钉住「非 VACUOUS 短路」：其余三面非空，故落的是 BIJECTION 逐行红而非整体 vacuous 红——
    /// 若某次改动使方言面产出恒为 0，本自证不会因 VACUOUS 提前返回而被掩盖。</para>
    /// </summary>
    [Fact]
    public void SelfProof20b_DialectBranchRemoved_DetectedAsBijection()
    {
        var fluent = EnumerateFluentSignatureSurface();
        var attrs = EnumerateAttributePropertySurface();
        var orm = EnumerateOrmTerminalSurface();
        var emptyDialect = new List<(string Key, string Name, string ShapeKey)>();

        // 态①：方言分支摘掉（扫描面该族为空）→ 台账 7 行全落 BIJECTION
        var scannedNorm = fluent.Concat(orm).Select(s => s.Key + "@" + s.ShapeKey)
                                .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var keyed = SignatureCoverageRows.Where(r => !IsAttributeSurface(r.Surface))
                        .Select(r => r.Surface + "." + r.Name + "@" + r.ShapeKey)
                        .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var red = keyed.Except(scannedNorm, StringComparer.Ordinal)
                       .Select(x => $"BIJECTION: 台账有行但扫描面无对应签名 → {x}")
                       .ToList();
        foreach (var (_, typeName) in DialectInSurfaceTypes)
            Assert.Contains(red, f => f.Contains("BIJECTION") && f.Contains(typeName + "."));

        // 其余三面非空 → 不得退化为 VACUOUS（防「摘掉分支即整体判红」掩盖身份）
        Assert.True(emptyDialect.Count == 0 && fluent.Count > 0 && orm.Count > 0,
            "方言分支承重自证空转：其余面为空致判据退化为 VACUOUS");

        // 态②：恢复（方言面接回枚举器）→ 该方向零 BIJECTION
        var restoredNorm = scannedNorm.Concat(EnumerateDialectSurface().Select(s => s.Key + "@" + s.ShapeKey))
                                .OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.DoesNotContain(keyed.Except(restoredNorm, StringComparer.Ordinal),
            x => DialectSurfaceTypeNames.Any(t => x.StartsWith(t + ".", StringComparison.Ordinal)));
    }

    // =================================================================
    // 自证扩展（Dapper 重载级辨异——同面共享模式判据）
    // =================================================================

    /// <summary>
    /// 自证 ㉑（重载级共享模式 · 本册）：把 <c>DapperDtoSelector.Query@1|[CommandDefinition]</c> 行的
    /// <c>CountPattern</c> 复制给 <c>QueryFirst@1|[CommandDefinition]</c> 行（<b>同面不同 Name</b>）
    /// → 完整性判据必报 <c>OVERLOAD-SHARED-PATTERN</c>，且<b>不</b>报 <c>PATTERN-COLLISION</c>
    /// （后者仅跨面发射、同面放过——见 <see cref="FindLedgerCompletenessFailures"/> 两判据分工）。
    /// <para>反向断言（#219③）：实台账（含本册 Dapper 11 组）在 <c>OVERLOAD-SHARED-PATTERN</c> 下零命中——
    /// 辨异后 CD/STR 两组模式互斥、QueryMultiple 两行按泛型元数互斥，同面内无两行共享模式；
    /// 既有 Fluent 9 组走 <see cref="KnownOverloadCollisions"/> 豁免，不参与该"零命中"反向断言的语义。</para>
    /// <para><b>共用被测判据函数本体</b> <see cref="FindLedgerCompletenessFailures"/>（#219①），不重写判据表达式。</para>
    /// </summary>
    [Fact]
    public void SelfProof21_OverloadSharedPattern_DetectedAndRealLedgerClean()
    {
        var donor = SignatureCoverageRows.First(r =>
            r.Surface == "DapperDtoSelector" && r.Name == "Query" && r.ShapeKey == "1|[CommandDefinition]");

        // 态①：把 Query@CD 行模式复制给 QueryFirst@CD 行（同面不同 Name）→ 必触发 OVERLOAD、不触发 PATTERN-COLLISION
        var mutated = SignatureCoverageRows
            .Select(r => r.Surface == "DapperDtoSelector" && r.Name == "QueryFirst" && r.ShapeKey == "1|[CommandDefinition]"
                ? (r.Surface, r.Name, r.ShapeKey, r.Anchor, r.MinCount, donor.CountPattern)
                : r)
            .ToArray();
        var red = FindLedgerCompletenessFailures(mutated);
        Assert.Contains(red, f => f.Contains("OVERLOAD-SHARED-PATTERN") && f.Contains("DapperDtoSelector"));
        Assert.DoesNotContain(red, f => f.Contains("PATTERN-COLLISION"));

        // 态②：恢复实台账 → OVERLOAD-SHARED-PATTERN 零命中（本册 Dapper 11 组辨异生效；Fluent 9 组走豁免）
        var clean = FindLedgerCompletenessFailures(SignatureCoverageRows);
        Assert.DoesNotContain(clean, f => f.Contains("OVERLOAD-SHARED-PATTERN"));
    }

    /// <summary>
    /// 自证 ㉒（豁免清单自身完整性 ·）：
    /// 验证 <see cref="KnownOverloadCollisions"/> 的两项互补防线均生效。
    /// <para><b>态 A（废键必红）</b>：向清单加入一个实台账中不存在的键 → 必报 <c>OVERLOAD-EXEMPT-PHANTOM</c>。
    /// 这一态锁住"反向存在性核"——防"塞废键占位"（该键永不命中，豁免形同虚设但清单看似有理由）。</para>
    /// <para><b>态 B（真键必红）</b>：向清单加入一个实台账中真实存在、但不在固定白名单内的键
    /// → 必报 <c>OVERLOAD-EXEMPT-UNAUTHORIZED</c>。这一态锁住"固定白名单核"——
    /// 防"顺手豁免应判红形态"（该键真实存在，故态 A 的存在性核**不会**报警，是核心威胁）。</para>
    /// <para><b>态 C（删键必红）</b>：从清单删掉一个既有键 → 该键对应的共享模式必报
    /// <c>OVERLOAD-SHARED-PATTERN</c>（承重未退化，防"清单被清空仍绿"）。</para>
    /// <para><b>反向断言</b>：实台账 ＋ 真实清单 → 两项新判据均零命中（防判据恒真）。</para>
    /// <para><b>共用被测判据函数本体</b> <see cref="FindLedgerCompletenessFailures"/>，不重写判据表达式。</para>
    /// </summary>
    [Fact]
    public void SelfProof22_ExemptionListIntegrity_PhantomAndUnauthorizedAndDeletionDetected()
    {
        // 基线：真实清单 ＋ 实台账 → 两项新判据均零命中（反向断言，防恒真）
        var baseline = FindLedgerCompletenessFailures(SignatureCoverageRows);
        Assert.DoesNotContain(baseline, f => f.Contains("OVERLOAD-EXEMPT-PHANTOM"));
        Assert.DoesNotContain(baseline, f => f.Contains("OVERLOAD-EXEMPT-UNAUTHORIZED"));

        // 态 A：废键（实台账中不存在的键）→ 必报 PHANTOM
        KnownOverloadCollisions =
        [
            .. KnownOverloadCollisions,
            ("DapperDtoSelector.NoSuchMethodAtAll", "自证植入：实台账不存在的废键"),
        ];
        try
        {
            var phantom = FindLedgerCompletenessFailures(SignatureCoverageRows);
            Assert.Contains(phantom, f => f.Contains("OVERLOAD-EXEMPT-PHANTOM")
                                       && f.Contains("DapperDtoSelector.NoSuchMethodAtAll"));
        }
        finally
        {
            KnownOverloadCollisions =
            [
                .. KnownOverloadCollisions.Where(e => e.Key != "DapperDtoSelector.NoSuchMethodAtAll"),
            ];
        }

        // 态 B：真键（实台账真实存在、但不在固定白名单内）→ 必报 UNAUTHORIZED
        // 选取 Fluent 面一个真实命中键：它若已被 9 键覆盖则取不到，故用一个白名单外的真实共享键。
        // 若实台账中不存在白名单外的真实共享键，则本态改为"把白名单缩一格"来构造同一语义（见下）。
        var realHitKeys = SignatureCoverageRows
            .Where(r => !string.IsNullOrWhiteSpace(r.CountPattern))
            .GroupBy(r => r.Surface, StringComparer.Ordinal)
            .SelectMany(sg => sg.GroupBy(r => r.CountPattern, StringComparer.Ordinal)
                .Where(pg => pg.Count() > 1)
                .SelectMany(pg => pg.Select(r => r.Surface + "." + r.Name)))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var unauthorizedCandidate = realHitKeys
            .Where(k => !FrozenOverloadWhitelist.Contains(k, StringComparer.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .FirstOrDefault();

        if (unauthorizedCandidate != null)
        {
            // 直接植入一个白名单外的真实键
            KnownOverloadCollisions =
            [
                .. KnownOverloadCollisions,
                (unauthorizedCandidate, "自证植入：白名单外的真实键（应判红）"),
            ];
            try
            {
                var unauth = FindLedgerCompletenessFailures(SignatureCoverageRows);
                Assert.Contains(unauth, f => f.Contains("OVERLOAD-EXEMPT-UNAUTHORIZED")
                                          && f.Contains(unauthorizedCandidate));
            }
            finally
            {
                KnownOverloadCollisions =
                [
                    .. KnownOverloadCollisions.Where(e => e.Key != unauthorizedCandidate),
                ];
            }
        }
        else
        {
            // 退化路径：实台账里没有白名单外的真实共享键 → 用"白名单缩一格"构造同一语义
            // （既有 9 键中任取一键从白名单移除 → 该键虽在清单内且真实存在，仍必报 UNAUTHORIZED）
            var victim = FrozenOverloadWhitelist[0];
            FrozenOverloadWhitelist = [.. FrozenOverloadWhitelist.Where(k => k != victim)];
            try
            {
                var unauth = FindLedgerCompletenessFailures(SignatureCoverageRows);
                Assert.Contains(unauth, f => f.Contains("OVERLOAD-EXEMPT-UNAUTHORIZED") && f.Contains(victim));
            }
            finally
            {
                FrozenOverloadWhitelist = [victim, .. FrozenOverloadWhitelist];
            }
        }

        // 态 C：删既有键 → 对应共享模式必报 OVERLOAD-SHARED-PATTERN（承重未退化）
        var dropped = KnownOverloadCollisions[0].Key;
        KnownOverloadCollisions = [.. KnownOverloadCollisions.Skip(1)];
        try
        {
            var deletion = FindLedgerCompletenessFailures(SignatureCoverageRows);
            Assert.Contains(deletion, f => f.Contains("OVERLOAD-SHARED-PATTERN"));
        }
        finally
        {
            KnownOverloadCollisions =
            [
                (dropped, "F8 锁定面：构造函数参数映射与同名属性映射共享计数模式；解锁须方案层裁定重载级辨异口径（X10）"),
                .. KnownOverloadCollisions,
            ];
        }
    }

    // =================================================================
    // 机械枚举
    // =================================================================
    /// <summary>
    /// Fluent 签名面：反射枚举 5 配置类公开实例方法 → <c>(键, 方法名, 形态键)</c>。
    /// <b>按 <c>DeclaringType</c> 归集</b>（防继承面重复计数）；排除 <c>object</c> 四方法与
    /// <c>IsSpecialName</c>；构造器不计。
    /// </summary>
    internal static List<(string Key, string Name, string ShapeKey)> EnumerateFluentSignatureSurface()
    {
        var assembly = typeof(NCMapper.FluentMappingConfig).Assembly;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        var surface = new List<(string Key, string Name, string ShapeKey)>();
        foreach (var typeName in FluentConfigTypeNames)
        {
            var type = assembly.GetType(typeName)
                ?? throw new InvalidOperationException($"Fluent 配置类解析失败: {typeName}（枚举源失效）");

            foreach (var method in type.GetMethods(flags))
            {
                var declaring = method.DeclaringType;
                if (declaring == null || declaring == typeof(object)) continue;
                if (method.IsSpecialName) continue;

                var simple = FluentSurfaceIntegrityTests.SimpleTypeName(declaring.Name);
                surface.Add((simple + "." + method.Name, method.Name, BuildShapeKey(method)));
            }
        }

        return surface
            .GroupBy(s => s)
            .Select(g => g.First())
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .ThenBy(s => s.ShapeKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 特性属性面：反射枚举目标特性类型的公开实例属性（<c>DeclaredOnly</c>）→ <c>(键, 属性名, 描述)</c>。
    /// 描述取属性类型名（与 <c>CapabilitySurfaceIntegrityTests</c> 的 Descriptor 口径一致）。
    /// </summary>
    internal static List<(string Key, string Name, string ShapeKey)> EnumerateAttributePropertySurface()
    {
        var assembly = typeof(NCMapper.FluentMappingConfig).Assembly;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var surface = new List<(string Key, string Name, string ShapeKey)>();
        foreach (var typeName in AttributeTypeNames)
        {
            var type = assembly.GetType(typeName)
                ?? throw new InvalidOperationException($"特性类型解析失败: {typeName}（枚举源失效）");

            foreach (var prop in type.GetProperties(flags))
            {
                var simple = FluentSurfaceIntegrityTests.SimpleTypeName(type.Name);
                surface.Add((simple + "." + prop.Name, prop.Name, prop.PropertyType.Name));
            }
        }

        return surface
            .GroupBy(s => s)
            .Select(g => g.First())
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .ToList();
    }

    // =================================================================
    // ORM 终端族扫描面
    // 扫描面与台账**同源于公开面快照**（候选 B）：零引用连带、零 lock 变更；
    // 快照本身即 CI 门（公开面变更即快照 diff），故「新增公开终端未登台账 → UNREGISTERED 红」
    // 的机制等价于反射面。解析走 Roslyn（与 CoverageCountScanner 同一工具链）。
    // =================================================================

    /// <summary>Dapper 公开面快照相对路径——ORM 面与方言面共用同一份快照；提为常量防快照重命名时多处漏改（审计 F-3）。</summary>
    internal const string DapperPublicApiSnapshot = "test/NCMapper.Tests/PublicApi.NCMapper.Dapper.approved.txt";

    /// <summary>入面的 ORM selector 类型：<c>(公开面快照相对路径, 类型简名)</c>。</summary>
    private static readonly (string Snapshot, string TypeSimpleName)[] OrmInSurfaceTypes =
    {
        (DapperPublicApiSnapshot, "DapperDtoSelector"),
        ("test/NCMapper.Tests/PublicApi.NCMapper.EFCore.approved.txt", "EFCoreDtoSelector"),
        ("test/NCMapper.Tests/PublicApi.NCMapper.FreeSql.approved.txt", "FreeSqlDtoSelector"),
        ("test/NCMapper.Tests/PublicApi.NCMapper.FreeSql.approved.txt", "FreeSqlComposeDtoSelector"),
        ("test/NCMapper.Tests/PublicApi.NCMapper.FreeSql.approved.txt", "FreeSqlComposeDtoSelector3"),
    };

    /// <summary>入面类型简名（面分类判据用；与 <see cref="OrmInSurfaceTypes"/> 同集）。</summary>
    internal static readonly string[] OrmSurfaceTypeNames =
    {
        "DapperDtoSelector", "EFCoreDtoSelector", "FreeSqlDtoSelector",
        "FreeSqlComposeDtoSelector", "FreeSqlComposeDtoSelector3",
    };

    // =================================================================
    // SQL 方言适配面
    // 面源 = 公开面快照 Roslyn 解析（沿 #217 同源口径），但**语法节点类型独立**：
    // 本分支只取 ConstructorDeclarationSyntax（类型级实例化行使），**不触碰**
    // EnumerateOrmTerminalSurface 的 MethodDeclarationSyntax——改后者会连带扰动既有
    // 80 行 ORM 扫描面与三面计数断言（GF-T2e）。
    // =================================================================

    /// <summary>入面的 SQL 方言适配类型：<c>(公开面快照相对路径, 类型简名)</c>。</summary>
    internal static readonly (string Snapshot, string TypeSimpleName)[] DialectInSurfaceTypes =
    {
        (DapperPublicApiSnapshot, "Db2Dialect"),
        (DapperPublicApiSnapshot, "MySqlDialect"),
        (DapperPublicApiSnapshot, "OracleDialect"),
        (DapperPublicApiSnapshot, "PostgreSqlDialect"),
        (DapperPublicApiSnapshot, "SqlServerDialect"),
        (DapperPublicApiSnapshot, "SqliteDialect"),
        (DapperPublicApiSnapshot, SqlDialectResolverTypeName),
    };

    /// <summary>方言解析静态入口的类型简名（无构造行，由方言分支显式纳入 <c>Resolve</c> 一行）。</summary>
    internal const string SqlDialectResolverTypeName = "SqlDialectResolver";

    /// <summary>入面方言类型简名（面分类判据用；与 <see cref="DialectInSurfaceTypes"/> 同集）。</summary>
    internal static readonly string[] DialectSurfaceTypeNames =
    {
        "Db2Dialect", "MySqlDialect", "OracleDialect", "PostgreSqlDialect",
        "SqlServerDialect", "SqliteDialect", SqlDialectResolverTypeName,
    };

    /// <summary>
    /// 方言行计数模式（由类型简名<b>机械派生</b>——台账行键串不得手写，B12）：
    /// 构造行使 = <c>new &lt;类型简名&gt;(</c>（各类型互不相同 → 天然不触发 PATTERN-COLLISION）；
    /// 静态入口 = <c>SqlDialectResolver.Resolve</c>（含泛型形态 <c>&lt;</c> 以对齐既有模式口径）。
    /// </summary>
    internal static string DialectCountPattern(string surface)
        => surface == SqlDialectResolverTypeName
            ? @"SqlDialectResolver\s*\.\s*Resolve\s*[(<]"
            : @"new " + surface + @"\s*\(";

    /// <summary>
    /// 不入面清单（逐条理由）。类头注「检出力边界」登记替代可见性手段 = 公开面快照门（+ 编译期错误引导）。
    /// <para><b>方言适配类已由移入 <see cref="DialectInSurfaceTypes"/> 入面
    /// （类型级实例化行使），本清单只保留不可实例化的抽象基类</b>（X5）。</para>
    /// </summary>
    internal static readonly (string Type, string Reason)[] OrmExcludedSurface =
    {
        ("NCMapperDapperExtensions", "静态扩展入口类（AsDto）——其可达性由入面 selector 终端计数间接承载；且 AsDto 与 guard 面/链式形态文本同形，入面即被普通调用灌高、仍恒绿"),
        ("NCMapperFreeSqlExtensions", "静态扩展入口类（AsDto）——同上"),
        ("NCMapperEFCoreExtensions", "静态扩展入口类（AsDto/终端转发）——同上"),
        ("NCMapperFreeSqlComposeExtensions", "compose 入口扩展类（AsDto&lt;T1,T2&gt;/&lt;T1,T2,T3&gt;）——同上"),
        ("NCMapperFreeSqlGuardExtensions", "guard 面：AsDto&lt;TKey,TValue&gt; 与链式 .AsDto( 文本同形，文件子集过滤亦不可辨异 → 入面即被普通 AsDto 调用灌高、恒绿"),
        ("NCMapperEFCoreGuardExtensions", "guard 面：全部方法标 [Obsolete(..., error: true)]，编译期即错、不可能被用例行使 → 入面必为 0 值行（按 0 值行闭合规则只能排除）"),
        ("LimitOffsetDialect", "SQL 方言适配类（ISqlDialect 实现，X5）——<b>抽象类不可实例化 → 类型级行使（new 表达式）不可写</b>，入面必为 0 值行；其可见性由公开面快照门承载。具体方言类与 SqlDialectResolver 已入面，见 DialectInSurfaceTypes"),
    };

    /// <summary>
    /// 入面类型内、测试行使数为 0 的终端（0 值行闭合规则 (b)：不入扫描面，逐条给理由）。
    /// <para>填 0 或删行留红均被 F9 阻断；本清单由探索项 3 首跑实测产生。</para>
    /// </summary>
    internal static readonly (string Key, string Reason)[] OrmZeroCountExcluded =
    {
    };

    /// <summary>
    /// 既有 Fluent 面的<b>跨类同名模式</b>白名单（9 组）：这些模式在 <c>MemberHead</c>／<c>MemberConfig</c>／
    /// <c>AfterIgnore</c>／<c>FluentTypeConfig</c> 等面间共享，属建立两面时的既有口径。
    /// <para>本册 F8 锁定既有 57 行的 <c>Name/ShapeKey/MinCount/CountPattern</c> 语义不得改，故不订正其模式，
    /// 只登记为已知碰撞；<b>任何新增的跨面同模式都会被判红</b>（见 <see cref="FindLedgerCompletenessFailures"/>
    /// 与自证 ⑱）。若要清偿这 9 组，须由方案层裁定重载/跨类级辨异口径并重算其下限。</para>
    /// </summary>
    internal static readonly string[] KnownCrossSurfaceCollisions =
    {
        @"\.NoAutoInclude\s*[(<]",
        @"\.FromSource\s*[(<]",
        @"\.Convert\s*[(<]",
        @"\.ConvertBack\s*[(<]",
        @"\.Format\s*[(<]",
        @"\.FromJson\s*[(<]",
        @"\.MapTree\s*[(<]",
        @"\.MapWhen\s*[(<]",
        @"\.Mask\s*[(<]",
    };

    /// <summary>
    /// 既有 Fluent 面的<b>同面同名重载共享模式</b>豁免清单（9 组 / 26 行）：
    /// 这些组在建立两面时即按「同名签名行共享模式故计数相同」登记，属 F8 锁定语义，本册不改其模式。
    /// <para>逐组理由＝F8 锁定面解锁须方案层裁定重载级辨异口径并重算其下限（X10）；本册只清偿 Dapper 11 组。
    /// <b>任何新增的同面共享模式（含本册 Dapper 11 组若未辨异）都会被判红</b>
    /// （见 <see cref="FindLedgerCompletenessFailures"/> 的 <c>OVERLOAD-SHARED-PATTERN</c> 与自证 ㉑）。
    /// 键 = <c>Surface.Name</c>（与台账列同形）。</para>
    /// </summary>
    internal static (string Key, string Reason)[] KnownOverloadCollisions =
    {
        ("FluentTypeConfig.MapCtorParam", "F8 锁定面：构造函数参数映射与同名属性映射共享计数模式；解锁须方案层裁定重载级辨异口径（X10）"),
        ("FluentTypeConfig.Tree", "F8 锁定面：树配置与同名属性映射共享计数模式；解锁须方案层裁定（X10）"),
        ("MemberConfig.Convert", "F8 锁定面：成员级 Convert 重载共享计数模式；解锁须方案层裁定（X10）"),
        ("MemberConfig.ConvertBack", "F8 锁定面：成员级 ConvertBack 重载共享计数模式；解锁须方案层裁定（X10）"),
        ("MemberConfig.MapWhen", "F8 锁定面：成员级 MapWhen 重载共享计数模式；解锁须方案层裁定（X10）"),
        ("MemberHead.Convert", "F8 锁定面：成员头 Convert 重载共享计数模式；解锁须方案层裁定（X10）"),
        ("MemberHead.ConvertBack", "F8 锁定面：成员头 ConvertBack 重载共享计数模式；解锁须方案层裁定（X10）"),
        ("MemberHead.Ignore", "F8 锁定面：成员头 Ignore 重载共享计数模式；解锁须方案层裁定（X10）"),
        ("MemberHead.MapWhen", "F8 锁定面：成员头 MapWhen 重载共享计数模式；解锁须方案层裁定（X10）"),
    };

    /// <summary>
    /// 豁免键固定白名单（M8 补强）——除既有 9 键外，清单出现**任何新增键**
    /// （不论真键废键）一律判红。
    /// <para>补强理由：仅靠"反向存在性核"（清单键须在实台账命中组中存在）只能堵**废键**；
    /// 若新增键在实台账真实存在（正是"顺手豁免应判红形态"的形态），存在性核**不报警**，豁免仍静默生效。
    /// 固定白名单使"顺手加一行豁免"在机械层不可行。</para>
    /// <para>新增豁免的正确路径＝方案层裁定 ＋ 类头注登记理由 ＋ 同步扩本白名单（三步留痕）。</para>
    /// </summary>
    internal static string[] FrozenOverloadWhitelist =
    {
        "FluentTypeConfig.MapCtorParam",
        "FluentTypeConfig.Tree",
        "MemberConfig.Convert",
        "MemberConfig.ConvertBack",
        "MemberConfig.MapWhen",
        "MemberHead.Convert",
        "MemberHead.ConvertBack",
        "MemberHead.Ignore",
        "MemberHead.MapWhen",
    };

    /// <summary>
    /// ORM 面同名辨异过滤器：面名 → 测试文件路径正则（作用于仓库相对路径 <c>test/...</c>，大小写无关）。
    /// <para>缺省 Name 派生模式在三扩展与 LINQ 同名终端上会串计（无辨异口径的命中量级 = LINQ 同名量级，
    /// 由自证 ⑮ 每次运行实测，本注不写死数字），下限被灌高而门禁对该终端实际失效——
    /// 故 ORM 面计数限定在对应扩展的用例文件子集内。链前缀单独不足以隔离跨面，本过滤器是必要第二道防线。</para>
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> OrmSurfaceFileFilters =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DapperDtoSelector"] = @"^test/NCMapper\.(IntegrationTests|Tests)/[^/]*Dapper[^/]*\.cs$",
            ["EFCoreDtoSelector"] = @"^test/NCMapper\.(IntegrationTests|Tests)/[^/]*(EFCore|DualEngine)[^/]*\.cs$",
            ["FreeSqlDtoSelector"] = @"^test/NCMapper\.(IntegrationTests|Tests)/(?!.*Compose)[^/]*FreeSql[^/]*\.cs$",
            ["FreeSqlComposeDtoSelector"] = @"^test/NCMapper\.(IntegrationTests|Tests)/(?=.*FreeSql)[^/]*\.cs$",
            ["FreeSqlComposeDtoSelector3"] = @"^test/NCMapper\.(IntegrationTests|Tests)/(?=.*FreeSql)[^/]*\.cs$",
        };

    /// <summary>面归属判据：属性面（既有启发式的单点化）。</summary>
    private static bool IsAttributeSurface(string surface)
        => surface.EndsWith("Attribute", StringComparison.Ordinal);

    /// <summary>面归属判据：ORM 终端面（入面类型简名集合判定，替换散落的 EndsWith 启发式）。</summary>
    private static bool IsOrmSurface(string surface)
        => OrmSurfaceTypeNames.Contains(surface, StringComparer.Ordinal);

    /// <summary>
    /// 面归属判据：SQL 方言适配面（入面；入面类型简名集合判定）。
    /// <para><b>必须在 <see cref="IsFluentSurface"/> 之前判定</b>——否则方言行会被并入 Fluent 面计数，
    /// 使 <c>ExpectedFluentSignatureRows</c> 与实际双射键集不一致（方言面是独立第四面）。</para>
    /// </summary>
    internal static bool IsDialectSurface(string surface)
        => DialectSurfaceTypeNames.Contains(surface, StringComparer.Ordinal);

    /// <summary>面归属判据：Fluent 签名面 = 非属性面且非 ORM 面且非方言面。</summary>
    private static bool IsFluentSurface(string surface)
        => !IsAttributeSurface(surface) && !IsOrmSurface(surface) && !IsDialectSurface(surface);

    /// <summary>
    /// ORM 终端面：解析公开面快照（Roslyn）→ <c>(键, 方法名, 形态键)</c>；
    /// 键 = 类型简名 + "." + 方法名（与台账 <c>Surface</c> 列同形，双射键合成规则见 <see cref="RunSignatureCoverageCheck"/>）。
    /// <para>排除：<see cref="OrmExcludedSurface"/> 的类型（不入面）与 <see cref="OrmZeroCountExcluded"/> 的 0 值终端
    /// （0 值行闭合规则 (b)）；类型在快照中解析不到即抛——防枚举源漂移致恒绿。</para>
    /// </summary>
    internal static List<(string Key, string Name, string ShapeKey)> EnumerateOrmTerminalSurface()
    {
        var root = FindRepoRoot();
        var excludedKeys = OrmZeroCountExcluded.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var surface = new List<(string Key, string Name, string ShapeKey)>();

        foreach (var (snapshot, typeName) in OrmInSurfaceTypes)
        {
            var classDecl = ParseSnapshotClass(root, snapshot, typeName);

            var classTypeParams = classDecl.TypeParameterList?.Parameters
                                  .Select(p => p.Identifier.ValueText).ToList()
                                  ?? new List<string>();

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                    continue;   // 入口扩展类方法不入面（快照内以 static + this 参数形态出现）

                var methodTypeParams = method.TypeParameterList?.Parameters
                                       .Select(p => p.Identifier.ValueText).ToList()
                                       ?? new List<string>();

                var parts = new List<string>();
                foreach (var p in method.ParameterList.Parameters)
                {
                    if (p.Default != null) continue;   // 规则 ②：剔除带默认值的可选参数
                    parts.Add(NormalizeSnapshotTypeText(p.Type!.ToString(), classTypeParams, methodTypeParams));
                }

                var name = method.Identifier.ValueText;
                var key = typeName + "." + name;
                var shapeKey = methodTypeParams.Count + "|[" + string.Join(",", parts) + "]";
                if (excludedKeys.Contains(key + "@" + shapeKey)) continue;
                surface.Add((key, name, shapeKey));
            }
        }

        return surface
            .GroupBy(s => s)
            .Select(g => g.First())
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .ThenBy(s => s.ShapeKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// SQL 方言适配面：解析公开面快照（Roslyn，沿 #217 同源口径）→
    /// <c>(键, 名称, 形态键)</c>。
    /// <list type="bullet">
    /// <item><b>粒度 = 类型级 ctor 行使</b>（用户裁定）：<see cref="DialectInSurfaceTypes"/> 内每个
    /// <b>具体</b>类型产一行——<c>Surface</c> = 类型简名、<c>Name</c> = <c>ctor</c>、<c>ShapeKey</c> = <c>0|[]</c>，
    /// 下限 = 该类型被 <c>new</c> 实例化的行使测试方法数；</item>
    /// <item><b>静态入口</b>：<c>SqlDialectResolver</c> 无构造行，显式纳入 <c>Resolve</c> 一行；</item>
    /// <item><b>成员行显式抑制</b>（GF-T2b／X11）：本分支<b>只取 ctor</b>，绝不取
    /// <see cref="MethodDeclarationSyntax"/>——否则 <c>Db2Dialect</c>／<c>OracleDialect</c>／
    /// <c>SqlServerDialect</c> 的 public <c>CountSql</c>／<c>PageSql</c> 会被自动产出，
    /// 与类型级一行重复 → <c>UNREGISTERED</c>；</item>
    /// <item><b>抽象类不产</b>：快照内 <c>protected Xxx()</c> 一类非 public 构造（含
    /// <c>LimitOffsetDialect</c>）被跳过，且该类型本就不在 <see cref="DialectInSurfaceTypes"/> 内。</item>
    /// </list>
    /// <para>本分支与 <see cref="EnumerateOrmTerminalSurface"/> <b>语法节点类型互相独立</b>——
    /// 后者固定取 <c>MethodDeclarationSyntax</c>（GF-T2e：改它会连带扰动既有 80 行扫描面与三面计数断言）。
    /// 类型在快照中解析不到即抛——防枚举源漂移致恒绿（vacuous 护栏）。</para>
    /// </summary>
    internal static List<(string Key, string Name, string ShapeKey)> EnumerateDialectSurface()
    {
        var root = FindRepoRoot();
        var surface = new List<(string Key, string Name, string ShapeKey)>();

        foreach (var (snapshot, typeName) in DialectInSurfaceTypes)
        {
            var classDecl = ParseSnapshotClass(root, snapshot, typeName);

            if (typeName == SqlDialectResolverTypeName)
            {
                // 静态入口：无构造行，显式纳入 Resolve（快照内 public static 方法声明）
                var hasResolve = classDecl.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.ValueText == "Resolve"
                              && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                if (!hasResolve)
                    throw new InvalidOperationException(
                        $"公开面快照内未找到 {typeName}.Resolve 的 public 方法声明（方言扫描面枚举源失效）");
                surface.Add((typeName + ".Resolve", "Resolve", "0|[]"));
                continue;
            }

            // 类型级 ctor 行使：仅 public 实例构造（快照内非 public 构造以修饰符缺省/protected 出现）
            var publicCtor = classDecl.Members.OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));
            if (publicCtor == null)
                throw new InvalidOperationException(
                    $"公开面快照内 {typeName} 无 public 构造声明（抽象/静态类不可作类型级行使面，枚举源失效）");

            surface.Add((typeName + ".ctor", "ctor", "0|[]"));
        }

        return surface
            .GroupBy(s => s)
            .Select(g => g.First())
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .ThenBy(s => s.ShapeKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>快照解析缓存（同一文件多次入面时复用）。</summary>
    private static readonly Dictionary<string, SyntaxNode> s_snapshotCache =
        new(StringComparer.Ordinal);

    private static SyntaxNode ParseSnapshotRoot(string repoRoot, string snapshotRelative)
    {
        var path = PathFor(repoRoot, snapshotRelative);
        if (!s_snapshotCache.TryGetValue(path, out var root))
        {
            if (!File.Exists(path))
                throw new InvalidOperationException($"公开面快照缺失: {snapshotRelative}（ORM 扫描面枚举源失效）");
            root = CSharpSyntaxTree
                .ParseText(File.ReadAllText(path)).GetRoot();
            s_snapshotCache[path] = root;
        }

        return root;
    }

    private static ClassDeclarationSyntax ParseSnapshotClass(
        string repoRoot, string snapshotRelative, string typeName)
        => ParseSnapshotRoot(repoRoot, snapshotRelative)
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.ValueText == typeName)
        ?? throw new InvalidOperationException($"公开面快照内未找到类型 {typeName}（{snapshotRelative}，枚举源失效）");

    /// <summary>C# 关键字 → CLR 简单名（与反射侧 <c>Type.Name</c> 对齐）。</summary>
    private static readonly Dictionary<string, string> SnapshotKeywordAliases = new(StringComparer.Ordinal)
    {
        ["bool"] = "Boolean", ["byte"] = "Byte", ["sbyte"] = "SByte", ["char"] = "Char",
        ["decimal"] = "Decimal", ["double"] = "Double", ["float"] = "Single", ["int"] = "Int32",
        ["uint"] = "UInt32", ["long"] = "Int64", ["ulong"] = "UInt64", ["short"] = "Int16",
        ["ushort"] = "UInt16", ["object"] = "Object", ["string"] = "String",
    };

    /// <summary>值类型关键字（可空形态须归一为 <c>Nullable&lt;T&gt;</c> 与反射侧对齐）。</summary>
    private static readonly HashSet<string> SnapshotValueTypeKeywords = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int",
        "uint", "long", "ulong", "short", "ushort",
    };

    /// <summary>
    /// 快照参数类型文本 → 形态键片段（与 <see cref="BuildShapeKey"/> 同一规范化口径：
    /// 类级形参 <c>^n</c> / 方法级形参 <c>!n</c>、去命名空间限定、去 <c>`n</c>、可空值类型 → <c>Nullable&lt;T&gt;</c>）。
    /// </summary>
    private static string NormalizeSnapshotTypeText(string text, List<string> classTypeParams, List<string> methodTypeParams)
    {
        var t = text.Trim();

        if (t.EndsWith("[]", StringComparison.Ordinal))
            return NormalizeSnapshotTypeText(t[..^2], classTypeParams, methodTypeParams) + "[]";

        if (t.EndsWith("?", StringComparison.Ordinal))
        {
            var inner = t[..^1];
            var normalizedInner = NormalizeSnapshotTypeText(inner, classTypeParams, methodTypeParams);
            return SnapshotValueTypeKeywords.Contains(inner.Trim())
                ? "Nullable<" + normalizedInner + ">"
                : normalizedInner;
        }

        if (classTypeParams.Contains(t, StringComparer.Ordinal)) return "^" + classTypeParams.IndexOf(t);
        if (methodTypeParams.Contains(t, StringComparer.Ordinal)) return "!" + methodTypeParams.IndexOf(t);

        var lt = t.IndexOf('<');
        if (lt > 0 && t.EndsWith(">", StringComparison.Ordinal))
        {
            var def = SimpleSnapshotName(t[..lt]);
            var args = SplitTopLevelGenericArgs(t[(lt + 1)..^1]);
            return def + "<" + string.Join(",", args.Select(a => NormalizeSnapshotTypeText(a, classTypeParams, methodTypeParams))) + ">";
        }

        // C# 关键字别名 → CLR 简单名（与反射侧 Type.Name 对齐）
        return SnapshotKeywordAliases.TryGetValue(t, out var alias) ? alias : SimpleSnapshotName(t);
    }

    private static string SimpleSnapshotName(string typeText)
    {
        var trimmed = typeText.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        var name = lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed;
        var tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    private static List<string> SplitTopLevelGenericArgs(string argsText)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < argsText.Length; i++)
        {
            var c = argsText[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(argsText[start..i]);
                start = i + 1;
            }
        }
        parts.Add(argsText[start..]);
        return parts;
    }

    /// <summary>四面扫描面集合（Fluent 签名面 / 特性属性面 / ORM 终端面 / SQL 方言适配面）——真实核查与自证共用。</summary>
    private static List<(string Key, string Name, string ShapeKey)>[] AllSurfaces() => new List<(string Key, string Name, string ShapeKey)>[]
    {
        EnumerateFluentSignatureSurface(),
        EnumerateAttributePropertySurface(),
        EnumerateOrmTerminalSurface(),
        EnumerateDialectSurface(),
    };

    /// <summary>
    /// 同源对照（探索项 1 判据）：快照解析器对<b>已反射在册</b>的两面（Fluent 46 + 属性 11）
    /// 产出结果须与反射枚举逐键相等——100% 一致才允许把该解析器用于 ORM 面。
    /// </summary>
    [Fact]
    public void SnapshotParser_SameSource_ControlAgainst_ReflectiveSurfaces()
    {
        var root = FindRepoRoot();

        var reflectedFluent = EnumerateFluentSignatureSurface();
        var parsedFluent = new List<(string Key, string Name, string ShapeKey)>();
        foreach (var typeName in FluentConfigTypeNames)
        {
            var simple = FluentSurfaceIntegrityTests.SimpleTypeName(typeName.Split('.').Last());
            var classDecl = ParseSnapshotClass(root, "test/NCMapper.Tests/PublicApi.NCMapper.approved.txt", simple);
            var classTypeParams = classDecl.TypeParameterList?.Parameters
                                  .Select(p => p.Identifier.ValueText).ToList() ?? new List<string>();
            foreach (var m in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodTypeParams = m.TypeParameterList?.Parameters
                                       .Select(p => p.Identifier.ValueText).ToList() ?? new List<string>();
                parsedFluent.Add((simple + "." + m.Identifier.ValueText, m.Identifier.ValueText,
                    BuildShapeKeyFromSnapshot(m, classTypeParams, methodTypeParams)));
            }
        }

        var reflectedAttrs = EnumerateAttributePropertySurface();
        var parsedAttrs = ParseSnapshotClass(root, "test/NCMapper.Tests/PublicApi.NCMapper.approved.txt", "MapDefaultsAttribute")
            .Members.OfType<PropertyDeclarationSyntax>()
            .Select(p => ("MapDefaultsAttribute." + p.Identifier.ValueText, p.Identifier.ValueText,
                NormalizeSnapshotTypeText(p.Type.ToString(), new List<string>(), new List<string>())))
            .ToList();

        var diffs = new List<string>();
        DiffSurface(reflectedFluent, parsedFluent, "Fluent", diffs);
        DiffSurface(reflectedAttrs, parsedAttrs, "Attribute", diffs);

        Assert.True(diffs.Count == 0, "同源对照失败（快照解析器与反射枚举不一致，禁用于 ORM 面）:\n" + string.Join("\n", diffs));
    }

    private static void DiffSurface(
        List<(string Key, string Name, string ShapeKey)> reflected,
        List<(string Key, string Name, string ShapeKey)> parsed,
        string label,
        List<string> diffs)
    {
        var r = reflected.Select(x => x.Key + "@" + x.ShapeKey).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var p = parsed.Select(x => x.Key + "@" + x.ShapeKey).OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (var miss in r.Except(p, StringComparer.Ordinal)) diffs.Add($"{label}: 反射有而解析无 → {miss}");
        foreach (var extra in p.Except(r, StringComparer.Ordinal)) diffs.Add($"{label}: 解析有而反射无 → {extra}");
    }

    private static string BuildShapeKeyFromSnapshot(
        MethodDeclarationSyntax method,
        List<string> classTypeParams, List<string> methodTypeParams)
    {
        var parts = new List<string>();
        foreach (var p in method.ParameterList.Parameters)
        {
            if (p.Default != null) continue;
            parts.Add(NormalizeSnapshotTypeText(p.Type!.ToString(), classTypeParams, methodTypeParams));
        }
        return methodTypeParams.Count + "|[" + string.Join(",", parts) + "]";
    }

    /// <summary>
    /// 入面 / 不入面全类型对账（验收 1 的机械交叉核对）：三份 ORM 公开面快照内的<b>全部公开类型</b>
    /// 必须非入面即已登记排除——差集为空即「扫描面漏类型致整面恒绿」在类型层被堵住。
    /// <para>入面集 = <see cref="OrmInSurfaceTypes"/> ∪ <see cref="DialectInSurfaceTypes"/>（方言面并入后，
    /// 类型对账须同时覆盖两族，否则方言类会被误判为「既不入面也未排除」）。</para>
    /// </summary>
    [Fact]
    public void OrmSurfaceCensus_EveryPublicTypeIsInSurfaceOrExcluded()
    {
        var root = FindRepoRoot();
        var snapshots = new[]
        {
            DapperPublicApiSnapshot,
            "test/NCMapper.Tests/PublicApi.NCMapper.EFCore.approved.txt",
            "test/NCMapper.Tests/PublicApi.NCMapper.FreeSql.approved.txt",
        };

        var inSurface = OrmInSurfaceTypes.Select(t => t.TypeSimpleName)
            .Concat(DialectInSurfaceTypes.Select(t => t.TypeSimpleName))
            .ToHashSet(StringComparer.Ordinal);
        var excluded = OrmExcludedSurface.Select(e => e.Type).ToHashSet(StringComparer.Ordinal);

        var unaccounted = new List<string>();
        foreach (var snapshot in snapshots)
        {
            var tree = ParseSnapshotRoot(root, snapshot);
            foreach (var cls in tree.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!cls.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) continue;
                var name = cls.Identifier.ValueText;
                if (!inSurface.Contains(name) && !excluded.Contains(name))
                    unaccounted.Add($"{snapshot} :: {name}");
            }
        }

        Assert.True(unaccounted.Count == 0,
            "ORM 面类型对账缺口（既不入面也未登记排除）:\n" + string.Join("\n", unaccounted));
    }

    /// <summary>
    /// 方言面类型对账（验收 2 第二维）：<see cref="DialectInSurfaceTypes"/> 内每个类型
    /// 在扫描面内<b>恰好产出一行</b>——0 行 = 该类型入面清单写了而枚举器没产（分支装饰化、
    /// 类型被静默跳过）；≥2 行 = 成员行未被抑制（<c>CountSql</c>／<c>PageSql</c> 泄漏入面）。
    /// <para>两侧均由枚举器实产决定，不由手写常量推断（B12）；行键与台账逐字符一致由
    /// <see cref="SignatureCoverage_CountGate_And_ShapeKeyBijection"/> 的双射判据另行钉住。</para>
    /// </summary>
    [Fact]
    public void DialectSurfaceCensus_EveryDialectTypeProducesExactlyOneRow()
    {
        var surface = EnumerateDialectSurface();
        var failures = new List<string>();

        foreach (var (_, typeName) in DialectInSurfaceTypes)
        {
            var n = surface.Count(s => s.Key.StartsWith(typeName + ".", StringComparison.Ordinal));
            if (n != 1)
                failures.Add($"方言面类型 {typeName} 实产 {n} 行 ≠ 1（0 = 枚举分支未承重/类型被跳过；≥2 = 成员行未被抑制）");
        }

        // 成员级终端不得入面（X11）：抑制生效的机械判据
        foreach (var name in new[] { "CountSql", "PageSql", "ParameterPrefix" })
        {
            var leaked = surface.Where(s => s.Name == name).Select(s => s.Key).ToList();
            foreach (var k in leaked)
                failures.Add($"方言面成员行未被抑制（X11）→ {k}（类型级入面不得自动产出成员行）");
        }

        Assert.True(failures.Count == 0,
            "方言面类型对账失败:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// 形态键构建：<c>(方法级类型参数个数, 规范化参数类型序列)</c>。
    /// <para>规范化：开放泛型形参 → 占位记号；带默认值的可选参数<b>剔除</b>；类型名去 arity 后缀。</para>
    /// <para><b>类级形参按位置编号（关键）</b>：记<code>^0</code>/<code>^1</code>/<code>^2</code>，
    /// <b>不得全部归一为同一记号</b>——否则 <c>ConvertBack(Func&lt;TProp,TEntityProp&gt;)</c> 与
    /// <c>ConvertBack(Func&lt;TTarget,TEntityProp&gt;)</c> 会折叠成同一键（<c>TProp</c> 与 <c>TTarget</c> 都是
    /// 类级形参），四重载实测塌成三键——正是「重载折叠致某重载零调用而门禁绿」的盲区形态。
    /// 方法级形参记 <c>!n</c>（位置编号）。</para>
    /// </summary>
    internal static string BuildShapeKey(MethodInfo method)
    {
        var genericCount = method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0;

        var parts = new List<string>();
        foreach (var p in method.GetParameters())
        {
            if (p.HasDefaultValue) continue;   // 规则 ②：剔除可选参数
            parts.Add(NormalizeParameterType(p.ParameterType, genericCount));
        }

        return genericCount + "|[" + string.Join(",", parts) + "]";
    }

    /// <summary>参数类型规范化（规则 ①+ ③；类级形参位置编号见方法注释）。</summary>
    private static string NormalizeParameterType(Type t, int genericCount)
    {
        if (t.IsByRef) return NormalizeParameterType(t.GetElementType()!, genericCount);

        if (t.IsArray)
        {
            var elem = NormalizeParameterType(t.GetElementType()!, genericCount);
            return t.GetArrayRank() == 1 ? elem + "[]" : elem + "[" + new string(',', t.GetArrayRank() - 1) + "]";
        }

        if (t.IsGenericParameter)
        {
            // 方法级 → !n（位置编号）；类级 → ^n（位置编号，**不可省略位置**）
            return t.DeclaringMethod != null
                ? "!" + t.GenericParameterPosition
                : "^" + t.GenericParameterPosition;
        }

        if (t.IsConstructedGenericType || t.IsGenericTypeDefinition)
        {
            var def = t.IsGenericTypeDefinition ? t : t.GetGenericTypeDefinition();
            var name = FluentSurfaceIntegrityTests.SimpleTypeName(def.Name);
            if (!t.IsGenericTypeDefinition)
            {
                var args = t.GetGenericArguments()
                             .Select(a => NormalizeParameterType(a, genericCount));
                return name + "<" + string.Join(",", args) + ">";
            }
            return name;
        }

        return FluentSurfaceIntegrityTests.SimpleTypeName(t.Name);
    }

    // =================================================================
    // 纯核查逻辑（真实核查与自证共用）
    // =================================================================

    /// <summary>
    /// 五类红纯核查（<paramref name="rows"/> 为可变副本，自证传入变异体；
    /// <paramref name="counts"/> 为可注入计数副本，缺省 null 表示不核计数下限）。
    /// <list type="bullet">
    /// <item>UNREGISTERED：扫描面 ⊖ 台账；STALE：台账 ⊖ 扫描面；</item>
    /// <item>UNCOVERED：台账行锚点为标记；ANCHOR：锚点文件/方法不存在；</item>
    /// <item>UNDERCOUNT：行使测试方法数 &lt; 台账登记下限（<paramref name="counts"/> 非 null 时）；</item>
    /// <item>VACUOUS：扫描面为空 / 计数源为空。</item>
    /// </list>
    /// </summary>
    internal static List<string> RunSignatureCoverageCheck(
        IEnumerable<(string Key, string Name, string ShapeKey)>[] surfaces,
        IEnumerable<(string Surface, string Name, string ShapeKey, string Anchor, int MinCount, string CountPattern)> rows,
        IReadOnlyDictionary<string, int>? counts = null,
        string? repoRoot = null)
    {
        var scanned = surfaces.SelectMany(s => s).ToList();
        var failures = new List<string>();

        // vacuous 护栏：扫描面为空 → 恒绿风险，立即判红
        if (scanned.Count == 0)
        {
            failures.Add("VACUOUS: 扫描面枚举结果为 0 条——反射口径漂移 / 类型改名将使本门禁恒绿，须立即排查");
            return failures;
        }

        var scannedKeys = scanned.Select(s => s.Key + "@" + s.ShapeKey).ToHashSet(StringComparer.Ordinal);
        var rowList = rows.ToList();
        var rowKeys = rowList.Select(r => r.Surface + "." + r.Name + "@" + r.ShapeKey).ToHashSet(StringComparer.Ordinal);

        // ① UNREGISTERED：扫描面 ⊖ 台账
        foreach (var miss in scannedKeys.Except(rowKeys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            failures.Add($"UNREGISTERED: 公开面存在 {miss} 但签名覆盖台账无对应行——新增签名/属性须同批登记（否则后续新增将游离于核验视野）");

        // ② STALE：台账 ⊖ 扫描面
        foreach (var extra in rowKeys.Except(scannedKeys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            failures.Add($"STALE: 台账存在 {extra} 但公开面已无此签名——公开面已撤而台账未清");

        // ③ UNCOVERED：行在台账但覆盖未落地
        foreach (var row in rowList.Where(r => r.Anchor == UncoveredMarker)
                                   .OrderBy(r => r.Surface, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal))
            failures.Add($"UNCOVERED: {row.Surface}.{row.Name}@{row.ShapeKey} 锚点仍为未覆盖标记——须由对应任务补齐用例后更新台账");

        // ④ ANCHOR：锚点文件/方法存在性（存在性弱校验——不判接收者/重载归属，见类头注）
        var root = repoRoot ?? FindRepoRoot();
        foreach (var row in rowList.Where(r => r.Anchor != UncoveredMarker))
        {
            var parts = row.Anchor.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                failures.Add($"ANCHOR: {row.Surface}.{row.Name} 锚点格式非法「{row.Anchor}」——应为 file::method");
                continue;
            }

            var path = PathFor(root, parts[0]);
            if (!File.Exists(path))
            {
                failures.Add($"ANCHOR: {row.Surface}.{row.Name} 锚点文件不存在 → {parts[0]}");
                continue;
            }
            if (parts[1].Length > 0 && !FileContainsMember(path, parts[1]))
                failures.Add($"ANCHOR: {row.Surface}.{row.Name} 锚点方法名未出现在 {parts[0]} → {parts[1]}");
        }

        // ⑤ UNDERCOUNT：行使测试方法数 < 台账登记下限（counts 非 null 时核；缺键按 0 计）
        if (counts != null)
        {
            if (counts.Count == 0)
                failures.Add("VACUOUS: 计数源为 0 条——计数扫描面失效将使计数下限门禁恒绿，须立即排查");

            foreach (var row in rowList.Where(r => r.Anchor != UncoveredMarker))
            {
                var key = row.Surface + "." + row.Name + "@" + row.ShapeKey;
                var n = counts.TryGetValue(key, out var c) ? c : 0;
                if (n < row.MinCount)
                    failures.Add($"UNDERCOUNT: {row.Surface}.{row.Name} 当前 {n} < 下限 {row.MinCount}（@{row.ShapeKey}）——行使该签名的测试方法数低于台账登记下限");
            }
        }

        return failures;
    }

    /// <summary>仓库根定位（向上找 <c>NCMapper.slnx</c>）。</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NCMapper.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static string PathFor(string root, string relative)
        => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>成员名出现性检查（方法名 / 类型名 / 常量名均可——锚点粒度为「含该名的测试面」）。</summary>
    private static bool FileContainsMember(string path, string member)
    {
        try
        {
            return File.ReadAllText(path).Contains(member, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
