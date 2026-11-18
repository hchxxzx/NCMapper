using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen;

/// <summary>
/// <see cref="MappingExtractor"/> 的共享目标属性遍历门分部
/// 把特性侧（<c>ExtractMappingCore</c>）与 Fluent 侧（<c>MappingSnapshotBuilder.Build</c>）同形复制的
/// "继承层次遍历 + GroupBy 最派生优先 + static/indexer 跳过 + public setter 门"结构收敛为单点。
/// <para>
/// <b>行为契约（字节不变门强制）：</b>
/// <list type="bullet">
/// <item>枚举顺序与重构前两侧内联遍历逐位一致（LINQ 链语义不变：GetTypeAndBaseTypes →
/// SelectMany(GetMembers().OfType&lt;IPropertySymbol&gt;()) → GroupBy(Name).Select(First)）。</item>
/// <item>static/indexer 与 setter 门判定条件不变（prop.IsStatic || prop.IsIndexer；
/// prop.SetMethod == null || DeclaredAccessibility != Public）。</item>
/// <item>不可写属性（setter 门命中）回调 <paramref name="onNonWritable"/> 委托——
/// 特性侧传入本地函数 EmitNonWritableDiagnostic（捕获外围 info/sourceType，发射 NCMAP006，
/// 含 IsImplicitlyDeclared + [MapIgnore] 排除逻辑，保留特性侧独有分叉）；Fluent 侧传 null 静默跳过
/// （通道输入镜像——Fluent 不读特性级 [MapIgnore]， 边界约束）。</item>
/// <item>NCMAP006 发射的排除逻辑（IsImplicitlyDeclared + [MapIgnore] 读取）为特性侧独有，
/// <b>不上移</b>共享方法（防 Fluent 通道被特性特性污染）。</item>
/// </list>
/// </para>
/// </summary>
internal static partial class MappingExtractor
{
    /// <summary>
    /// 共享目标属性遍历门——产出可写（public setter/init）目标属性序列（按继承层次 + 最派生优先）。
    /// <para>不可写属性经 <paramref name="onNonWritable"/> 回调（特性侧发射 NCMAP006；Fluent 侧传 null 跳过）。</para>
    /// <para>延迟枚举（yield return）——调用方 foreach 内每步触发 ct 取消检查。</para>
    /// </summary>
    /// <param name="targetType">目标类型符号（继承层次由 GetTypeAndBaseTypes 展开）。</param>
    /// <param name="cancellationToken">编译期取消令牌（每步 ThrowIfCancellationRequested）。</param>
    /// <param name="onNonWritable">不可写属性回调（特性侧发射 NCMAP006；Fluent 侧传 null 静默跳过）。</param>
    /// <returns>可写目标属性序列（含基类属性，最派生优先）。</returns>
    internal static IEnumerable<IPropertySymbol> EnumerateWritableTargetProperties(
        INamedTypeSymbol targetType,
        CancellationToken ct,
        Action<IPropertySymbol>? onNonWritable = null)
    {
        // 遍历目标类型完整继承层次（含基类属性），GroupBy 取最派生优先
        foreach (var prop in GetTypeAndBaseTypes(targetType)
            .SelectMany(t => t.GetMembers().OfType<IPropertySymbol>())
            .GroupBy(p => p.Name).Select(g => g.First()))
        {
            // 长时间编译时响应取消请求，避免阻塞 IDE
            ct.ThrowIfCancellationRequested();
            if (prop.IsStatic || prop.IsIndexer) continue;
            // 仅接受 public setter/init（private init 不可从外部赋值，生成代码会 CS0272）
            if (prop.SetMethod == null || prop.SetMethod.DeclaredAccessibility != Accessibility.Public)
            {
                // 不可写属性回调（特性侧发射 NCMAP006 含排除逻辑；Fluent 侧 null 静默跳过）
                onNonWritable?.Invoke(prop);
                continue;
            }

            yield return prop;
        }
    }

