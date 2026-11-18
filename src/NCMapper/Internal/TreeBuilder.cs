// : BuildTree 上移 —— 树构建内核（Dapper 现役 + EFCore 树 A4 预留消费）
// 从 DapperDtoSelector.BuildTree 迁移（迁移前行为锁定：现有 Dapper 树测试 4 用例回归 + 变异验证闭环）。
// 树契约落地：自动查找（FirstOrDefault）→ 精确名查找（[MapTree].ChildrenProperty ?? "Children"），
// 三判据逐项校验（存在性→自引用→可写性）——消除多自引用集合非确定性。
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NCMapper.Attributes;
using NCMapper.Runtime;

namespace NCMapper.Internal;

/// <summary>
/// 树构建内核 —— 平铺列表 → 嵌套树（零 ORM 依赖）
/// <para>internal 类型，经 InternalsVisibleTo 供 NCMapper.Dapper / NCMapper.EFCore 扩展包使用</para>
/// <para>契约：DTO 须含 Id/ParentId 属性与 Children 自引用集合（默认名，[MapTree] 三属性均可自定义）</para>
/// <para>EFCore 树（A4 独立立项）消费同一内核 —— 上移第二消费者真实化</para>
/// </summary>
internal static class TreeBuilder
{
    /// <summary>
    /// 构建嵌套树（根节点列表）—— 按 Id 建索引，ParentId 命中父节点则挂载，否则为根
    /// </summary>
    /// <typeparam name="TDto">DTO 类型（含 Id/ParentId/Children 契约属性）</typeparam>
    /// <param name="flatList">平铺列表（父节点无须先于子节点——先按 Id 全量索引再挂载）</param>
    /// <param name="config">树契约配置（由 ResolveConfig 从 [MapTree] 解析或取默认）</param>
    /// <returns>根节点列表（递归嵌套；空列表原样返回）</returns>
    [RequiresDynamicCode("TreeBuilder.Build builds the tree structure via MakeGenericType and reflection-created collections. Not supported in AOT.")]
    [RequiresUnreferencedCode("TreeBuilder.Build scans type members and attributes via reflection. Some members may be trimmed.")]
    internal static List<TDto> Build<TDto>(List<TDto> flatList, TreeConfig config) where TDto : class
    {
        if (flatList.Count == 0) return flatList;

        var type = typeof(TDto);
        // T-2b 契约：Children 精确名查找 + 三判据校验（见 ResolveChildrenProp）
        var childrenProp = ResolveChildrenProp(type, config);

        // 查找 Id 和 ParentId 属性（名称来自 [MapTree] 解析或默认，见 ResolveConfig）
        var idProp = type.GetProperty(config.IdProperty, BindingFlags.Public | BindingFlags.Instance);
        var parentIdProp = type.GetProperty(config.ParentIdProperty, BindingFlags.Public | BindingFlags.Instance);
        if (idProp == null || parentIdProp == null)
            throw new MapConfigurationException(
                ErrorMessages.TreeMissingIdParentId(type.FullName!, config.IdProperty, config.ParentIdProperty),
                sourceType: null, targetType: type);

        // 按 Id 建索引
        var byId = new Dictionary<object, TDto>();
        foreach (var item in flatList)
        {
            var id = idProp.GetValue(item);
            if (id != null)
            {
                // 重复 Id 覆盖告警
                //（原 静默覆盖零观测面——后项覆盖前项索引的既有语义保持，仅补可观测）
                if (byId.TryGetValue(id, out var prev) && !ReferenceEquals(prev, item))
                    MapTracer.Write($"[NCMapper] TreeBuilder: 重复 Id {id}（{type.Name}），后项覆盖前项索引（挂载语义保持）");
                byId[id] = item;
            }
        }

        var roots = new List<TDto>();
        foreach (var item in flatList)
        {
            var parentId = parentIdProp.GetValue(item);
            if (parentId != null && byId.TryGetValue(parentId, out var parent))
            {
                // 自环/互指显式告警（原静默消失于 roots——保持挂载语义不变，
                // 自环自挂载/互指对偶互挂，均不出 roots，仅补可观测；互指对在第二节点再告警一次）
                var itemId = idProp.GetValue(item);
                if (Equals(parentId, itemId))
                    MapTracer.Write($"[NCMapper] TreeBuilder: 节点 Id == ParentId 自环（Id={itemId}, {type.Name}），保持自挂载不出 roots");
                else if (Equals(parentIdProp.GetValue(parent), itemId))
                    MapTracer.Write($"[NCMapper] TreeBuilder: 节点互指（{parent.GetType().Name} Id={parentId} ↔ {type.Name} Id={itemId}），保持对偶互挂不出 roots");

                var children = (System.Collections.IList?)childrenProp.GetValue(parent);
                if (children == null)
                {
                    // 惰性创建固定 List<TDto>（锁定现状语义——接口类型属性同样塞 List 实例）
#pragma warning disable IL2060 // MakeGenericType 在 trim 下类型实参可被擦除（迁移自 DapperDtoSelector.BuildTree，作用域仅本调用行）
                    children = (System.Collections.IList)Activator.CreateInstance(
                        typeof(List<>).MakeGenericType(type))!;
#pragma warning restore IL2060
                    childrenProp.SetValue(parent, children);
                }
                children.Add(item);
            }
            else
            {
                roots.Add(item);
            }
        }
        return roots;
    }

