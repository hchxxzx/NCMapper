using FsCheck.Xunit;
using NCMapper.FuzzTests.Models;

namespace NCMapper.FuzzTests;

/// <summary>
/// 高级深拷贝属性测试 — RegisterGeneratedDeepCopy 自定义委托 + JsonDeepCopyProvider
/// <para>覆盖路径：注册自定义 DeepCopy 委托、JSON 序列化深拷贝方案</para>
/// <para>每个 [Property] 默认运行 100 次随机输入，失败时自动 shrinking 到最小复现</para>
/// <para>注意：全局状态（DeepCopyProviderBase.Current / RegisterGeneratedDeepCopy），每个测试必须 try/finally 恢复</para>
/// </summary>
public class AdvancedDeepCopyProperties
{
    // ================================================================
    // RegisterGeneratedDeepCopy — 注册自定义 DeepCopy 委托
    // ================================================================

    /// <summary>
    /// 注册后 DeepCopy 使用注册的委托 — 注册自定义转换逻辑覆盖默认深拷贝
    /// </summary>
    [Property]
    public bool RegisterGeneratedDeepCopy_Uses_Registered_Func(int id, string name, int age)
    {
        // 清除可能残留的注册
        Mapper.RegisterGeneratedDeepCopy<FuzzUser>((Func<FuzzUser, FuzzUser>?)null);
        try
        {
            Mapper.RegisterGeneratedDeepCopy<FuzzUser>(src =>
                new FuzzUser { Id = src.Id * 10, Name = "COPY:" + src.Name, Age = src.Age });

            var user = new FuzzUser { Id = id, Name = name, Age = age };
            var copy = Mapper.DeepCopy(user)!;
            return copy.Id == id * 10
                && copy.Name == "COPY:" + name
                && copy.Age == age;
        }
        finally
        {
            Mapper.RegisterGeneratedDeepCopy<FuzzUser>((Func<FuzzUser, FuzzUser>?)null);
        }
    }

    /// <summary>
    /// 注册后 HasGeneratedDeepCopy 返回 true — 未注册时返回 false
    /// </summary>
    [Property]
    public bool HasGeneratedDeepCopy_True_After_Register(int dummy)
    {
        Mapper.RegisterGeneratedDeepCopy<FuzzUser>((Func<FuzzUser, FuzzUser>?)null);
        try
        {
            Mapper.RegisterGeneratedDeepCopy<FuzzUser>(src => new FuzzUser { Id = src.Id });
            return Mapper.HasGeneratedDeepCopy<FuzzUser>();
        }
        finally
        {
            Mapper.RegisterGeneratedDeepCopy<FuzzUser>((Func<FuzzUser, FuzzUser>?)null);
        }
    }

    /// <summary>
    /// 清除注册后 HasGeneratedDeepCopy 返回 false — null 注册恢复默认行为
    /// </summary>
    [Property]
    public bool HasGeneratedDeepCopy_False_After_Clear(int dummy)
    {
        Mapper.RegisterGeneratedDeepCopy<FuzzUser>((Func<FuzzUser, FuzzUser>?)null);
        try
        {
            Mapper.RegisterGeneratedDeepCopy<FuzzUser>(src => new FuzzUser { Id = src.Id });
            Mapper.RegisterGeneratedDeepCopy<FuzzUser>((Func<FuzzUser, FuzzUser>?)null); // 清除
            return !Mapper.HasGeneratedDeepCopy<FuzzUser>();
        }
        finally
        {
            Mapper.RegisterGeneratedDeepCopy<FuzzUser>((Func<FuzzUser, FuzzUser>?)null);
        }
    }

    // ================================================================
    // JsonDeepCopyProvider — JSON 序列化深拷贝方案
    // ================================================================

    /// <summary>
    /// JsonDeepCopy 保留基本类型字段 — JSON 序列化往返后值一致
    /// </summary>
    [Property]
    public bool JsonDeepCopy_Preserves_Simple_Fields(int id, string name, int age)
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = DeepCopyProviderBase.JsonProvider;
            var user = new FuzzUser { Id = id, Name = name, Age = age };
            var copy = Mapper.DeepCopy(user)!;
            return copy.Id == id && copy.Name == name && copy.Age == age;
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }

    /// <summary>
    /// JsonDeepCopy 嵌套对象独立 — JSON 序列化天然深拷贝，修改副本不影响原件
    /// </summary>
    [Property]
    public bool JsonDeepCopy_Nested_Independent(int id, string city, int zipCode)
    {
        var original = DeepCopyProviderBase.Current;
        try
        {
            DeepCopyProviderBase.Current = DeepCopyProviderBase.JsonProvider;
            var entity = new FuzzNestedEntity
            {
                Id = id,
                Address = new FuzzAddress { City = city, ZipCode = zipCode }
            };
            var copy = Mapper.DeepCopy(entity)!;
            if (copy.Address is null) return false;
            copy.Address.City = "__MODIFIED__";
            return entity.Address.City == city;
        }
        finally
        {
            DeepCopyProviderBase.Current = original;
        }
    }
}
