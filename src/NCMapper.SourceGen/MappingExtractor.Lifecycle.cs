using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NCMapper.SourceGen;

internal static partial class MappingExtractor
{
        /// <summary>
        /// 特性侧 [MapWhen(methodName)] 消费
        /// 在被标注属性声明类型上按名解析静态谓词方法（单类型自足，DeclaredOnly 含 partial 全部部分）：
        /// <list type="number">
        /// <item>方法不存在（DeclaredOnly 无同名）/多重载同名 → NCMAP041（不可解析/歧义）</item>
        /// <item>非静态 / lambda/局部函数 → NCMAP041</item>
        /// <item>private/protected/protected internal 跨类不可访问 → NCMAP041（可见性不足）</item>
        /// <item>签名门：单参 + 返回 bool + 无 ref/out/in → 违者 NCMAP041（签名不符）</item>
        /// <item>入参形态判定：单参 == 源实体类型（info.SourceType）判 T2 整源；否则 T1 字段值
        /// （参数类型按惯例 = 源字段类型——T1 侧源→参数须隐式转换，违者 NCMAP041）</item>
        /// <item>产物：pm 条件四字段注入（A 形态——ConditionMethodRef="声明类型::方法名"）</item>
        /// </list>
        /// <para>解析内核提取为 <see cref="TryResolveWhenMethod"/>（跨表属性消费共享
        /// 镜像 Fluent 块内：跨表发射落点 = Update_{Sub}_To_{Target} 块内赋值层，有真实落点，
        /// 消费而非 F45；静默丢失过滤语义 = 正确性危害）。</para>
        /// </summary>
        /// <param name="info">映射信息（诊断收集）</param>
        /// <param name="prop">被标注的 DTO 属性（锚点）</param>
        /// <param name="pm">该属性的映射对（条件四字段注入目标）</param>
        /// <param name="sourceProp">该属性映射的源属性（T1 转换校验基准）</param>
        /// <param name="methodName">[MapWhen] 单参方法名</param>
        private static void ApplyMapWhenAttribute(
            MappingInfo info, IPropertySymbol prop, PropertyMapping pm, IPropertySymbol sourceProp, string methodName)
        {
            if (!TryResolveWhenMethod(info, prop, methodName, info.SourceType, sourceProp.Type,
                    out var methodRef, out var shape))
                return;

            // 产物：pm 条件四字段注入（A 形态——ConditionMethodRef="声明类型::方法名"；
            // B 形态为 Fluent 独有——特性实参为编译期常量，表达力不对称 D-n）
            pm.ConditionShape = shape;
            pm.ConditionMethodRef = methodRef;
        }

