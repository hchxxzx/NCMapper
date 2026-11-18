using NCMapper.Runtime;
using NCMapper.Attributes;
using NCMapper.Internal;
using NCMapper.Shared;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace NCMapper;

internal static partial class ExpressionBuilder
{
    /// <summary>
    /// 构建命名约定扁平化赋值表达式— AddressCity → src.Address.City
    /// <para>与投影内核 TryBuildFlattenedExpression 同算法（候选生成收敛于 PathCandidateGenerator.Build）：
    /// 拆点 ≤4、2-5 段、
    /// 段匹配 OrdinalIgnoreCase（不随命名策略）、中间节点排除值类型与 string、
    /// 候选按段数升序最短路径优先。</para>
    /// <para>null 安全范式同 BuildCrossTableAssignment：从叶向根包裹 Condition(segment != null, next, default)。
    /// 方向守卫由调用方保证（仅 isToDto，目标标 [MapFrom]）。叶节点命中全局忽略清单 → 跳过
    /// （对齐源生成器值模型管线 ApplySourceGlobalIgnoreValue）。
    /// [Mask]/[Convert] 消费：与直接属性分支同形态
    /// Convert 优先（输入 = null 安全后的叶原始值），双标先转换再脱敏；Mask 在类型转换与 null 安全链后消费。</para>
    /// </summary>
    /// <param name="sourceParameter">源对象参数表达式</param>
    /// <param name="targetParameter">目标对象参数表达式</param>
    /// <param name="sourceType">当前源对象类型</param>
    /// <param name="targetItem">目标属性（同名/配置感知匹配失败项）</param>
    /// <param name="registryCtx">注册表腿上下文（本方法与 FromPath 逐层下传；叶节点消费归渲染腿）</param>
    /// <returns>赋值表达式；无匹配路径或类型不兼容时返回 null</returns>
    [RequiresUnreferencedCode("BuildFlattenedAssignment scans type members via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildFlattenedAssignment builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
    private static Expression? BuildFlattenedAssignment(
        ParameterExpression sourceParameter, ParameterExpression targetParameter,
        Type sourceType, PropertyInfo targetItem, RegistryMaskContext? registryCtx = null)
    {
        var targetName = targetItem.Name;

        // 缓存查询（含失败缓存；动态类型直算，继承 PropertyMatcher 判据）
        var useCache = !PropertyMatcher.IsDynamicType(sourceType)
                       && !PropertyMatcher.IsDynamicType(targetItem.DeclaringType!);
        if (useCache && s_flattenPathCache.TryGetValue((sourceType, targetItem), out var cachedPath))
        {
            if (cachedPath == null) return null; // 失败缓存（仅推导扁平化；显式路径失败不进缓存）
            // 推导扁平化无索引段（空索引元数据）
            return BuildFlattenedAssignmentFromPath(cachedPath, Array.Empty<int?>(), sourceParameter, targetParameter, targetItem, registryCtx);
        }

        // 解析路径：枚举候选（段数升序），最短路径优先
        PropertyInfo[]? path = null;
        foreach (var candidate in PathCandidateGenerator.Build(targetName))
        {
            if (candidate.Count <= 1) continue;

            // 段循环收敛为 TypePathResolver（正向尽力式，判据统一
            // CanRead + 中间节点排除值类型/string；候选模式走 segments 重载避免 Join/Split 往返）
            var result = s_typeResolver.Resolve(sourceType, candidate, PathResolutionOptions.ForwardEager, context: null);
            if (result.Success && result.Path.Count > 1) { path = result.Path.ToArray(); break; }
        }

        if (useCache)
            s_flattenPathCache.TryAdd((sourceType, targetItem), path);

        if (path == null)
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetName}（原因：同名/配置感知匹配失败，且命名约定扁平化无匹配路径）");
            return null;
        }

        return BuildFlattenedAssignmentFromPath(path, Array.Empty<int?>(), sourceParameter, targetParameter, targetItem, registryCtx);
    }

    /// <summary>
    /// PathSegmentFailure → DotPathFailureKind 映射（前 5 行）
    /// 显式点路径契约式失败分类；正向解析不会产生 SegmentNotWritable/NotInstantiable，
    /// 其余枚举值无对应 Kind 时视为实现缺陷抛 InvalidOperationException。
    /// </summary>
    /// <param name="failure">统一失败分类</param>
    /// <returns>显式点路径失败 Kind</returns>
    private static DotPathFailureKind MapFailureKind(PathSegmentFailure failure)
        => failure switch
        {
            PathSegmentFailure.SegmentNotFound => DotPathFailureKind.PropertyNotFound,
            PathSegmentFailure.NotNavigable => DotPathFailureKind.NotNavigableValueType,
            PathSegmentFailure.InvalidIndexSyntax => DotPathFailureKind.InvalidIndexSyntax,
            PathSegmentFailure.IndexNotNonNegative => DotPathFailureKind.IndexNotNonNegative,
            PathSegmentFailure.NotIndexableType => DotPathFailureKind.NotIndexableType,
            _ => throw new InvalidOperationException($"无法映射的路径失败分类：{failure}"),
        };