    /// <summary>
    /// 解析 Children 属性 —— 精确名查找（config.ChildrenProperty，默认 "Children"）+ 三判据逐项校验
    /// <para>判据①存在性：属性缺失抛 TreeMissingChildrenCollectionProperty（报期望名 + [MapTree] 引导 + 扁平提示）</para>
    /// <para>判据②自引用：泛型集合且元素类型 == TDto，否则抛 TreeChildrenPropertyInvalid（同名非自引用）</para>
    /// <para>判据③可写性：CanWrite，否则抛 TreeChildrenPropertyInvalid（同名只读）——两种失败因消息区分</para>
    /// </summary>
    /// <param name="type">DTO 类型</param>
    /// <param name="config">树契约配置</param>
    /// <returns>满足契约的 Children 属性（可写自引用集合）</returns>
    // private → internal —— EFCore BuildTreeFromFlat 须在 slot 清理前
    // 前置三判据校验并复用本内核（经 InternalsVisibleTo NCMapper.EFCore，非公开面，X7 无影响）
    [RequiresUnreferencedCode("TreeBuilder.ResolveChildrenProp scans type members via reflection. Some members may be trimmed.")]
    internal static PropertyInfo ResolveChildrenProp(Type type, TreeConfig config)
    {
        var prop = type.GetProperty(config.ChildrenProperty, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null)
            throw new MapConfigurationException(
                ErrorMessages.TreeMissingChildrenCollectionProperty(type.FullName!, type.Name, config.ChildrenProperty),
                sourceType: null, targetType: type);

        var isSelfRef = prop.PropertyType.IsGenericType
            && prop.PropertyType.GetGenericArguments().FirstOrDefault() == type;
        if (!isSelfRef)
            throw new MapConfigurationException(
                ErrorMessages.TreeChildrenPropertyInvalid(type.FullName!, type.Name, config.ChildrenProperty,
                    prop.PropertyType.FullName ?? prop.PropertyType.Name),
                sourceType: null, targetType: type);

        if (!prop.CanWrite)
            throw new MapConfigurationException(
                ErrorMessages.TreeChildrenPropertyInvalid(type.FullName!, type.Name, config.ChildrenProperty,
                    prop.PropertyType.FullName ?? prop.PropertyType.Name, isReadOnly: true),
                sourceType: null, targetType: type);

        return prop;
    }

    /// <summary>
    /// 解析树契约配置 —— 注册表优先（Fluent .Tree() 承载）+ [MapTree] 反射回退（消费点一）
    /// </summary>
    /// <param name="type">DTO 类型</param>
    /// <returns>树契约配置（注册表三元组 / [MapTree] 三属性名或默认名）</returns>
    /// <remarks>
    /// <para>双读语义：RuntimeSnapshotStore.GetTreeConfig 命中（Fluent .Tree()，opt-in）→ 注册表三元组权威；
    /// null（未注册/未调用 .Tree()）→ 反射 [MapTree]——有标注用特性三元组，无标注取 Default
    ///（[MapFrom] 通道既有语义零变化——本方法调用方均为树构建入口，Default 语义不变）。</para>
    /// </remarks>
    [RequiresUnreferencedCode("TreeBuilder.ResolveConfig reads the [MapTree] attribute via reflection. Some members may be trimmed.")]
    internal static TreeConfig ResolveConfig(Type type)
    {
        var registryConfig = RuntimeSnapshotStore.GetTreeConfig(type);
        if (registryConfig is not null)
            return new TreeConfig(registryConfig.IdProperty, registryConfig.ParentIdProperty, registryConfig.ChildrenProperty);

        // G6 T5 锚门回滚——[MapTree] 是类级树构建配置
        //（Dapper/EFCore ToTreeList 场景的 DTO 天然无映射对，ToTreeList 调用即激活路径，
        // 不属"映射对属性级配置惰性"治理域； 后 NCMAP019 清单不含 [MapTree]）
        var treeAttr = type.GetCustomAttribute<MapTreeAttribute>();
        if (treeAttr is null) return TreeConfig.Default;
        return new TreeConfig(treeAttr.IdProperty, treeAttr.ParentIdProperty, treeAttr.ChildrenProperty);
    }
}

/// <summary>
/// 树契约配置 —— Id/ParentId/Children 三属性名（[MapTree] 解析结果）
/// <para>三属性均精确名查找（T-2b 契约）；Children 缺失/类型不符/只读抛 MapConfigurationException</para>
/// </summary>
internal sealed record TreeConfig(string IdProperty, string ParentIdProperty, string ChildrenProperty)
{
    /// <summary>
    /// 默认契约 —— Id/ParentId/Children（单一事实来源：ResolveConfig 无 [MapTree] 标注时引用，附录 A I-3）
    /// </summary>
    internal static readonly TreeConfig Default = new("Id", "ParentId", "Children");
}