        /// <summary>
        /// [MapWhen] 谓词方法解析内核（提取——主链 pm / 跨表 cm 两消费点共享）：
        /// 声明类型按名解析（DeclaredOnly 含 partial）→ 候选门（存在/唯一）→ 静态门 →
        /// 可见性门 → 签名门 → T1/T2 入参形态判定；诊断口径与 主链版一致（NCMAP041）。
        /// </summary>
        /// <param name="info">映射信息（诊断收集）</param>
        /// <param name="prop">被标注属性（锚点——声明类型 = 谓词解析宿主）</param>
        /// <param name="methodName">[MapWhen] 单参方法名</param>
        /// <param name="wholeSourceBase">T2 整源形态判定基准（主链 = 源实体类型；跨表 = 子源类型）</param>
        /// <param name="fieldValueBase">T1 字段值形态判定基准（主链 = 源属性类型；跨表 = 末段属性类型）</param>
        /// <param name="methodRef">输出：谓词方法引用（"声明类型::方法名"）</param>
        /// <param name="shape">输出：谓词入参形态</param>
        private static bool TryResolveWhenMethod(
            MappingInfo info, IPropertySymbol prop, string methodName,
            INamedTypeSymbol wholeSourceBase, ITypeSymbol fieldValueBase,
            out string methodRef, out ConditionShape shape)
        {
            methodRef = "";
            shape = ConditionShape.None;

            // 声明类型上按名解析（DeclaredOnly：含 partial 各部分、不含基类继承——
            // 与 [BeforeMapping] 单参形态同口径）
            var declaringType = prop.ContainingType;
            var candidates = declaringType.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .ToList();

            if (candidates.Count == 0)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapWhenAttributeResolutionRule,
                    Args = new object[] { prop.Name, declaringType.ToDisplayString(), methodName, "方法不存在（须声明于被标注属性所在类型，含 partial 各部分）" },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            // 多重载歧义（同名多方法形态——用户消歧手段：重命名方法）
            if (candidates.Count > 1)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapWhenAttributeResolutionRule,
                    Args = new object[] { prop.Name, declaringType.ToDisplayString(), methodName, "同名方法存在多个重载，无法确定谓词（须唯一）" },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            var ms = candidates[0];

            // 静态门（lambda/局部函数形态不可达——特性实参为字符串，恒普通方法）
            if (!ms.IsStatic)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapWhenAttributeResolutionRule,
                    Args = new object[] { prop.Name, declaringType.ToDisplayString(), methodName, "谓词方法必须为 static" },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            // 可见性门：生成代码位于 internal static class GeneratedMapper（同程序集）——
            // private/protected/protected internal 跨类不可访问
            if (ms.DeclaredAccessibility is Accessibility.Private
                or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapWhenAttributeResolutionRule,
                    Args = new object[] { prop.Name, declaringType.ToDisplayString(), methodName, "谓词方法可见性不足（须 public/internal，private/protected 跨类不可访问）" },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            // 签名门：单参 + 返回 bool + 无 ref/out/in
            if (ms.Parameters.Length != 1
                || ms.ReturnType.SpecialType != SpecialType.System_Boolean
                || ms.Parameters[0].RefKind != RefKind.None)
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapWhenAttributeResolutionRule,
                    Args = new object[] { prop.Name, declaringType.ToDisplayString(), methodName, "谓词签名须为 static bool M(T)（单参、返回 bool、无 ref/out/in）" },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            // 入参形态判定（Convert 族同规则）：单参 == 整源基准判 T2，否则 T1 字段值；
            // T1 侧参数类型须 == 字段值基准（Nullable 解包相等——TypeEqualsUnwrapped，
            // TryMatchConverterTemplate 同口径；无隐式转换放宽——特性侧签名契约为精确匹配）
            var paramType = ms.Parameters[0].Type;
            var isWholeSource = TypeEqualsUnwrapped(paramType, wholeSourceBase);
            if (!isWholeSource && !TypeEqualsUnwrapped(paramType, fieldValueBase))
            {
                info.Diagnostics.Add(new DiagnosticInfo
                {
                    Descriptor = DiagnosticDescriptors.MapWhenAttributeResolutionRule,
                    Args = new object[] { prop.Name, declaringType.ToDisplayString(), methodName,
                        $"谓词入参类型 {paramType.ToDisplayString()} 须为源字段类型 {fieldValueBase.ToDisplayString()}（T1）或源实体类型 {wholeSourceBase.ToDisplayString()}（T2）" },
                    Location = prop.Locations.FirstOrDefault(),
                });
                return false;
            }

            methodRef = declaringType.ToDisplayString() + "::" + methodName;
            shape = isWholeSource ? ConditionShape.WholeSource : ConditionShape.FieldValue;
            return true;
        }

        /// <summary>
        /// 特性通道生命周期钩子提取 — 读目标 DTO 类型级 [BeforeMapping]/[AfterMapping] 特性，
        /// 按声明所在对 (sourceType, targetSymbol) 签名适配后填充 <see cref="MappingInfo"/> 两承载字段
        /// <para>ExtractMappingCore 入口调用（NCMAP011 短路检查之前——non-public 对仍填充）；
        /// 解析/可见性/签名失败发 NCMAP038/039/040（fail-fast，与 Convert 族同口径）。</para>
        /// </summary>
        /// <param name="info">映射信息（承载字段与诊断追加目标）</param>
        /// <param name="sourceType">源类型符号（签名适配基准 1）</param>
        /// <param name="targetSymbol">目标 DTO 类型符号（签名适配基准 2；特性锚定宿主）</param>
        private static void ExtractLifecycleHooks(MappingInfo info, INamedTypeSymbol sourceType, INamedTypeSymbol targetSymbol)
        {
            foreach (var attr in targetSymbol.GetAttributes())
            {
                // 赋值形态同族站点缓存化（编制期 == 口径外，等价替换登记）
                var attrName = AttrSymbolCache.DisplayName(attr.AttributeClass);
                if (attrName == AttributeNames.BeforeMapping)
                {
                    info.BeforeMappingMethodRef = ResolveLifecycleHook(
                        attr, targetSymbol, sourceType, targetSymbol, info.Diagnostics,
                        diagnoseSignatureMismatch: true);
                }
                else if (attrName == AttributeNames.AfterMapping)
                {
                    info.AfterMappingMethodRef = ResolveLifecycleHook(
                        attr, targetSymbol, sourceType, targetSymbol, info.Diagnostics,
                        diagnoseSignatureMismatch: true);
                }
            }
        }

        /// <summary>
        /// 生命周期钩子方法解析（共享——特性通道本对 + Fluent 通道跨对传播两调用点）：
        /// 双 ctor 解包（双参=宿主/单参=DTO 自身）→ 锚定类型静态同名方法枚举 → 可访问性白名单 →
        /// 签名适配（void + 2 参 + 参数类型赋值兼容映射对）。
        /// <para>诊断口径：NCMAP038（找不到静态方法/可见性不足）、NCMAP040（多重载兼容无精确）恒由
        /// diagnostics 非 null 时产出；NCMAP039（签名不匹配）仅 <paramref name="diagnoseSignatureMismatch"/>
        /// = true（声明所在对）时产出——跨对传播签名不适配为 D-d 正常过滤路径（不适用对不注入不发诊断）。</para>
        /// </summary>
        /// <param name="attr">[BeforeMapping]/[AfterMapping] 特性数据</param>
        /// <param name="annotatedType">承载该特性的 DTO 类型符号（单参形态锚定类型）</param>
        /// <param name="sourceType">签名适配基准 1（映射对源类型）</param>
        /// <param name="targetType">签名适配基准 2（映射对目标类型）</param>
        /// <param name="diagnostics">诊断追加目标；null = 全静默（跨对传播且特性通道已产出场景）</param>
        /// <param name="diagnoseSignatureMismatch">签名不匹配是否发 NCMAP039（本对 true / 跨对 false）</param>
        /// <returns>方法引用 "{ContainingType.ToDisplayString()}.{Name}"；解析失败 null（诊断按口径已产出）</returns>
        internal static string? ResolveLifecycleHook(
            AttributeData attr,
            INamedTypeSymbol annotatedType,
            INamedTypeSymbol sourceType,
            INamedTypeSymbol targetType,
            List<DiagnosticInfo>? diagnostics,
            bool diagnoseSignatureMismatch)
        {
            // 双 ctor 解包（形态合法性由编译器保证；Try 形态不匹配直接放弃——静默与 Convert 同口径）
            INamedTypeSymbol? anchorType = null;
            string? methodName = null;
            if (attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol declaringType
                && attr.ConstructorArguments[1].Value is string dualMethodName)
            {
                anchorType = declaringType;
                methodName = dualMethodName;
            }
            else if (attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string singleMethodName)
            {
                anchorType = annotatedType;
                methodName = singleMethodName;
            }
            if (anchorType == null || methodName == null) return null;

            var attributeName = attr.AttributeClass?.Name ?? "BeforeMappingAttribute";
            var anchorName = anchorType.ToDisplayString();
            // 特性自身语法位置（ApplicationSyntaxReference——无语法引用的合成场景为 null，Location.None 语义）
            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();

            // 开放泛型宿主（镜像 Convert NCMAP037 开放泛型判据——typeof(X<>) 传入的未绑定泛型符号）
            if (anchorType.IsGenericType
                && (anchorType.IsUnboundGenericType
                    || anchorType.IsDefinition
                    || anchorType.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter)))
            {
                if (diagnostics != null)
                {
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.LifecycleHookSignatureMismatchRule,
                        Args = new object[] { attributeName, methodName,
                            $"anchor type is an open generic type; use the two-argument form with a closed generic type (e.g. typeof(X<int>))" },
                        Location = location
                    });
                }
                return null;
            }

            // 锚定类型自身声明的同名静态方法（GetMembers 不含基类继承 = DeclaredOnly；partial 天然合并）
            var sameName = new List<IMethodSymbol>();
            foreach (var member in anchorType.GetMembers())
            {
                if (member is IMethodSymbol m && m.IsStatic
                    && string.Equals(m.Name, methodName, StringComparison.Ordinal))
                {
                    sameName.Add(m);
                }
            }

            // NCMAP038 判据①：锚定类型上无该名称静态方法（名称不存在 / 仅有实例方法）——
            // 消息含"考虑双参形态指定宿主"改写指引（nameof 限定名截断场景）
            if (sameName.Count == 0)
            {
                if (diagnostics != null)
                {
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.LifecycleHookMethodUnresolvedRule,
                        Args = new object[] { attributeName, methodName, anchorName },
                        Location = location
                    });
                }
                return null;
            }

            // 可访问性白名单：public / internal（同程序集）/ protected internal（同程序集腿）；
            // protected / private protected / private 拒绝（与 ResolveConverterMethod R3 同口径）
            var accessible = new List<IMethodSymbol>();
            foreach (var m in sameName)
            {
                if (m.DeclaredAccessibility == Accessibility.Public
                    || ((m.DeclaredAccessibility == Accessibility.Internal || m.DeclaredAccessibility == Accessibility.ProtectedOrInternal)
                        && SymbolEqualityComparer.Default.Equals(m.ContainingAssembly, anchorType.ContainingAssembly)))
                {
                    accessible.Add(m);
                }
            }

            // NCMAP038 判据②：存在同名静态方法但可见性不足（与名称不存在同码，消息覆盖两种理由）
            if (accessible.Count == 0)
            {
                if (diagnostics != null)
                {
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.LifecycleHookMethodUnresolvedRule,
                        Args = new object[] { attributeName, methodName, anchorName },
                        Location = location
                    });
                }
                return null;
            }

            // 签名适配：void 返回 + 恰 2 参 + 参数类型赋值兼容映射对（精确匹配优先）
            var exact = new List<IMethodSymbol>();
            var compatible = new List<IMethodSymbol>();
            foreach (var m in accessible)
            {
                if (m.ReturnsVoid && m.Parameters.Length == 2
                    && IsTypeAssignableTo(sourceType, m.Parameters[0].Type)
                    && IsTypeAssignableTo(targetType, m.Parameters[1].Type))
                {
                    compatible.Add(m);
                    if (SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceType)
                        && SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, targetType))
                    {
                        exact.Add(m);
                    }
                }
            }

            if (compatible.Count == 0)
            {
                if (diagnoseSignatureMismatch && diagnostics != null)
                {
                    var signature = string.Join(", ", accessible.Select(m =>
                        $"{m.ReturnType.ToDisplayString()} {m.Name}({string.Join(", ", m.Parameters.Select(p => p.Type.ToDisplayString()))})"));
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.LifecycleHookSignatureMismatchRule,
                        Args = new object[] { attributeName, methodName,
                            $"expected void({sourceType.ToDisplayString()}, {targetType.ToDisplayString()}); accessible candidates: {signature}" },
                        Location = location
                    });
                }
                return null;
            }

            // NCMAP040：多重载兼容且无精确匹配（精确签名唯一命中优先——钩子签名即适用范围声明）
            if (exact.Count == 0 && compatible.Count > 1)
            {
                if (diagnostics != null)
                {
                    diagnostics.Add(new DiagnosticInfo
                    {
                        Descriptor = DiagnosticDescriptors.LifecycleHookAmbiguousRule,
                        Args = new object[] { attributeName, methodName, anchorName },
                        Location = location
                    });
                }
                return null;
            }

            var resolved = exact.Count > 0 ? exact[0] : compatible[0];
            return resolved.ContainingType.ToDisplayString() + "." + resolved.Name;
        }

        /// <summary>
        /// 符号级赋值兼容判定（签名适配 D-d）——from 可赋值给 to：
        /// 精确相等 / object / 基类链 / 接口实现（SymbolEqualityComparer.Default 忽略 nullability）
        /// <para>覆盖钩子方法参数对映射对的常见适配形态（精确 + 宽签名 object/基类/接口）；
        /// 用户自定义隐式转换运算符与数值族隐式转换不覆盖（DTO 级钩子签名场景不适用， 差异登记）。</para>
        /// </summary>
        /// <param name="from">映射对侧类型（实参）</param>
        /// <param name="to">钩子方法参数类型（形参）</param>
        /// <returns>可赋值时为 true</returns>
        private static bool IsTypeAssignableTo(ITypeSymbol from, ITypeSymbol to)
        {
            if (SymbolEqualityComparer.Default.Equals(from, to)) return true;
            if (to.SpecialType == SpecialType.System_Object) return true;
            for (var b = from.BaseType; b != null; b = b.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(b, to)) return true;
            }
            foreach (var i in from.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(i, to)) return true;
            }
            return false;
        }
}