    /// <summary>
    /// 由已解析路径构造扁平化赋值（缓存命中路径复用；赋值期判定不缓存）
    /// <para> （H6 消费）：[Convert]/[Mask] 消费分支经 BuildConverterValue/BuildMaskValue
    /// 反射扫描转换器/脱敏类型成员，须保留 AOT 标注。</para>
    /// <para>：索引段（indexes[i] 非 null）经 <see cref="ChainAccess"/> 生成
    /// ElementAtOrDefault 调用（集合 null → default；空集合/越界 → default），与生成器形态同语义。</para>
    /// </summary>
    [RequiresUnreferencedCode("BuildFlattenedAssignmentFromPath scans type members and attributes via reflection (path leaf, Mask/Convert). Some members may be trimmed.")]
    [RequiresDynamicCode("BuildFlattenedAssignmentFromPath builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
    private static Expression? BuildFlattenedAssignmentFromPath(
        PropertyInfo[] path, int?[] indexes, ParameterExpression sourceParameter,
        ParameterExpression targetParameter, PropertyInfo targetItem,
        RegistryMaskContext? registryCtx = null)
    {
        var targetType = targetItem.PropertyType;
        var leaf = path[path.Length - 1];

        // 叶节点命中全局忽略清单 → 跳过（对齐源生成器值模型管线）
        if (PropertyMatcher.ShouldIgnore(leaf, isToDto: true, PropertyIgnoreChecker.HasOverrideIgnore(targetItem)))
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：扁平化叶节点 {leaf.Name} 命中全局忽略清单）");
            return null;
        }

        // （H6 消费）：路径 × [Convert] 消费——与直接属性分支同形态
        // （ConvertUsing 优先于所有其他转换路径；锚点无 Convert 特性时消费对侧（叶节点）[Convert]，
        // X1-1c 语义与直接分支一致）。转换器输入 = null 安全链后的叶原始值（与直接分支传源字段值同语义，
        // 类型兼容性检查之前消费——对齐 BuildMapBody Convert 分支先于类型分发的位置）。
        // null 安全链 Block 优化 — canBlock（中间段 ≥2 且无值类型
        // 索引中间段）时用临时变量缓存各段访问（每段 getter 恰一次；原各级 Condition test 由
        // ChainAccess 从根重建前缀链，O(d²) 次 getter——实测 5 段路径 14 次 vs 优化后 6 次）。
        // 值类型索引中间段回退原 Condition 嵌套形态（null 传播经值类型段与 Condition 短路语义分叉，
        // 保持原语义；该形态路径 d 通常较小）。
        var depth = path.Length - 1;
        bool canBlock = depth >= 2;
        if (canBlock)
        {
            for (var i = 0; i < depth; i++)
            {
                if (indexes.Length > i && indexes[i] is not null)
                {
                    var elemType = NavigationResolver.GetCollectionElementType(path[i].PropertyType);
                    if (elemType != null && elemType.IsValueType) { canBlock = false; break; }
                }
            }
        }
        List<ParameterExpression>? blockVars = null;
        List<Expression>? blockStmts = null;
        Expression? tailVar = null;
        if (canBlock)
        {
            blockVars = new List<ParameterExpression>(depth);
            blockStmts = new List<Expression>(depth + 1);
            var s0 = AppendSegment(sourceParameter, path[0], indexes.Length > 0 ? indexes[0] : null);
            var v0 = Variable(s0.Type, "np0");
            blockVars.Add(v0);
            blockStmts.Add(Assign(v0, s0));
            for (var i = 1; i < depth; i++)
            {
                var si = AppendSegment(blockVars[i - 1], path[i], indexes.Length > i ? indexes[i] : null);
                var vi = Variable(si.Type, "np" + i);
                blockVars.Add(vi);
                // 前段引用判空（canBlock 保证全引用链）：null → 本段 null（与 Condition 短路语义等价）
                blockStmts.Add(Assign(vi, Condition(
                    NotEqual(blockVars[i - 1], Constant(null, blockVars[i - 1].Type)),
                    si, Constant(null, si.Type))));
            }
            tailVar = blockVars[depth - 1];
        }
        Expression Finalize(Expression assign)
        {
            if (!canBlock) return assign;
            var vars = blockVars!;
            var stmts = blockStmts!;
            stmts.Add(assign);
            return Block(vars, stmts);
        }

        Expression current = canBlock
            ? AppendSegment(tailVar!, path[depth], indexes.Length > depth ? indexes[depth] : null)
            : ChainAccess(sourceParameter, path, indexes, path.Length - 1);

        // MapWhen 守卫（点路径/扁平化属性——生成端 BuildSourceAccess
        // "src.A?.B" 形态镜像）——T1 实参 = null 安全链路径值（中段 null → 谓词收到 null；
        // 转换前求值：谓词实参取 Convert/类型转换消费前的原始路径值）
        Expression? whenGuard = null;
        var whenDecl = PropertyMatcher.TryGetWhenDeclaration(sourceParameter.Type, targetItem.DeclaringType!, targetItem);
        if (whenDecl != null)
        {
            // null 安全链值：canBlock = 末中间段判空三元；否则从倒数第二段向根嵌套 Condition
            //（与 Convert null 链同构；索引段值类型跳过判空—— 同口径）
            Expression whenValue;
            if (canBlock)
            {
                var leafAccess = AppendSegment(tailVar!, path[depth], indexes.Length > depth ? indexes[depth] : null);
                whenValue = Condition(NotEqual(tailVar!, Constant(null, tailVar!.Type)), leafAccess, Default(leafAccess.Type));
            }
            else
            {
                whenValue = current;
                for (var i = path.Length - 2; i >= 0; i--)
                {
                    var segAccess = ChainAccess(sourceParameter, path, indexes, i);
                    if (indexes.Length > i && indexes[i] is not null && segAccess.Type.IsValueType) continue;
                    whenValue = Condition(NotEqual(segAccess, Constant(null, segAccess.Type)), whenValue, Default(whenValue.Type));
                }
            }
            whenGuard = BuildWhenGuardCondition(sourceParameter, whenValue, sourceParameter.Type, targetItem, whenDecl);
        }
        // 守卫包裹出口（无守卫零包裹——行为不变门）
        Expression WrapWithWhen(Expression assign) => whenGuard != null ? IfThen(whenGuard, assign) : assign;

        // T5 锚门化：目标（承载侧）无锚时 [Convert] 不消费；
        // leaf（实体叶属性）侧查询属 S 侧锚定形态，随 A-14 退役一并删除
        // 收紧：读特性准入改**特性锚**——Fluent 对不消费路径写回 [Convert]
        var converterAttr = RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
            ? targetItem.GetCustomAttribute<ConvertAttribute>()
            : null;
        if (converterAttr != null)
        {
            // null 安全链（叶原始类型 default；中间节点 null → 转换器收到 default(TLeaf)）
            if (canBlock)
                current = Condition(NotEqual(tailVar!, Constant(null, tailVar!.Type)), current, Default(current.Type));
            else
                for (var i = path.Length - 2; i >= 0; i--)
                {
                    var segAccess = ChainAccess(sourceParameter, path, indexes, i);
                    // 索引段值类型元素跳过判空——ChainAccess 已
                    // 内联集合 null 守卫（ElementAtOrDefault → default(值类型)，无 null 语义），
                    // 且 Constant(null, 值类型) 会抛 ArgumentException（构建期裸抛）。
                    if (indexes.Length > i && indexes[i] is not null && segAccess.Type.IsValueType) continue;
                    var nullCheck = NotEqual(segAccess, Constant(null, segAccess.Type));
                    current = Condition(nullCheck, current, Default(current.Type));
                }

            var convertValue = BuildConverterValue(leaf, targetItem, converterAttr, current, sourceParameter);
            if (convertValue == null)
                throw new MapException(ErrorMessages.ConverterExpressionNull(targetItem.Name));

            // 双标检测：[Mask] 共存时组合为 dst.Prop = Mask(Convert(src))，Mask 输入 = 转换结果
            // T5 锚门化：所在类型无锚时不消费
            // 收紧：读特性准入改**特性锚**（Fluent 对不消费 [Mask] 组合腿）
            var maskAttrForConvert = RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
                ? targetItem.GetCustomAttribute<MaskAttribute>()
                : null;
            // 注册表腿补位：特性腿未命中时查注册表明细（ctx 携带对级类型供解析域）
            var registryMaskForConvert = maskAttrForConvert == null && registryCtx != null && registryCtx.HasRegistryMask(targetItem.Name)
                ? registryCtx.MaskTypeNameOf(targetItem.Name)
                : null;
            if (registryMaskForConvert != null && targetItem.PropertyType == typeof(string))
            {
                var registryMaskType = MaskTypeNameResolver.Resolve(registryMaskForConvert, registryCtx!.SourceType, registryCtx!.TargetType, targetItem);
                var registryMaskCall = BuildMaskValue(registryMaskType, convertValue, targetItem);
                return WrapWithWhen(Finalize(Assign(Property(targetParameter, targetItem), registryMaskCall)));
            }
            if (maskAttrForConvert != null && targetItem.PropertyType == typeof(string))
            {
                var maskCall = BuildMaskValue(maskAttrForConvert, convertValue, targetItem);
                return WrapWithWhen(Finalize(Assign(Property(targetParameter, targetItem), maskCall)));
            }
            // 双标非 string 目标 Mask 静默丢弃
            // 补对齐 Trace（与单标分支同口径，修复口径分叉）
            if (maskAttrForConvert != null && MapTracer.GloballyEnabled)
                MapTracer.Write(
                    $"Mask 特性不支持非 string 类型（双标 [Convert]+[Mask] 路径），'{targetItem.DeclaringType?.Name}.{targetItem.Name}' 类型为 {targetItem.PropertyType.Name}，Mask 已跳过仅保留转换");
            // 注册表腿非 string 同口径 Trace（守卫扩腿：特性或注册表命中均不静默）
            if (maskAttrForConvert == null && registryMaskForConvert != null && MapTracer.GloballyEnabled)
                MapTracer.Write(
                    $"Mask 注册表明细不支持非 string 类型（双标 [Convert]+注册表 Mask 路径），'{targetItem.DeclaringType?.Name}.{targetItem.Name}' 类型为 {targetItem.PropertyType.Name}，Mask 已跳过仅保留转换");
            return WrapWithWhen(Finalize(Assign(Property(targetParameter, targetItem), convertValue)));
        }

        // 类型兼容性检查 + 转换（同 BuildCrossTableAssignment 范式）
        // 注意:ChainAccess 已含叶节点访问,此处不再包 Property(leaf)
        if (current.Type != targetType)
        {
            if (TypeConverter.IsImplicitConvertible(current.Type, targetType))
                current = Convert(current, targetType);
            else if (TypeConverter.IsNullableType(targetType)
                     && Nullable.GetUnderlyingType(targetType) == current.Type)
                current = Convert(current, targetType); // T → T? 始终合法
            else
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过属性 {targetItem.Name}（原因：扁平化叶类型 {current.Type.Name} → {targetType.Name} 不兼容）");
                return null;
            }
        }

        // null 安全链：从倒数第二段向根包裹 Condition(segment != null ? next : default)
        // canBlock 时末中间段判空包裹（t 链 null 传播已在
        // Block 赋值内完成）；否则原 Condition 嵌套（各级 test 由 ChainAccess 从根重建前缀链）
        if (canBlock)
            current = Condition(NotEqual(tailVar!, Constant(null, tailVar!.Type)), current, Default(targetType));
        else
            for (var i = path.Length - 2; i >= 0; i--)
            {
                var segAccess = ChainAccess(sourceParameter, path, indexes, i);
                // 索引段值类型元素跳过判空——ChainAccess 已
                // 内联集合 null 守卫（ElementAtOrDefault → default(值类型)，无 null 语义），
                // 且 Constant(null, 值类型) 会抛 ArgumentException（构建期裸抛）。
                if (indexes.Length > i && indexes[i] is not null && segAccess.Type.IsValueType) continue;
                var nullCheck = NotEqual(segAccess, Constant(null, segAccess.Type));
                current = Condition(nullCheck, current, Default(targetType));
            }

        // （H6 消费）：路径 × [Mask] 消费——与直接属性分支同形态
        // （null 安全链之后消费：current 已为目标类型，Mask 输入 = 路径最终值）
        // T5 锚门化：所在类型无锚时不消费
        // 收紧：读特性准入改**特性锚**——Fluent 对不消费路径写回 [Mask]
        var maskAttr = RuntimeAnchorGate.HasAttributeAnchor(targetItem.DeclaringType!)
            ? targetItem.GetCustomAttribute<MaskAttribute>()
            : null;
        // 注册表腿补位：特性腿未命中时查注册表明细（ctx 携带对级类型供解析域）
        var registryMaskName = maskAttr == null && registryCtx != null && registryCtx.HasRegistryMask(targetItem.Name)
            ? registryCtx.MaskTypeNameOf(targetItem.Name)
            : null;
        if (registryMaskName != null && targetItem.PropertyType == typeof(string))
        {
            var registryMaskType = MaskTypeNameResolver.Resolve(registryMaskName, registryCtx!.SourceType, registryCtx!.TargetType, targetItem);
            return WrapWithWhen(Finalize(Assign(Property(targetParameter, targetItem), BuildMaskValue(registryMaskType, current, targetItem))));
        }
        if (maskAttr != null)
        {
            if (targetItem.PropertyType == typeof(string))
                return WrapWithWhen(Finalize(Assign(Property(targetParameter, targetItem), BuildMaskValue(maskAttr, current, targetItem))));
            // 非 string 属性标注 [Mask] — 与直接属性分支同口径 Trace 警告，按常规映射赋值
            if (MapTracer.GloballyEnabled) // Trace 关闭态免插值分配
                MapTracer.Write(
                $"Mask 特性不支持非 string 类型，'{targetItem.DeclaringType?.Name}.{targetItem.Name}' 类型为 {targetItem.PropertyType.Name}，将按常规映射赋值");
        }
        else if (registryMaskName != null && targetItem.PropertyType != typeof(string) && MapTracer.GloballyEnabled)
        {
            // 注册表腿非 string 同口径 Trace（守卫扩腿：特性或注册表命中均不静默）
            MapTracer.Write(
            $"Mask 注册表明细不支持非 string 类型，'{targetItem.DeclaringType?.Name}.{targetItem.Name}' 类型为 {targetItem.PropertyType.Name}，将按常规映射赋值");
        }

        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 扁平化命中 {targetItem.Name} → {string.Join(".", path.Select(p => p.Name))}");
        return WrapWithWhen(Finalize(Assign(Property(targetParameter, targetItem), current)));
    }

