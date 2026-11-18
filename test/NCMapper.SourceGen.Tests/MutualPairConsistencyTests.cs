extern alias runtime;
using System;
using System.Collections.Generic;
using NCMapper;
using NCMapper.Attributes;
using runtime::NCMapper.Shared;
using Xunit;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// -1: 互指对判定一致性测试
    /// <para>运行时适配（PropertyMatcher.IsMutualMapFromPair）与共享规则（NCMapper.Shared.MutualMapPairRules，
    /// 双端物理同源 include）对同一场景表给出一致判定；生成侧适配由既有互指测试
    /// （MutualDeadFieldGenerationTests + NCMAP025 诊断测试）覆盖。</para>
    /// </summary>
    public class MutualPairConsistencyTests
    {
    // 样本类均声明为 private（生成器跳过 non-public 类型，避免生成开放泛型代码；运行时反射不受影响）
    [MapFrom(typeof(B))]
    private sealed class A { }

    [MapFrom(typeof(A))]
    private sealed class B { }

    [MapFrom(typeof(A))]
    private sealed class C { } // 单向：C 指向 A，A 未指向 C

    private sealed class D { }

    private sealed class E { } // 无标注

    [MapFrom(typeof(G<>))]
    private sealed class F<T> { }

    [MapFrom(typeof(F<>))]
    private sealed class G<T> { }

        /// <summary>场景表：互指对 / 反向 / 单向 / 无标注 / 泛型互指</summary>
        public static IEnumerable<object[]> Scenarios()
        {
            yield return new object[] { "互指对 A<->B", typeof(A), typeof(B), true };
            yield return new object[] { "反向 B<->A", typeof(B), typeof(A), true };
            yield return new object[] { "单向 C->A", typeof(C), typeof(A), false };
            yield return new object[] { "单向反向 A->C", typeof(A), typeof(C), false };
            yield return new object[] { "无标注 D<->E", typeof(D), typeof(E), false };
            yield return new object[] { "泛型互指 F<->G", typeof(F<>), typeof(G<>), true };
        }

        private static bool RuntimeHasMapFrom(Type t)
            => t.GetCustomAttributes(typeof(MapFromAttribute), true).Length > 0;

        private static bool RuntimePointsTo(Type t, Type target)
        {
            var attrs = t.GetCustomAttributes(typeof(MapFromAttribute), true);
            return attrs.Length > 0 && ((MapFromAttribute)attrs[0]).FromSource == target;
        }

        // Runtime_IsMutualMapFromPair_Matches_Expected 用例随
        // PropertyMatcher.IsMutualMapFromPair 退役删除（X2-B-1 块出清连带）；共享规则
        // 属性形式/映射对形式两用例保留（MutualMapPairRules 不受影响）

        /// <summary>共享规则组合判定（属性读取 + 规则）与场景期望一致——锁定规则入口正确性</summary>
        [Theory]
        [MemberData(nameof(Scenarios))]
        public void SharedRules_AttrForm_Matches_Expected(string name, Type source, Type target, bool expected)
            => Assert.True(MutualMapPairRules.IsMutualPair(
                RuntimeHasMapFrom(source),
                RuntimeHasMapFrom(target),
                RuntimePointsTo(source, target),
                RuntimePointsTo(target, source)) == expected, $"场景: {name}");

        /// <summary>映射对形式（生成侧 DetectMutualPairs 使用的入口）与属性形式对同一场景语义等价</summary>
        [Theory]
        [MemberData(nameof(Scenarios))]
        public void SharedRules_MappingPairForm_Equivalent_To_AttrForm(string name, Type source, Type target, bool expected)
        {
            // 映射对视角：映射 X(源=source, 目标=target) 与 Y(源=target, 目标=source) 交叉判定
            bool xExists = RuntimePointsTo(source, target);   // X.Source == Y.Target
            bool yExists = RuntimePointsTo(target, source);   // X.Target == Y.Source
            Assert.True(expected == MutualMapPairRules.IsMutualMappingPair(xExists, yExists), $"场景: {name}");
        }

        /// <summary>P2-7 单向指向守卫（NCMAP026 抑制）语义与运行时属性读取一致</summary>
        [Theory]
        [InlineData(typeof(C), typeof(A), true)] // C 标 [MapFrom(A)]
        [InlineData(typeof(A), typeof(B), true)] // A 标 [MapFrom(B)]
        [InlineData(typeof(D), typeof(A), false)] // D 无标注
        [InlineData(typeof(A), typeof(C), false)] // A 未指向 C
        public void PointsToTarget_Matches_RuntimeAttr(Type source, Type target, bool expected)
            => Assert.Equal(expected, MutualMapPairRules.PointsToTarget(
                RuntimeHasMapFrom(source), RuntimePointsTo(source, target)));
    }
}
