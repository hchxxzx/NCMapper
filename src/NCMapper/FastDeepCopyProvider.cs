using System.Diagnostics.CodeAnalysis;
using System.Threading;
namespace NCMapper;
/// <summary>
/// 快速深拷贝实现 — 方案 F 条件分发器：三值分类（<see cref="DeepCopyTypeClassifier"/>）+ Acyclic 零检测快路径
/// <para>分发规则：类型图分类为 <see cref="DeepCopyGraphShape.Acyclic"/> 时走快路径
/// （环形缓冲仅作为分类假阴性兜底，正常不命中）；<see cref="DeepCopyGraphShape.Cyclic"/>/
/// <see cref="DeepCopyGraphShape.Unknown"/> 时转发基类完整检测（CycleCache + record 占位），
/// 支持任意环长的循环引用与自引用 record。</para>
/// <para>走 TryCompileRecordCreator 构参路径的类型
/// （无无参公共构造器、非 string、非集合）即使分类为 Acyclic 也统一转发基类占位路径——
/// 其构参递归早于环形缓冲压槽，防构参链成环时环检测只剩深度守卫（详见
/// <see cref="DeepCopyProviderBase"/> 占位分支）。</para>
/// <para>Acyclic 快路径性能与 <see cref="ExpressionTreeDeepCopyProvider"/> 接近（~142ns vs ~167ns，Fast 略快约 1.2 倍），
/// 保留递归深度保护（<see cref="Mapper.MaxRecursionDepth"/>）。</para>
/// <para>需要 DAG 共享引用保持（同一实例拷贝后仍共享）时，请使用 <see cref="ExpressionTreeDeepCopyProvider"/>。</para>
/// <para>用法：<c>DeepCopyProviderBase.Current = new FastDeepCopyProvider();</c></para>
/// </summary>
public sealed class FastDeepCopyProvider : DeepCopyProviderBase
{
    // 移除硬编码 const MaxRecursionDepth = 500，改用 Mapper.MaxRecursionDepth 尊重用户全局配置