    /// <summary>
    /// 反向路径写回集合 — 源（DTO）驱动遍历带路径标注属性，生成折叠写回表达式
    /// <para>触发：源属性带 <c>[MapProperty(FromSource=实体, FromField 含 '.')]</c>；
    /// 守卫：仅规范位置反向（sourceType 标 <see cref="MapFromAttribute"/> 且 FromSource == targetType），
    /// 非规范位置/无标注类型对维持现状（DTO 属性路径标注不产生写回）；
    /// 优先级：实体同名直接属性匹配 &gt; 路径写回（与正向“同名优先于扁平化”对称）；
    /// [MapIgnore] 方向显式接线：DTO 属性（revSource）Direction=ToEntity 生效；
    /// 集合索引路径（FromField 含 '['）反向登记不适用 → Trace 跳过；IsFromJson 正交。</para>
    /// </summary>
    [RequiresUnreferencedCode("BuildReversePathWriteBacks scans type members and attributes via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildReversePathWriteBacks builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
    private static void BuildReversePathWriteBacks(ParameterExpression sourceParameter, ParameterExpression targetParameter,
        Type sourceType, Type targetType, List<PropertyInfo> sourceTypes, List<Expression> expressions,
        HashSet<PropertyInfo>? coveredSegments, bool noAutoMatch = false)
    {
        // 守卫：sourceType 声明映射自 targetType（规范位置 DTO→Entity 反向）
        var sourceAttr = PropertyMatcher.GetMapFromAttribute(sourceType);
        if (sourceAttr == null || sourceAttr.FromSource != targetType) return;

        foreach (var sourceProp in sourceTypes)
        {
            var attr = PropertyIgnoreChecker.GetMapPropertyAttribute(sourceProp); // 缓存族收敛
            var isExplicitPath = attr != null && attr.FromSource == targetType && !attr.IsFromJson
                                 && !string.IsNullOrEmpty(attr.FromField) && attr.FromField.Contains('.');

            if (isExplicitPath)
            {
                // 集合索引路径反向登记不适用（S12h，与源生成器 NCMAP013 登记同语义）
                if (attr!.FromField.Contains('['))
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name}（原因：集合索引路径反向登记不适用）");
                    continue;
                }

                // [MapIgnore] 方向接线：DTO 属性（revSource）Direction=ToEntity → 跳过
                if (PropertyMatcher.ShouldIgnore(sourceProp, isToDto: false, globalIgnoreExempt: false))
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name}（原因：源或目标属性标注了 [MapIgnore] 或命中全局忽略清单）");
                    continue;
                }

                // 显式路径标注 → 路径优先写回（与正向/源生成器对称：显式 FromField 声明即契约，无同名拦截）
                var expr = BuildReversePathWriteBack(sourceParameter, targetParameter, sourceType, targetType, sourceProp, attr, attr.FromField, coveredSegments);
                if (expr != null)
                    expressions.Add(expr);
                continue;
            }

