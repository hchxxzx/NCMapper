using NCMapper.Shared;
using System.Linq.Expressions;
using System.Reflection;

namespace NCMapper.Internal;

/// <summary>
/// 构建会话 — BuildMapBody 上下文要素的不可变打包（④ 策略化深化）。
/// <para>四必参（sourceType/targetType/sourceParameter/targetParameter）+ 逐层推导的
/// 属性列表与方向标志，显式化为单一会话对象，作为策略类统一入参载体。</para>
/// <para>v1.1 修正：不含 MapperDelegate——委托工厂按实际类型对在 3 处反射回指点
/// 独立解析（嵌套/集合元素类型对与会话顶层类型对不同，会话级委托必然错配，
/// 且 TryGetInlineMapBody 现签名无委托来源）；不含 InlineDepth——readonly struct
/// 构造即快照，递归中过期，递归保护语义失效，
/// 维持现状 [ThreadStatic] 实时读。</para>
/// </summary>
internal readonly struct BuildSession
{
    /// <summary>源对象类型</summary>
    public Type SourceType { get; init; }
    /// <summary>目标对象类型</summary>
    public Type TargetType { get; init; }
    /// <summary>源对象参数表达式</summary>
    public ParameterExpression SourceParameter { get; init; }
    /// <summary>目标对象参数表达式</summary>
    public ParameterExpression TargetParameter { get; init; }
    /// <summary>源属性列表（由 PropertyMatcher.GetPropertyList 产出，会话创建时计算一次）</summary>
    public List<PropertyInfo> SourceProperties { get; init; }
    /// <summary>目标属性列表</summary>
    public List<PropertyInfo> TargetProperties { get; init; }
    /// <summary>源类型是否标注 [MapFrom]</summary>
    public bool SourceIsDefine { get; init; }
    /// <summary>目标类型是否标注 [MapFrom]（isToDto 推导依据）</summary>
    public bool TargetIsDefine { get; init; }
    /// <summary>路径解析器（① 已建好的 s_typeResolver 注入点，会话持有引用）</summary>
    public IPathResolver<Type, PropertyInfo> PathResolver { get; init; }
    /// <summary>方向（isToDto = TargetIsDefine，派生属性）</summary>
    public bool IsToDto => TargetIsDefine;
}
