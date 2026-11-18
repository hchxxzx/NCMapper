using System;

namespace NCMapper.Attributes;

/// <summary>
/// 对级白名单开关 — 该 (S,T) 对禁用自动匹配（含自动扁平化），仅映射显式声明的属性。
/// <para>标注在映射目标侧（DTO）类上，与 [MapFrom] 同位；语义镜像 Fluent
/// <c>For&lt;S,T&gt;().NoAutoMatch()</c>（对级）。映射本身照常（方法生成/注册/调用不变），
/// 关闭的是"自动"三样：①未声明同名自动配对 ②配置感知补匹配 ③自动扁平化。</para>
/// <para><b>显式声明边界</b>：同属性标注 [MapProperty]（特性通道 ctor 为 (Type fromSource, string fromField)，
/// FromField 须显式给出；Fluent 通道 .Member(d =&gt; d.X) 点名即声明、缺省同名默认——两形态都算声明）。</para>
/// <para><b>与 [MapNoAutoInclude]（属性级）辨析</b>：对级 = 该对禁用自动匹配（整对白名单）；
/// 属性级 = 仅该字段跳过导航收集——粒度不同，双承载并行而非等价，两形态叠加取并集。</para>
/// <para><b>反向派生</b>：反向映射仅翻转正向显式声明（与 Fluent 侧 S-3a 一致）。</para>
/// <para><b>投影路径（v1.2 补——D3）</b>：实现 <see cref="IProjectionPolicy"/> 且
/// <c>RequiresDowngrade = true</c>——白名单语义无法在 SQL 投影中完整表达（未声明列不得被读出），
/// ORM 投影自动降级为二步内存映射（协议位沿 MapFormat/MapWhen/AfterMapping 全族先例）。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public class MapNoAutoMatchAttribute : Attribute, IProjectionPolicy
{
    /// <inheritdoc/>
    /// <remarks>恒 true：白名单对投影强制降级（未声明列泄漏防线）。</remarks>
    public bool RequiresDowngrade => true;
}
