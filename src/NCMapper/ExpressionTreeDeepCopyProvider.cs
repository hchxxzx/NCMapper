using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;
namespace NCMapper;
/// <summary>
/// 基于表达式树编译的深拷贝实现 — 转发到 <see cref="DeepCopyCompiler"/> 共享工具类
/// <para>两阶段设计是循环引用检测的关键：空实例在阶段1后即加入缓存，阶段2递归时能命中缓存</para>
/// <para>性能接近手写深拷贝代码，远优于 JSON 序列化中转方案</para>
/// <para>表达式树编译逻辑提取到 <see cref="DeepCopyCompiler"/>，消除 FastDeepCopyProvider 对本类的直接依赖（DIP 修复）</para>
/// </summary>
[System.Diagnostics.CodeAnalysis.RequiresDynamicCode("ExpressionTreeDeepCopyProvider uses Expression.Compile() for deep copy. Use source-generated deep copy for AOT scenarios.")]
public sealed class ExpressionTreeDeepCopyProvider : DeepCopyProviderBase
{
    /// <inheritdoc/>
    [RequiresDynamicCode("CreateInstance builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("CreateInstance scans type members and attributes via reflection. Some members may be trimmed.")]
    protected override T CreateInstance<T>(T source) => DeepCopyCompiler.CreateInstanceCore<T>(source);

    /// <inheritdoc/>
    [RequiresDynamicCode("FillProperties builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("FillProperties scans type members and attributes via reflection. Some members may be trimmed.")]
    protected override void FillProperties<T>(T source, T target) => DeepCopyCompiler.FillPropertiesCore<T>(source, target);

    /// <summary>
    /// 带上下文的阶段1（热路径）— 转发到 DeepCopyCompiler 带 ctx 版本
    /// <para>方向 I 优化：将 ctx 传递到 DeepCopyCompiler 编译的委托，使递归 DeepCopy 调用复用 ctx，
    /// 保持循环引用检测有效（Expression 路径核心优化点）。</para>
    /// </summary>
    [RequiresDynamicCode("CreateInstance builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("CreateInstance scans type members and attributes via reflection. Some members may be trimmed.")]
    internal override T CreateInstance<T>(T source, DeepCopyContext ctx)
        => DeepCopyCompiler.CreateInstanceCore<T>(source, ctx);

    /// <summary>
    /// 带上下文的阶段2（热路径）— 转发到 DeepCopyCompiler 带 ctx 版本
    /// <para>方向 I 优化：将 ctx 传递到 DeepCopyCompiler 编译的委托，使递归 DeepCopy 调用复用 ctx，
    /// 保持循环引用检测有效。原 [ThreadStatic] s_cycleCache+s_depth 每次递归 6-8 次访问，
    /// net9/10 ABI 从 SHARED 改为 DYNAMIC 导致 ~427ns/ops 性能回退，本优化消除该开销。</para>
    /// </summary>
    [RequiresDynamicCode("FillProperties builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("FillProperties scans type members and attributes via reflection. Some members may be trimmed.")]
    internal override void FillProperties<T>(T source, T target, DeepCopyContext ctx)
        => DeepCopyCompiler.FillPropertiesCore<T>(source, target, ctx);
}
