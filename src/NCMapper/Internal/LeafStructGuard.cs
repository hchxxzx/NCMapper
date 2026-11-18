using System.Collections.Concurrent;
using System.Reflection;
using NCMapper;

namespace NCMapper.Internal;

/// <summary>
/// 运行时叶子判定收紧守卫
/// <para>缺陷：<c>IsSimpleType = IsValueType || string</c> 把含可变引用成员的 struct 当叶子
/// 浅赋值——拷贝体与源共享同一可变引用成员（如 <c>struct S { List&lt;int&gt; Items; }</c> 的
/// Items），深拷贝契约失效。</para>
/// <para>口径（+ 探索①建议）：排除项 = 含<b>可变引用实例字段</b>的 struct；
/// readonly 引用字段不排除（运行时回写通道对 readonly 无效，保共享为既定语义）；
/// string/Delegate 及 X4/Q2 叶白名单类型（Type/MemberInfo/Stream/DBNull）不触发——
/// 与 DeepCopyTypeClassifier.IsLeaf 的叶子语义一致（共享为既定行为）。</para>
/// <para><b>生成期/运行时分叉豁免登记</b>（对照 :336"禁止第二份"）：本守卫仅
/// 运行时侧三消费点（DeepCopyCompiler/CollectionCopier/DeepCopyTypeClassifier）使用；
/// 共享规则 DeepCopyGraphRules 与 SourceGen 物化期第四处（MappingExtractor.Materialize）
/// 不动——行为修复批守住字节门（生成码零变化，G3 维持 IDENTICAL），生成期路径缺陷残留
/// 由 B5-4/收尾批承接。</para>
/// <para>残留面：①嵌套 struct（值类型字段不穿透——含可变引用成员的
/// struct 作为另一 struct 的值字段时，外层仍判叶）②Nullable&lt;S&gt; 包装（其字段为值类型
/// 不触发）③生成期物化链。均属甲案"部分修复"已登记代价。</para>
/// <para><b>登记制 + 动态类型直算（三分法第二行）</b>：
/// s_cache 原未登记 CacheRegistry（ClearCache 链不覆盖——审计唯一漏网），静态构造补登记
/// （<c>leaf-struct-guard</c>，仅登记不触发缓存构建防自触发）；动态类型（collectible ALC）
/// 直算不缓存（<see cref="PropertyMatcher.IsDynamicType"/> 命中即绕过——强键 CD 会钉住
/// 动态类型阻止 ALC 回收；简单值缓存按三分法第二行裁直算），静态类型照常 GetOrAdd。</para>
/// </summary>
internal static class LeafStructGuard
{
    private static readonly ConcurrentDictionary<Type, bool> s_cache = new();

    /// <summary>
    /// CacheRegistry 登记制自注册：仅登记清理委托，不触发缓存构建（防自触发，
    /// 同 PropertyMatcher/NavigationTreeCollector 口径）——Mapper.ClearCache() 全链清理覆盖本缓存。
    /// </summary>
    static LeafStructGuard()
    {
        CacheRegistry.Register("leaf-struct-guard", CacheLayer.Full, ClearCache);
    }

    /// <summary>
    /// struct 类型是否叶子安全（无可变引用实例字段）。枚举/基元恒 true；仅值类型有意义。
    /// <para>动态类型直算分支（IsDynamicType 命中即 Compute 不缓存
    /// 三分法第二行）；Compute 结果与直算逐位一致（仅缓存策略差异）。</para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("IsLeafSafe scans struct fields via reflection. Some members may be trimmed. Use the source-generated path to make this AOT-safe.")]
    internal static bool IsLeafSafe(Type type)
        => PropertyMatcher.IsDynamicType(type)
            ? Compute(type)
            : s_cache.GetOrAdd(type, static t => Compute(t));

    /// <summary>
    /// 清除缓存 —— 供 Mapper.ClearCache() 全链（CacheRegistry 登记 leaf-struct-guard）与测试隔离调用。
    /// 强键 CD Clear()；静态类型清后重算（可重算前提，结果确定）
    /// </summary>
    internal static void ClearCache() => s_cache.Clear();

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Compute scans struct fields via reflection. Some members may be trimmed.")]
    private static bool Compute(Type type)
    {
        // 结构体无继承（隐式 sealed），GetFields(Instance) 仅返回自身声明字段（含自动属性 backing）
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var f in fields)
        {
            var ft = f.FieldType;
            if (ft.IsValueType) continue;          // 值类型字段：不触发（嵌套缺陷面已登记残留）
            if (f.IsInitOnly) continue;            // readonly 引用字段：裁决不排除
            if (IsLeafReference(ft)) continue;      // string/Delegate/叶白名单：共享为既定语义
            return false;                          // 可变引用实例字段 → 非叶
        }
        return true;
    }

    /// <summary>
    /// 引用类型字段是否"共享安全"（不触发非叶排除）——string/Delegate 及 X4/Q2 叶白名单
    ///（Type/MemberInfo/Stream/DBNull，与 DeepCopyTypeClassifier.IsLeaf 口径一致）。
    /// </summary>
    internal static bool IsLeafReference(Type fieldType)
        => fieldType == typeof(string)
           || typeof(Delegate).IsAssignableFrom(fieldType)
           || fieldType == typeof(Type)
           || typeof(System.Reflection.MemberInfo).IsAssignableFrom(fieldType)
           || typeof(System.IO.Stream).IsAssignableFrom(fieldType)
           || fieldType == typeof(DBNull);
}
