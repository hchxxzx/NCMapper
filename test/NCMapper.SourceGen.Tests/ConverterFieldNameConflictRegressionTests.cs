using NCMapper.SourceGen.Tests.Models;
using NCMapper.Generated;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 转换器字段命名冲突 CS0102 回归测试
/// <para>2026-07-18 修复 GenerateConverterFields 字段命名冲突：</para>
/// <para>修复前：字段名格式 __conv_{PropertyName}，不同 DTO 同名属性 + 不同转换器时 CS0102</para>
/// <para>修复后：字段名格式 __conv_{TargetClassName}_{PropertyName}，加 DTO 类名前缀消除冲突</para>
/// <para>本测试直接调用 GeneratedMapper 静态方法 — 若 CS0102 复发，编译失败</para>
/// </summary>
public class ConverterFieldNameConflictRegressionTests
{
    /// <summary>
    /// 两个 DTO 同名属性 + 不同转换器 — 编译通过且各自调用正确转换器
    /// <para>SgConflictDtoA 和 SgConflictDtoB 都有 ComputedField 属性，
    /// 分别使用 SgComputedConverterA 和 SgComputedConverterB</para>
    /// <para>修复前：GenerateConverterFields 生成两个 __conv_ComputedField 字段 → CS0102</para>
    /// <para>修复后：生成 __conv_SgConflictDtoA_ComputedField 和 __conv_SgConflictDtoB_ComputedField</para>
    /// </summary>
    [Fact]
    public void SamePropertyName_DifferentConverter_NoConflict()
    {
        var entity = new SgConflictSourceEntity { Id = 42, Name = "Test" };

        // 两个 DTO 都能正确映射 — 若 CS0102 复发，GeneratedMapper 类不存在，编译失败
        var dtoA = GeneratedMapper.Map_SgConflictSourceEntity_To_SgConflictDtoA(entity)!;
        var dtoB = GeneratedMapper.Map_SgConflictSourceEntity_To_SgConflictDtoB(entity)!;

        Assert.Equal(42, dtoA.Id);
        Assert.Equal("A-Test", dtoA.ComputedField);  // 转换器 A

        Assert.Equal(42, dtoB.Id);
        Assert.Equal("B-Test", dtoB.ComputedField);  // 转换器 B
    }

    /// <summary>
    /// 两个 DTO 的 MapList 路径都能正确调用各自的转换器
    /// <para>验证内联元素路径的 GenerateSinglePropertyAssignment 也用了正确的字段名</para>
    /// </summary>
    [Fact]
    public void SamePropertyName_DifferentConverter_MapList_BothCorrect()
    {
        var entities = new[]
        {
            new SgConflictSourceEntity { Id = 1, Name = "Alice" },
            new SgConflictSourceEntity { Id = 2, Name = "Bob" }
        };

        var listA = GeneratedMapper.MapList_SgConflictSourceEntity_To_SgConflictDtoA(entities)!;
        var listB = GeneratedMapper.MapList_SgConflictSourceEntity_To_SgConflictDtoB(entities)!;

        Assert.Equal("A-Alice", listA[0].ComputedField);
        Assert.Equal("A-Bob", listA[1].ComputedField);
        Assert.Equal("B-Alice", listB[0].ComputedField);
        Assert.Equal("B-Bob", listB[1].ComputedField);
    }

    /// <summary>
    /// 反向映射也能正确编译（SgConflictDtoA → SgConflictSourceEntity）
    /// <para>验证反向映射路径不会因转换器字段名问题崩溃</para>
    /// </summary>
    [Fact]
    public void SamePropertyName_DifferentConverter_ReverseMap_BothCorrect()
    {
        var dtoA = new SgConflictDtoA { Id = 100, ComputedField = "Ignored" };
        var dtoB = new SgConflictDtoB { Id = 200, ComputedField = "Ignored" };

        var entityA = GeneratedMapper.Map_SgConflictDtoA_To_SgConflictSourceEntity(dtoA)!;
        var entityB = GeneratedMapper.Map_SgConflictDtoB_To_SgConflictSourceEntity(dtoB)!;

        Assert.Equal(100, entityA.Id);
        Assert.Equal(200, entityB.Id);
    }
}
