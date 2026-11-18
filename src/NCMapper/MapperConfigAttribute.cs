namespace NCMapper;

/// <summary>
/// 声明 Fluent 映射配置入口类 — 源生成器扫描该类内所有
/// <c>static void M(FluentMappingConfig cfg)</c> 方法（方法名任意）提取映射意图
/// <para>Fluent 通道入口锚点（A1.5 方案）：与 [MapFrom] 特性通道并存（模式并存）——
/// 用户可对不同 (S,T) 类型对自由混合使用两通道；同一 (S,T) 对被两通道同时声明时
/// 触发 NCMAP_F11 互斥禁止诊断。</para>
/// <para>类承载映射意图，DTO 类保持干净（[MapFrom] 标在 DTO 类上，本特性标在独立的 Mapper 配置类上）。</para>
/// </summary>
/// <remarks>
/// 入口约定：类内声明 <c>public static void 任意方法名(FluentMappingConfig cfg)</c>，
/// 方法体内以 <c>cfg.For&lt;TSource, TTarget&gt;()... 链式声明映射</c>。
/// 类内没有任何匹配签名方法时触发 NCMAP_F15 提示。
/// </remarks>
[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MapperConfigAttribute : System.Attribute
{
}