            // 推导扁平化折叠（无标注 DTO 属性，命名约定 AddressCity → Address.City；与显式同进退，P1 同契约）
            // 同名直接属性优先（与正向扁平化 L1124 同规则）：实体存在同名可写属性 → 主循环普通赋值已消费
            if (attr == null)
            {
                // （E5 独立门位；门位二选一定案 = (b) 仅本支路短路，
                // 登记）：白名单模式下**推导扁平化不写回**（未声明属性不得自动折叠回实体
                // 嵌套路径）；E6 显式点路径写回（上方 isExplicitPath 支路）照常——白名单内显式优先。
                // 主循环门（BuildMapBody 前）覆盖不到本路径：本方法执行于主循环之后（回验 F-2），
                // 故须独立门位。
                if (noAutoMatch)
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name}（原因：[MapNoAutoMatch] 白名单模式下未声明扁平化不写回）");
                    continue;
                }

                var flatExpr = BuildReverseFlattenWriteBack(sourceParameter, targetParameter, sourceType, targetType, sourceProp, coveredSegments);
                if (flatExpr != null)
                    expressions.Add(flatExpr);
            }
        }
    }

    /// <summary>
    /// 构建推导扁平化反向写回 — 无标注 DTO 属性按命名约定折叠到实体嵌套路径
    /// <para>候选生成复用正向 <see cref="PathCandidateGenerator.Build"/>（PascalCase 拆解，
    /// 同进退：AddressCity → Address.City）；同名直接属性优先（实体存在同名可写属性 → 跳过）；
    /// 失败为尽力而为语义（无候选/不可写/不可实例化 → Trace + 缓存 null，与正向扁平化失败缓存同语义）；
    /// 段类型非法不抛（推导 = 尽力而为，与正向扁平化一致——抛异常仅限显式路径 C6）。</para>
    /// </summary>
    [RequiresUnreferencedCode("BuildReverseFlattenWriteBack scans type members via reflection. Some members may be trimmed.")]
    [RequiresDynamicCode("BuildReverseFlattenWriteBack builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
    private static Expression? BuildReverseFlattenWriteBack(ParameterExpression sourceParameter, ParameterExpression targetParameter,
        Type sourceType, Type targetType, PropertyInfo sourceProp, HashSet<PropertyInfo>? coveredSegments)
    {
        // 同名直接属性优先：实体存在同名可写属性 → 主循环普通赋值已消费，不折叠（与正向扁平化同规则）
        if (TypeHelper.GetPublicProperties(targetType)
            .Any(p => p.CanWrite && string.Equals(p.Name, sourceProp.Name, StringComparison.OrdinalIgnoreCase)))
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name}（原因：实体存在同名直接属性，同名匹配优先）");
            return null;
        }

        // 缓存查询（含失败缓存；动态类型直算，与正向扁平化同判据）
        var useCache = !PropertyMatcher.IsDynamicType(targetType)
                       && !PropertyMatcher.IsDynamicType(sourceProp.DeclaringType!);
        if (useCache && s_reverseWritePathCache.TryGetValue((targetType, sourceProp), out var cachedPath))
        {
            if (cachedPath == null) return null; // 失败缓存（推导折叠；显式路径失败不进缓存）
            // 覆盖排除段收集——解析成功路径全段入集
            coveredSegments?.UnionWith(cachedPath);
            return BuildReversePathWriteBackFromPath(sourceParameter, targetParameter, sourceProp, cachedPath,
                string.Join(".", cachedPath.Select(p => p.Name)));
        }

        // 候选解析：复用正向扁平化候选生成（段数升序，最短路径优先）
        PropertyInfo[]? path = null;
        foreach (var candidate in PathCandidateGenerator.Build(sourceProp.Name))
        {
            if (candidate.Count <= 1) continue;

            // 段循环收敛为 TypePathResolver（反向尽力式
            // RequireWritable + RequireInstantiable 判据变体，判据与失败分类单点承载）
            var result = s_typeResolver.Resolve(targetType, candidate, PathResolutionOptions.ReverseEager, context: null);
            if (result.Success && result.Path.Count > 1) { path = result.Path.ToArray(); break; }
        }

        if (useCache)
            s_reverseWritePathCache.TryAdd((targetType, sourceProp), path);

        if (path == null)
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name}（原因：命名约定折叠无匹配路径）");
            return null;
        }

        // 覆盖排除段收集——解析成功路径全段入集
        coveredSegments?.UnionWith(path);

        return BuildReversePathWriteBackFromPath(sourceParameter, targetParameter, sourceProp, path,
            string.Join(".", path.Select(p => p.Name)));
    }

    /// <summary>
    /// 构建单条反向路径写回表达式 — DTO 扁平属性 → 实体嵌套路径赋值
    /// <para>语义：源值 null → 整体跳过（不创建中间节点、不写叶值，O-1）；
    /// 中间节点 null → new 创建（仅创建 null 段，已有实例复用，不覆盖导航对象）；
    /// [Convert] 例外：转换器输入 = DTO 值（允许 null），输出 null → 不创建不写、输出非 null → 创建（如需）并写叶；
    /// 失败语义：段缺失/值类型或 string 中间段/非可写段 → 抛 <see cref="MapConfigurationException"/>（C6，不进失败缓存，与正向一致）；
    /// 中间节点不可实例化（接口/抽象/无公共无参构造器）→ 跳过写回 + MapTracer 警告（N-2 方案 1 降级，与源生成器 Warning+跳过同形态）。</para>
    /// </summary>
    /// <returns>写回表达式；跳过（不可实例化/忽略）返回 null（Trace 已输出）</returns>
    [RequiresUnreferencedCode("BuildReversePathWriteBack scans type members and attributes via reflection (path segments, Mask/Convert). Some members may be trimmed.")]
    [RequiresDynamicCode("BuildReversePathWriteBack builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
    private static Expression? BuildReversePathWriteBack(ParameterExpression sourceParameter, ParameterExpression targetParameter,
        Type sourceType, Type targetType, PropertyInfo sourceProp, MapPropertyAttribute attr, string pathDisplay,
        HashSet<PropertyInfo>? coveredSegments)
    {
        // 反向写路径解析缓存：键 (targetType, sourceProp)，动态类型不缓存；
        // null 值 = 跳过缓存（中间节点不可实例化软失败）；抛异常的解析失败不进缓存（显式 = 契约，G4）
        var useCache = !PropertyMatcher.IsDynamicType(targetType)
                       && !PropertyMatcher.IsDynamicType(sourceProp.DeclaringType!);
        if (useCache && s_reverseWritePathCache.TryGetValue((targetType, sourceProp), out var cachedPath))
        {
            if (cachedPath == null)
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name} → {pathDisplay}（原因：中间节点不可实例化，缓存命中）");
                return null;
            }
            // 覆盖排除段收集——解析成功路径全段入集
            coveredSegments?.UnionWith(cachedPath);
            return BuildReversePathWriteBackFromPath(sourceParameter, targetParameter, sourceProp, cachedPath, pathDisplay);
        }

        // 段循环收敛为 TypePathResolver（反向契约式）
        // 失败分类映射（⑤ 行）：SegmentNotFound/NotNavigable → 抛（硬失败，对齐现）；
        // SegmentNotWritable/NotInstantiable → 跳过+Trace+失败缓存（软失败，对齐现）
        var segments = attr.FromField.Split('.');
        var result = s_typeResolver.Resolve(targetType, segments, PathResolutionOptions.ReverseContract, context: null);
        if (!result.Success)
        {
            // 失败段所在类型名：前缀路径末段属性类型（FailedSegmentIndex==0 时为根类型）
            var failedTypeName = result.FailedSegmentIndex == 0
                ? targetType.Name
                : result.Path[result.FailedSegmentIndex - 1].PropertyType.Name;
            var failedSegment = result.FailedSegment ?? segments[result.FailedSegmentIndex];

            if (result.Failure == PathSegmentFailure.SegmentNotFound || result.Failure == PathSegmentFailure.NotNavigable)
            {
                // 硬失败：段缺失/不可导航 → 抛（C6，不进失败缓存，与正向一致）
                var kind = result.Failure == PathSegmentFailure.SegmentNotFound
                    ? DotPathFailureKind.ReverseWriteBackSegmentPropertyNotFound
                    : DotPathFailureKind.ReverseWriteBackSegmentNotNavigable;
                throw new MapConfigurationException(
                    ErrorMessages.ReverseWriteBackPathFailed(pathDisplay, failedSegment, kind, sourceProp.Name, failedTypeName));
            }

            // 软失败：setter 不可用（SegmentNotWritable）/不可实例化（NotInstantiable）→ 跳过 + Trace + 失败缓存
            // （与源生成器 NCMAP013 跳过同形态，跨引擎失败形态对称，N-2）
            var isLeaf = result.FailedSegmentIndex == segments.Length - 1;
            if (result.Failure == PathSegmentFailure.NotInstantiable)
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name} → {pathDisplay}（原因：中间节点 {failedSegment}（{failedTypeName}）不可实例化（接口/抽象类/无公共无参构造器））");
            }
            else
            {
                var nodeKind = isLeaf ? "叶节点" : "中间节点";
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name} → {pathDisplay}（原因：{nodeKind} {failedSegment} setter 不可用（init-only/private），无法赋值）");
            }
            if (useCache) s_reverseWritePathCache.TryAdd((targetType, sourceProp), null);
            return null;
        }

        if (useCache)
            s_reverseWritePathCache.TryAdd((targetType, sourceProp), result.Path.ToArray());

        // 覆盖排除段收集——解析成功路径全段入集
        var resolvedPath = result.Path.ToArray();
        coveredSegments?.UnionWith(resolvedPath);

        return BuildReversePathWriteBackFromPath(sourceParameter, targetParameter, sourceProp, resolvedPath, pathDisplay);
    }

    /// <summary>
    /// 由已解析写路径构造反向写回表达式（缓存命中路径复用）
    /// <para>形态：源值 null 跳过守卫包裹 [中间节点 null → new 创建逐段守卫 + 叶赋值]；
    /// [ConvertBack]：转换器调用（输入 = DTO 值，仅在源非 null 分支求值）
    /// 替代类型转换；源 null → 清叶（不创建中间节点、不调转换器，N-1）。
    /// 叶节点全局忽略判定（与正向 BuildFlattenedAssignmentFromPath 对称：叶命中忽略 → 跳过 + Trace）。</para>
    /// </summary>
    [RequiresUnreferencedCode("BuildReversePathWriteBackFromPath scans type members and attributes via reflection (leaf ignore, Convert). Some members may be trimmed.")]
    [RequiresDynamicCode("BuildReversePathWriteBackFromPath builds expressions via MakeGenericMethod/MakeGenericType. Not supported in AOT.")]
    private static Expression? BuildReversePathWriteBackFromPath(ParameterExpression sourceParameter, ParameterExpression targetParameter,
        PropertyInfo sourceProp, PropertyInfo[] path, string pathDisplay)
    {
        var leaf = path[path.Length - 1];
        var leafType = leaf.PropertyType;
        var sourceValue = Property(sourceParameter, sourceProp);

        // 叶节点全局忽略判定（与正向对称：isToDto=false 方向 + 源侧豁免传递）
        var globalIgnoreExempt = PropertyIgnoreChecker.HasOverrideIgnore(sourceProp);
        if (PropertyMatcher.ShouldIgnore(sourceProp, isToDto: false, globalIgnoreExempt)
            || PropertyMatcher.ShouldIgnore(leaf, isToDto: false, globalIgnoreExempt))
        {
            if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name}（原因：源或目标属性标注了 [MapIgnore] 或命中全局忽略清单）");
            return null;
        }

        // [ConvertBack]（DTO 属性规范位置优先，实体叶属性非规范位置兜底）
        // → 调用静态反向转换方法（T0-T3 模板解析）替代类型转换；
        // 转换器调用表达式仅在源非 null 分支求值（null=清空形态包裹），源 null 走清叶不调转换器。
        // 无 [ConvertBack] → 一律不消费 [Convert]（生成端 / 同语义），
        // 统一走类型转换（同 BuildFlattenedAssignmentFromPath 范式）。
        Expression? valueExpr;
        // leaf（实体叶属性）非规范位置兜底删——[ConvertBack] 单查
        // 承载侧（sourceProp=DTO 属性）；实体侧形态属 S 侧锚定（退役， ④）
        // （判类订正见 附 B-①）：本点位原为**无门直读**
        // 加**特性锚门**：Fluent 对（仅注册表命中）不消费属性级 [ConvertBack]，反向路径写回按
        // 明细执行（预期差异面登记： 行 7 +）
        var backAttr = RuntimeAnchorGate.HasAttributeAnchor(sourceProp.DeclaringType!)
            ? sourceProp.GetCustomAttribute<ConvertBackAttribute>()
            : null;
        if (backAttr != null)
        {
            valueExpr = BuildBackConverterValue(sourceProp, leaf, backAttr, sourceValue, sourceParameter);
            if (valueExpr == null)
            {
                if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name}（原因：[ConvertBack] 转换器表达式构建失败）");
                return null;
            }
        }
        else
        {
            if (sourceValue.Type != leafType)
            {
                if (TypeConverter.IsImplicitConvertible(sourceValue.Type, leafType))
                    valueExpr = Convert(sourceValue, leafType);
                else if (TypeConverter.IsNullableType(leafType)
                         && Nullable.GetUnderlyingType(leafType) == sourceValue.Type)
                    valueExpr = Convert(sourceValue, leafType); // T → T? 始终合法
                else if (TypeConverter.IsNullableType(sourceValue.Type)
                         && Nullable.GetUnderlyingType(sourceValue.Type) == leafType)
                {
                    // T? → T 写 default(T) 形态，
                    // 与直接属性路径 BuildNullableConditionalAssignment 的 ?? default 语义对称；
                    // 对应生成端 BuildLeafValueExpr 的 IsNullableToNonNullable 分支
                    valueExpr = Condition(
                        Property(sourceValue, "HasValue"),
                        Convert(sourceValue, leafType),
                        Default(leafType));
                }
                else
                {
                    if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 跳过反向写回 {sourceProp.Name}（原因：叶类型 {sourceValue.Type.Name} → {leafType.Name} 不兼容）");
                    return null;
                }
            }
            else
            {
                valueExpr = sourceValue;
            }
        }

        // 中间节点 null → new 创建逐段守卫（仅创建 null 段；已有实例复用不覆盖）
        // 同循环构建清空分支链存在判定（中间段全非 null）
        var chainExprs = new List<Expression>(path.Length);
        Expression? chainExists = null;
        Expression chain = targetParameter;
        for (var i = 0; i < path.Length - 1; i++)
        {
            var seg = path[i];
            var segProp = Property(chain, seg);
            chainExprs.Add(IfThen(
                Equal(segProp, Constant(null, segProp.Type)),
                Assign(segProp, New(seg.PropertyType))));
            var segNotNull = NotEqual(segProp, Constant(null, segProp.Type));
            chainExists = chainExists == null ? segNotNull : AndAlso(chainExists, segNotNull);
            chain = segProp;
        }
        var leafAccess = Property(chain, leaf);

        chainExprs.Add(Assign(leafAccess, valueExpr));
        var writeBody = Expression.Block(chainExprs);
        // 源可空 → null=清空。
        // 源 null：链存在则清叶（可空叶 null / 非可空值叶 default(T)），清空路径不创建中间节点；
        // 源非 null：写链（中间段 null → new 创建）。非可空值类型源：直写（现状不变）。
        Expression body;
        var sourceNullable = !sourceValue.Type.IsValueType || Nullable.GetUnderlyingType(sourceValue.Type) != null;
        if (sourceNullable)
        {
            Expression clearValue = leafType.IsValueType && Nullable.GetUnderlyingType(leafType) == null
                ? Default(leafType)
                : Constant(null, leafType);
            Expression clearBody;
            if (chainExists != null)
                clearBody = IfThen(chainExists, Assign(leafAccess, clearValue));
            else
                clearBody = Assign(leafAccess, clearValue);
            body = IfThenElse(
                NotEqual(sourceValue, Constant(null, sourceValue.Type)),
                writeBody,
                clearBody);
        }
        else
        {
            body = writeBody;
        }

        if (MapTracer.GloballyEnabled) MapTracer.Write($"[NCMapper] ExpressionBuilder: 反向写回命中 {sourceProp.Name} → {pathDisplay}");
        return body;
    }

    /// <summary>
    /// 构建源参数到路径第 depth 段（含）的成员访问链
    /// <para>：索引段（indexes[i] 非 null）生成 ElementAtOrDefault 调用并内联 null 守卫
    /// （集合 null → default(元素类型)；空集合/越界 → ElementAtOrDefault 返回 default）——
    /// 外层 null 安全链的 Condition(seg != null) 判空位于索引访问之后，若不在索引段内联判空，
    /// 集合为 null 时 ElementAtOrDefault(null, n) 会抛 ArgumentNullException。</para>
    /// </summary>
    [RequiresDynamicCode("ChainAccess builds ElementAtOrDefault via MakeGenericMethod. Not supported in AOT.")]
    [RequiresUnreferencedCode("ChainAccess scans type members via reflection. Some members may be trimmed.")]
    private static Expression ChainAccess(ParameterExpression root, IReadOnlyList<PropertyInfo> path,
        IReadOnlyList<int?> indexes, int depth)
    {
        // 循环体抽取 AppendSegment（供 null 安全链 Block 优化续接复用）
        Expression current = root;
        for (var i = 0; i <= depth; i++)
            current = AppendSegment(current, path[i], indexes.Count > i ? indexes[i] : null);
        return current;
    }

    /// <summary>
    /// 从基表达式续接单段访问 — S-21抽取 ChainAccess 循环体。
    /// <para>索引段生成 ElementAtOrDefault 调用并内联 null 守卫（集合 null → default(元素类型)；
    /// 空集合/越界 → ElementAtOrDefault 返回 default）——与 ChainAccess 原语义逐字一致。</para>
    /// </summary>
    [RequiresDynamicCode("AppendSegment builds ElementAtOrDefault via MakeGenericMethod. Not supported in AOT.")]
    [RequiresUnreferencedCode("AppendSegment scans type members via reflection. Some members may be trimmed.")]
    private static Expression AppendSegment(Expression current, PropertyInfo seg, int? index)
    {
        var propAccess = Property(current, seg);
        if (index is int n)
        {
            var elemType = NavigationResolver.GetCollectionElementType(propAccess.Type);
            if (elemType == null)
            {
                // 解析期已校验集合类型；防御性兜底（不可达）
                return Default(propAccess.Type);
            }
            var elementAtOrDefault = s_elementAtOrDefaultDef.MakeGenericMethod(elemType); // 静态缓存
            return Condition(
                NotEqual(propAccess, Constant(null, propAccess.Type)),
                Call(elementAtOrDefault, propAccess, Constant(n)),
                Default(elemType));
        }
        return propAccess;
    }
}