    /// <summary>
    /// 重写 DeepCopy — 方案 F 条件分发器入口（新建 ctx 后转发带 ctx 重载）
    /// <para>Acyclic 类型走零检测快路径（环形缓冲仅作假阴性兜底）；
    /// Cyclic/Unknown 类型转发基类完整检测（CycleCache + record 占位，任意环长）。</para>
    /// <para>表达式树编译的 FillProperties 中引用属性递归调用 Current.DeepCopy，
    /// 当 Current 为 FastDeepCopyProvider 时自动走此方法，递归也走此路径。</para>
    /// <para>递归深度超过 <see cref="Mapper.MaxRecursionDepth"/> 时抛出
    /// <see cref="MapException"/>（可 catch），防止循环引用导致的
    /// <see cref="StackOverflowException"/>（不可 catch，进程崩溃）。</para>
    /// </summary>
    /// <param name="source">源对象</param>
    /// <returns>深拷贝后的新对象；source为null时返回null</returns>
    /// <exception cref="MapException">递归深度超过 <see cref="Mapper.MaxRecursionDepth"/>，可能存在循环引用</exception>
    [RequiresDynamicCode("DeepCopy uses RuntimeHelpers.GetUninitializedObject and Expression.Compile in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy path to make this AOT-safe.")]
    [RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy path to make this AOT-safe.")]
    public override T? DeepCopy<T>(T source) where T : class
    {
        if (source == null) return null;
        return DeepCopy(source, new DeepCopyContext());
    }

    /// <summary>
    /// 带上下文的 Fast DeepCopy — 方案 F 条件分发器
    /// <para>分发顺序：
    /// null 检查 →<b>F1 顶层重置</b>（分发之前，防跨调用耦合）→ 分类分发 → Acyclic 快路径。</para>
    /// <para><b>D2 F1 顶层重置</b>：ctx.Depth == 0 时重置环形缓冲三字段，
    /// 防止 [ThreadStatic] ctx 复用导致跨调用泄漏；覆盖三入口（Mapper.DeepCopy 复用 ctx /
    /// 基类 DeepCopy 新建 ctx / DeepCopyObject 新建 ctx）。必须在分类分发之前执行，
    /// 保证任何顶层调用入口都不残留历史槽位。</para>
    /// <para><b>分类分发</b>：<see cref="DeepCopyTypeClassifier.Cache{T}.Value"/> 为
    /// <see cref="DeepCopyGraphShape.Cyclic"/>/<see cref="DeepCopyGraphShape.Unknown"/> 时
    /// 转发基类完整检测（CycleCache + record 占位，支持任意环长）；
    /// 走 TryCompileRecordCreator 构参路径的类型
    /// （无无参公共构造器、非 string、非集合）同样转发（构参递归早于压槽）。
    /// <b>D3</b>：转发前不得 ctx.Depth++ —— 基类自带自增（DeepCopyProviderBase.cs:89），先行自增会双重计数。</para>
    /// <para><b>Acyclic 快路径</b>：保留方案 E 实现 —— 环扫描（分类假阴性兜底 D5，正常不命中）→
    /// Depth++ → 深度守卫 → CreateInstance → 入环形缓冲 → FillProperties → F2 退出回滚。
    /// 方向 I 优化：用 ctx.Depth 替代 [ThreadStatic] s_fastDepth，
    /// 避免 net9/10 ThreadStatic ABI 从 SHARED 改为 DYNAMIC 导致的性能回退。</para>
    /// <para>表达式树编译的 FillProperties 中引用属性递归调用 Current.DeepCopy，
    /// 当 Current 为 FastDeepCopyProvider 时自动走此方法，递归也走此路径（子类型按其自身分类独立分发）。</para>
    /// <para>顶层调用创建新 ctx，递归调用复用同一 ctx（由 DeepCopyCompiler 编译的委托传递）。</para>
    /// </summary>
    /// <typeparam name="T">对象类型（必须为引用类型）</typeparam>
    /// <param name="source">源对象</param>
    /// <param name="ctx">深拷贝上下文（承载递归深度、环形缓冲状态和循环引用缓存；Acyclic 快路径不使用 CycleCache）</param>
    /// <returns>深拷贝后的新对象；source为null时返回null</returns>
    /// <exception cref="MapException">递归深度超过 <see cref="Mapper.MaxRecursionDepth"/>，可能存在循环引用</exception>
    [RequiresDynamicCode("DeepCopy uses RuntimeHelpers.GetUninitializedObject and Expression.Compile in the runtime fallback path. Not supported in AOT. Use the source-generated deep copy path to make this AOT-safe.")]
    [RequiresUnreferencedCode("DeepCopy scans type members via reflection in the runtime fallback path. Some members may be trimmed. Use the source-generated deep copy path to make this AOT-safe.")]
    internal override T? DeepCopy<T>(T source, DeepCopyContext ctx) where T : class
    {
        if (source == null) return null;

        // F1 顶层入口重置环形缓冲（Depth==0 唯一标识顶层，覆盖三入口）。
        // 必须在分类分发之前执行（否定条件 递归中途 Depth>0 时不得清环）。
        // Mapper.DeepCopy 复用 [ThreadStatic] ctx，顶层退出仅 Clear CycleCache，不清理 Fast 路径状态。
        if (ctx.Depth == 0)
        {
            ctx.FastSlot = 0;
            if (ctx.FastSources != null)
            {
                Array.Clear(ctx.FastSources, 0, DeepCopyContext.FastRingSize);
                Array.Clear(ctx.FastTargets!, 0, DeepCopyContext.FastRingSize);
            }
        }

        // 分类分发：Cyclic/Unknown → 基类完整检测（CycleCache + record 占位，任意环长）。
        // 走 TryCompileRecordCreator 构参路径的类型
        // （无无参公共构造器、非 string、非集合——镜像 CreateInstanceCore 回退顺序）统一转发基类——
        // 其构参递归（TryCompileRecordCreator 的 DeepCopy 调用）发生在 CreateInstance 内、早于
        // 环形缓冲压槽，构参链成环且分类假阴性时环检测只剩深度守卫（合法环图误抛
        // RecursionDepthExceeded）；base 占位路径（CycleCache 占位先于构造）对构参递归环完整承接。
        // string（CreateInstanceCore 特判原样返回）与集合类型（CompileCollectionCopy 路径）无构参递归，
        // 保持 Acyclic 快路径；Acyclic 普通 POCO 快路径不动。
        // 条件顺序（性能）：HasParameterlessConstructor 缓存字典查询（~2ns）前置短路——多数 POCO
        // 在此处即跳出，不评估 IsCollectionType 的 IsAssignableFrom（~15ns/次，仅无参构造缺失类型评估）。
        // 转发前不得 ctx.Depth++ —— 基类 DeepCopy 自增（DeepCopyProviderBase.cs:89），先行自增会双重计数。
        if (DeepCopyTypeClassifier.Cache<T>.Value != DeepCopyGraphShape.Acyclic
            || (!DeepCopyProviderBase.HasParameterlessConstructor(typeof(T))
                && typeof(T) != typeof(string)
                && !DeepCopyCompiler.IsCollectionType(typeof(T)))
            // 接口声明类型统一转发
            // base——Fast 快路径 CreateInstance 对接口集合（如 Acyclic 分类的
            // IDictionary<string,string>）无 creator/copier（接口泛型集合委托返回 null），
            // 落 兜底守卫会误伤原 JSON 可行路径；base 接口转发入口按运行时
            // 具体类型分发。非集合接口本就经 条件转发，此处补齐集合接口盲点
            || typeof(T).IsInterface)
            return base.DeepCopy(source, ctx);

        // ── Acyclic 快路径：方案 E 实现逐字保留 ──

        // 阶段 0：扫描环形缓冲（仅祖先链，F2 退出回滚保证）。
        // 保留为分类假阴性兜底：声明类型判 Acyclic 但运行时多态实例携带环
        // （环长 ≤ 8）时仍能打破循环；环长 > 8 由深度守卫兜底。
        var sources = ctx.FastSources;
        if (sources != null)
        {
            // FastSlot==0 ⟺ 无祖先链——压槽（:158 FastSlot++）
            // 与退出回滚（:168-174 还原 prevSlot）严格成对、顶层（:80-88 Depth==0）清零，
            // 同步性核验通过（核验点）；此时环形缓冲内容全为还原后 null，扫描必不
            // 命中——顶层首节点不可能自环，免 8 槽扫描。D5 兜底保留：有祖先场景照扫，
            // 环长 ≤ 8 覆盖不变，不得改为按计数叠加。
            if (ctx.FastSlot > 0)
            {
                for (int i = 0; i < DeepCopyContext.FastRingSize; i++)
                {
                    if (ReferenceEquals(sources[i], source))
                        // FastSources 与 FastTargets 为配对数组（同长度同时分配），
                        // 外层判 FastSources 非空时 FastTargets 必非空（修 #4 CS8602）
                        return (T)ctx.FastTargets![i]!;
                }
            }
        }

        ctx.Depth++;
        int prevSlot = ctx.FastSlot;  // F2 方案 b：记录回滚点
        // 压入前备份被覆盖槽位旧值，回滚时还原而非置 null
        // 深分支（>8 层）回绕覆盖祖先后，回溯时祖先链得以保留（兄弟分支短环可检出）。
        // 备份为栈上双值，热路径零新增分配；正常无回绕场景备份为 null，还原 == 置 null，行为与现状一致
        object? backupSource = null;
        object? backupTarget = null;
        bool pushed = false;
        try
        {
            // 递归深度保护 — 超过限制抛出可 catch 的异常，而非不可 catch 的 StackOverflowException
            if (ctx.Depth > Mapper.MaxRecursionDepth)
                throw new MapException(
                    ErrorMessages.RecursionDepthExceeded(Mapper.MaxRecursionDepth, typeof(T).Name, typeof(T).Name),
                    typeof(T), typeof(T));

            var result = CreateInstance(source, ctx);
            if (result != null)
            {
                // 关键：在 FillProperties 之前入环形缓冲，递归命中返回占位实例（循环被打破）
                ctx.FastSources ??= new object[DeepCopyContext.FastRingSize];
                ctx.FastTargets ??= new object[DeepCopyContext.FastRingSize];
                int slot = ctx.FastSlot & (DeepCopyContext.FastRingSize - 1);
                // S1备份被覆盖槽位旧值（回绕场景为被覆盖的祖先节点；正常场景为 null）
                backupSource = ctx.FastSources[slot];
                backupTarget = ctx.FastTargets![slot];
                ctx.FastSources[slot] = source;
                ctx.FastTargets[slot] = result;
                ctx.FastSlot++;
                pushed = true;

                FillProperties(source, result, ctx);
            }
            return result;
        }
        finally
        {
            // F2 方案 b + S1退出回滚，还原本次压入的槽位（而非置 null），环形缓冲仅保留祖先链
            if (pushed && ctx.FastSources != null && ctx.FastSlot > prevSlot)
            {
                int slot = prevSlot & (DeepCopyContext.FastRingSize - 1);
                ctx.FastSources[slot] = backupSource;
                ctx.FastTargets![slot] = backupTarget;
                ctx.FastSlot = prevSlot;
            }
            ctx.Depth--;
        }
    }

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
    /// <para>方向 I 优化：将 ctx 传递到 DeepCopyCompiler 编译的委托，使递归 DeepCopy 调用复用 ctx。</para>
    /// </summary>
    [RequiresDynamicCode("CreateInstance builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("CreateInstance scans type members and attributes via reflection. Some members may be trimmed.")]
    internal override T CreateInstance<T>(T source, DeepCopyContext ctx)
        => DeepCopyCompiler.CreateInstanceCore<T>(source, ctx);

    /// <summary>
    /// 带上下文的阶段2（热路径）— 转发到 DeepCopyCompiler 带 ctx 版本
    /// <para>方向 I 优化：将 ctx 传递到 DeepCopyCompiler 编译的委托，使递归 DeepCopy 调用复用 ctx。</para>
    /// </summary>
    [RequiresDynamicCode("FillProperties builds expressions via MakeGenericMethod/MakeGenericType or Expression.Compile(). Not supported in AOT.")]
    [RequiresUnreferencedCode("FillProperties scans type members and attributes via reflection. Some members may be trimmed.")]
    internal override void FillProperties<T>(T source, T target, DeepCopyContext ctx)
        => DeepCopyCompiler.FillPropertiesCore<T>(source, target, ctx);
}
