using System.Reflection;
using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// ExpressionTreeDeepCopyProvider 安全深拷贝属性测试 — 验证循环引用检测和深拷贝正确性
/// <para>ExpressionTreeDeepCopyProvider 使用 ConditionalWeakTable 检测循环引用，不栈溢出</para>
/// <para>FastDeepCopyProvider 默认不做循环引用检测，遇到循环引用会栈溢出</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// <para>注意：DeepCopyProviderBase.Current 是全局状态，每个测试必须 try/finally 恢复</para>
/// </summary>
public class SafeDeepCopyProperties
{
    // ================================================================
    // 基本字段保留 — ExpressionTreeDeepCopyProvider 正确性
    // ================================================================

    /// <summary>
    /// SafeDeepCopy 保留基本类型字段 — ExpressionTreeDeepCopyProvider 深拷贝后值一致
    /// </summary>
    [Property]
    public bool SafeDeepCopy_Preserves_Simple_Fields(int id, string name, int age)
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            var user = new FuzzUser { Id = id, Name = name, Age = age };
            var copy = Mapper.DeepCopy(user)!;
            return copy.Id == id && copy.Name == name && copy.Age == age;
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    // ================================================================
    // 嵌套对象独立副本 — 深拷贝非浅拷贝
    // ================================================================

    /// <summary>
    /// SafeDeepCopy 嵌套对象独立 — 修改副本的子对象不影响原件
    /// </summary>
    [Property]
    public bool SafeDeepCopy_Nested_Independent(int id, string city, int zipCode)
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            var entity = new FuzzNestedEntity
            {
                Id = id,
                Address = new FuzzAddress { City = city, ZipCode = zipCode }
            };
            var copy = Mapper.DeepCopy(entity)!;
            if (copy.Address is null) return false;

            copy.Address.City = "__MODIFIED__";
            copy.Address.ZipCode = int.MinValue;

            return entity.Address.City == city && entity.Address.ZipCode == zipCode;
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    // ================================================================
    // 循环引用安全处理 — 不栈溢出，循环引用指向拷贝后自身
    // ================================================================

    /// <summary>
    /// SafeDeepCopy 循环引用安全处理 — 自引用节点不栈溢出，copy.Self 指向 copy 自身
    /// </summary>
    [Property]
    public bool SafeDeepCopy_CircularReference_Handled(int id, string name)
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            var node = new FuzzCircularNode { Id = id, Name = name };
            node.Self = node; // 循环引用

            var copy = Mapper.DeepCopy(node)!;
            return copy.Id == id
                && copy.Name == name
                && copy.Self is not null
                && ReferenceEquals(copy, copy.Self); // 循环引用指向拷贝后自身
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    // ================================================================
    // M-07: record 循环引用端到端 — 源生成器 NCMAP012 回退 + 运行时 CycleCache
    // ================================================================

    /// <summary>
    /// M-07 端到端：自引用 record（Parent 指向自身）经 DeepCopyRuntimeOnly 回退后不栈溢出
    /// <para>源生成器检测 FuzzRecordHolder 属性图循环 → 生成 DeepCopyRuntimeOnly 调用 →
    /// 运行时 ExpressionTreeDeepCopyProvider 用 CycleCache 打破循环</para>
    /// <para>验证：不栈溢出 + 构造器参数值保留 + CycleCache 命中返回非 null 占位</para>
    /// <para>注意：positional record 构造器执行时最终实例尚不存在，CycleCache 返回的是
    /// GetUninitializedObject 占位实例，因此 copy.Parent != copy（已知限制，非 bug）</para>
    /// </summary>
    [Property]
    public bool M07_RecordCircular_DeepCopy_NoStackOverflow(int id, string name)
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            // 构造自引用环：用反射设置 init-only 属性的 backing field
            var holder = new FuzzRecordHolder(id, name, null);
            typeof(FuzzRecordHolder)
                .GetField("<Parent>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(holder, holder);

            // 关键验证：不抛 StackOverflowException（测试能完成即通过）
            var copy = Mapper.DeepCopy(holder)!;
            return copy.Id == id
                && copy.Name == name
                && copy.Parent is not null; // CycleCache 命中返回占位实例（非 null）
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    /// <summary>
    /// M-07 端到端：线性链 record（无环）DeepCopy 后值保留且引用独立
    /// <para>Parent 为构造器参数 → TryCompileRecordCreator 匹配并递归 DeepCopy</para>
    /// <para>验证：全链值保留 + 深拷贝引用独立</para>
    /// </summary>
    [Property]
    public bool M07_RecordChain_DeepCopy_ValuesPreserved(int id1, int id2, string name1, string name2)
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = new ExpressionTreeDeepCopyProvider();
            // 线性链：head.Parent = tail, tail.Parent = null（全部通过构造器参数）
            var tail = new FuzzRecordHolder(id2, name2, null);
            var head = new FuzzRecordHolder(id1, name1, tail);

            var copy = Mapper.DeepCopy(head)!;
            return copy.Id == id1
                && copy.Name == name1
                && copy.Parent is not null
                && copy.Parent.Id == id2
                && copy.Parent.Name == name2
                && copy.Parent.Parent is null
                && !ReferenceEquals(tail, copy.Parent); // 深拷贝：引用独立
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }
}
