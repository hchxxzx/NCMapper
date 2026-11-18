using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using NCMapper;
using NCMapper.Internal;

namespace NCMapper.Runtime;

/// <summary>
/// 注册表脱敏类型名解析器 —— 字符串显示名到 Type 的四段式解析（对级解析域 + 解析域入缓存键 + 负缓存）。
/// <para>解析顺序（写死）：① targetType.Assembly 直解（含 + 嵌套形态）+ 嵌套链逐段（"." 形态）
/// ② sourceType.Assembly 同款 ③ Type.GetType（AQN 兼容）④ 已加载程序集扫描
/// （歧义处置：解析域程序集优先 → 其余按 Assembly.FullName 序取首，GloballyEnabled 时 Trace 记候选数）。
/// 解析域取对级类型而非 targetItem.DeclaringType——继承属性的 DeclaringType 是基类，
/// 其程序集可能与 DTO 侧不同（对级根因同缓存键）。</para>
/// <para>缓存键 = (类型名, 解析域程序集 FullName)——跨程序集同 FullName 时无域前缀的键会缓存串扰
/// （先例：PropertyMatcher 解析域前缀同口径）。负缓存：失败存 null，命中仍抛同款 MapException——
/// 负缓存省的是重复解析动作，不是重复抛错（先例 PropertyMatcher 负缓存命中仍抛同形态）；
/// 禁止"命中 null 即返回 null"——可诊断异常被换成 null 流入渲染层 = 新静默面。</para>
/// </summary>
[RequiresUnreferencedCode("MaskTypeNameResolver resolves type names via reflection over assemblies. Some types may be trimmed.")]
internal static class MaskTypeNameResolver
{
    private static readonly ConcurrentDictionary<(string Name, string DomainKey), Type?> s_cache = new();

    static MaskTypeNameResolver()
    {
        // 登记制自注册：仅登记清理委托，不触发缓存构建（先例 assignment-planner 同形态）
        CacheRegistry.Register("mask-type-name-resolver", CacheLayer.Full, ClearCache);
    }

    internal static void ClearCache() => s_cache.Clear();

    /// <summary>四段式解析；全 miss（含负缓存命中）抛同款 MapException——每次命中均重新构造抛出。</summary>
    internal static Type Resolve(string maskTypeName, Type? sourceType, Type targetType, PropertyInfo targetItem)
    {
        // AQN 形态（含逗号）直走 Type.GetType——Assembly.GetType 不接受装配限定名（net9 FileLoadException）
        if (maskTypeName.Contains(','))
        {
            var byAqn = Type.GetType(maskTypeName, throwOnError: false);
            return byAqn
                ?? throw new MapException(ErrorMessages.MaskTypeNameUnresolvable(maskTypeName, targetItem.Name));
        }
        var domainKey = targetType.Assembly.FullName ?? string.Empty;
        var resolved = s_cache.GetOrAdd((maskTypeName, domainKey), _ => ResolveCore(maskTypeName, sourceType, targetType));
        return resolved
            ?? throw new MapException(ErrorMessages.MaskTypeNameUnresolvable(maskTypeName, targetItem.Name));
    }

    private static Type? ResolveCore(string name, Type? sourceType, Type targetType)
    {
        // ① 解析域 = 目标（DTO）侧程序集
        var resolved = ResolveInAssembly(targetType.Assembly, name);
        if (resolved != null) return resolved;

        // ② 源侧程序集
        if (sourceType != null)
        {
            resolved = ResolveInAssembly(sourceType.Assembly, name);
            if (resolved != null) return resolved;
        }

        // ③ Type.GetType（AQN 兼容）
        var byTypeGetType = Type.GetType(name, throwOnError: false);
        if (byTypeGetType != null) return byTypeGetType;

        // ④ 已加载程序集扫描（歧义：解析域优先 → FullName 序取首 + Trace 记候选数）
        var candidates = AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType(name, throwOnError: false))
            .Where(t => t != null)
            .Select(t => t!)
            .Distinct()
            .OrderBy(t => t.Assembly.FullName, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 1) return candidates[0];
        if (candidates.Length > 1)
        {
            var preferred = candidates.FirstOrDefault(c => c.Assembly == targetType.Assembly) ?? candidates[0];
            if (MapTracer.GloballyEnabled)
                MapTracer.Write($"[NCMapper] MaskTypeNameResolver: type name '{name}' matched {candidates.Length} candidates across loaded assemblies; resolved to {preferred.FullName} ({preferred.Assembly.FullName})");
            return preferred;
        }
        return null;   // 负缓存：null 入缓存，命中每次重抛同款异常
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "运行时回退路径 best-effort 解析（Assembly.GetType）；调用链 RequiresUnreferencedCode 已声明，类型被 trim 时降级 MapException（显式失败优于静默）。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "GetNestedType 嵌套链解析为显示名形态 best-effort（先例同构）。")]
    private static Type? ResolveInAssembly(System.Reflection.Assembly assembly, string name)
    {
        var direct = assembly.GetType(name, throwOnError: false);
        if (direct != null) return direct;

        // 嵌套链（"." 形态）：最长可解析前缀 + 剩余段 GetNestedType（先例同构）
        var parts = name.Split('.');
        for (var i = parts.Length - 1; i >= 1; i--)
        {
            var outer = assembly.GetType(string.Join(".", parts, 0, i), throwOnError: false);
            if (outer == null) continue;
            var cur = outer;
            for (var j = i; j < parts.Length; j++)
            {
                cur = cur.GetNestedType(parts[j]);
                if (cur == null) break;
            }
            if (cur != null) return cur;
        }
        return null;
    }
}