    /// <summary>
    /// 共享 ReverseOnly 条目构建门
    /// 把特性侧（<c>DetectReverseOnlyConvertBackMappings</c>）与 Fluent 侧（<c>BuildReverseOnlyEntry</c>）
    /// 同形复制的"setter 门 → 解析委托 → 组装"门序收敛为单点。
    /// <para>
    /// <b>门序契约（B9 禁止倒置）：</b>
    /// <list type="number">
    /// <item><b>setter 门</b>：无 setter（get-only）或（setter 非 public 且非 init-only）
    /// → 登记 <c>ReverseMappingSkippedRule</c>（消息逐字保持，B10）后返回 false；
    /// init-only 接受（反向映射同样接受 init-only setter）。</item>
    /// <item><b>解析委托</b>：<paramref name="resolveConverter"/> 在 setter 门通过后调用——
    /// 证明门先于解析（"setter 不可写 + 引用无效"场景诊断不漂移）。
    /// 特性侧委托内 <c>TryGetMethodReference</c>/<c>ResolveConverterMethod</c> 失败自发
    /// NCMAP035/036/037 后返回 null，本方法静默返回 false；Fluent 侧 backDecl 提取期已解析，
    /// 委托恒非空。</item>
    /// <item><b>组装</b>：new PropertyMapping（PathOrigin.Direct）→
    /// <c>ApplyConverterMethodReference</c> → <paramref name="fluentOverrideIgnore"/> 豁免置位
    /// （006 新增 Fluent 独有，特性侧恒 false 不传——F4 否定条件）→ Add。</item>
    /// </list>
    /// </para>
    /// <para><b>诊断消息逐字保持</b>（ReverseMappingSkippedRule Args = 属性名/DTO 显示名/实体显示名/
    /// "目标 setter 为 {desc}，无法赋值"——与双侧既有发射点同款，B10）。</para>
    /// </summary>
    /// <param name="info">映射信息（结果追加到此对象）。</param>
    /// <param name="entityProp">实体属性符号（[ConvertBack] 标注属性 / Fluent backDecl.EntityProperty）。</param>
    /// <param name="sourceType">主源类型（实体——[MapFrom].FromSource / Fluent fs.SourceType）。</param>
    /// <param name="targetSymbol">目标类型（DTO——[MapFrom] 标注类 / Fluent fs.TargetType）。</param>
    /// <param name="resolveConverter">解析委托（门后调用）：特性侧封装 TryGetMethodReference + ResolveConverterMethod（失败自发 NCMAP035/036/037 返回 null）；Fluent 侧预解析直传 () => (backDecl.Method, backDecl.Shape)——B 形态Method 为 null（shape 经委托携带）。</param>
    /// <param name="fluentOverrideIgnore">Fluent 独有 overrideIgnore 豁免（特性侧恒 false 不传）——组装后置位 pmBack.FluentOverrideIgnore。</param>
    /// <param name="configureBShape">：B 形态转换器注入委托（生成三元组 + 方法体三字段——Method == null 时由调用方注入；A 形态/特性侧 null 不传）。</param>
    /// <returns>true = 条目已组装入 info.ReverseOnlyConvertBacks；false = setter 门拦截（已发 ReverseMappingSkippedRule）或解析委托返回 null（静默）。</returns>
    internal static bool TryAppendReverseOnlyEntry(
        MappingInfo info,
        IPropertySymbol entityProp,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol targetSymbol,
        Func<(IMethodSymbol? Method, ConverterShape Shape)?> resolveConverter,
        bool fluentOverrideIgnore = false,
        Action<PropertyMapping>? configureBShape = null)
    {
        // setter 门（与正向门同判——非 public 拒绝，init-only
        // 豁免取消——private init 入生成面即 CS0272；原 P1-SHOULD-8 豁免口径废弃，:53 注释矛盾
        // 同步订正）：无 setter（get-only）或 setter 非 public → 登记 ReverseMappingSkippedRule 后跳过
        var setter = entityProp.SetMethod;
        if (setter == null || setter.DeclaredAccessibility != Accessibility.Public)
        {
            var setterDesc = setter == null
                ? "无 setter（get-only）"
                : setter.DeclaredAccessibility.ToString().ToLowerInvariant();
            info.Diagnostics.Add(new DiagnosticInfo
            {
                Descriptor = DiagnosticDescriptors.ReverseMappingSkippedRule,
                Args = new object[] { entityProp.Name, targetSymbol.ToDisplayString(), sourceType.ToDisplayString(), $"目标 setter 为 {setterDesc}，无法赋值" },
                Location = entityProp.Locations.FirstOrDefault()
            });
            return false;
        }

        // 解析委托（门序：setter 门 → 解析——/， B9 禁止倒置）
        // 特性侧委托内 TryGetMethodReference/ResolveConverterMethod 失败自发 NCMAP035/036/037 后返回 null
        // Fluent 侧 backDecl 提取期已解析，委托恒非空（B 形态 Method=null 占位——三元组经 configureBShape 注入）
        var resolved = resolveConverter();
        if (resolved == null) return false;
        var (method, shape) = resolved.Value;

        // 组装（两步式：new PropertyMapping → ApplyConverterMethodReference → overrideIgnore 豁免 → Add）
        var pmBack = new PropertyMapping
        {
            TargetProperty = entityProp,
            SourceProperty = null!,
            SourcePath = new List<IPropertySymbol>(),
            IsFlattened = false,
            PathOrigin = PathOrigin.Direct,
        };
        if (method != null)
        {
            ApplyConverterMethodReference(pmBack, method, shape);
        }
        else
        {
            // B 形态：shape 直写——三元组 + 方法体三字段由 configureBShape 注入
            pmBack.ConverterShape = shape;
        }
        configureBShape?.Invoke(pmBack);
        // ReverseOnly 条目 overrideIgnore 豁免承载——置位 pmBack.FluentOverrideIgnore
        //（物化叠加进 Target（实体属性）.HasOverrideIgnore：ReverseOnly 条目 Source=null，DTO 锚点不在条目内，
        // 豁免经实体侧 PropertyRef 置位实现（ApplyReverseGlobalIgnoreFilter 的 hitTarget 判定豁免——过滤
        // 语义等同；差异登记：ReverseOnly 场景豁免挂实体侧而非 DTO 侧）——Fluent 独有，特性侧恒 false 不传）
        if (fluentOverrideIgnore) pmBack.FluentOverrideIgnore = true;
        info.ReverseOnlyConvertBacks.Add(pmBack);
        return true;
    }
}
