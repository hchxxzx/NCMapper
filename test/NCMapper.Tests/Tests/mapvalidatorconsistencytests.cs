using System;
using System.IO;
using System.Linq;
using NCMapper.Attributes;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 校验 vs 映射行为一致性测试（测试 2/3）：
    /// MapValidator 收敛后（判定权威 TypeCompatView + 匹配链 PropertyMatcher 复用，评审 M1/M4），
    /// 断言：① 校验结论与 Mapper.Map 实际行为无矛盾（校验 OK ⇔ 映射成功；校验 Error ⇔ 运行时
    /// 无转换路径——抛 MapException 或属性跳过）；② 校验匹配结果与 PropertyMatcher 匹配链一致。
    /// <para>校验结论变化（相对 v1.0 内联 OrdinalIgnoreCase 纯按名 + 自建规则表）属收敛目标内的
    /// 预期变化，变化清单登记（R3/Q3）：string→非 IConvertible 引用类型（如 Stream）
    /// 不再放行；非默认命名策略/FromField/反向标注消费与映射侧同源。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapValidatorConsistencyTests
    {
        // ---------- 测试 2：校验 vs 映射行为 ----------

        public class CompatSource { public int Id { get; set; } public string Name { get; set; } = "n"; }
        public class CompatTarget { public int Id { get; set; } public string Name { get; set; } = ""; }

        public class StringToIntSource { public string Value { get; set; } = "123"; }
        public class StringToIntTarget { public int Value { get; set; } }

        public class IntToDateTimeSource { public int Value { get; set; } }
        public class IntToDateTimeTarget { public DateTime Value { get; set; } }

        public class StringToStreamSource { public string Data { get; set; } = "x"; }
        public class StringToStreamTarget { public Stream Data { get; set; } = null!; }

        public class GuidToStringSource { public Guid Id { get; set; } }
        public class GuidToStringTarget { public string Id { get; set; } = ""; }

        public enum Color { Red, Green }
        public class EnumToStringSource { public Color C { get; set; } }
        public class EnumToStringTarget { public string C { get; set; } = ""; }

        public class NestedA { public int X { get; set; } }
        public class NestedB { public int X { get; set; } }
        public class NestedSource { public NestedA A { get; set; } = new(); }
        public class NestedTarget { public NestedB A { get; set; } = new(); }

        /// <summary>校验 OK ⇔ 运行时映射成功且值正确（判定面与执行面无矛盾）。</summary>
        [Fact]
        public void Validator_OK_Implies_Mapping_Succeeds_Compat()
        {
            var result = MapValidator.ValidateMapping(typeof(CompatSource), typeof(CompatTarget));
            Assert.Equal(MapValidationLevel.OK, result.Level);

            var mapped = Mapper.Map<CompatSource, CompatTarget>(new CompatSource { Id = 7, Name = "n" });
            Assert.NotNull(mapped);
            Assert.Equal(7, mapped!.Id);
            Assert.Equal("n", mapped.Name);
        }

        [Fact]
        public void Validator_OK_Implies_Mapping_Succeeds_StringToInt()
        {
            // string→int：判定 true（IConvertible 交叉），映射实际转换成功
            var result = MapValidator.ValidateMapping(typeof(StringToIntSource), typeof(StringToIntTarget));
            Assert.Equal(MapValidationLevel.OK, result.Level);

            var mapped = Mapper.Map<StringToIntSource, StringToIntTarget>(new StringToIntSource());
            Assert.NotNull(mapped);
            Assert.Equal(123, mapped!.Value);
        }

        [Fact]
        public void Validator_OK_Implies_Mapping_Succeeds_GuidToString()
        {
            var result = MapValidator.ValidateMapping(typeof(GuidToStringSource), typeof(GuidToStringTarget));
            Assert.Equal(MapValidationLevel.OK, result.Level);

            var guid = Guid.NewGuid();
            var mapped = Mapper.Map<GuidToStringSource, GuidToStringTarget>(new GuidToStringSource { Id = guid });
            Assert.NotNull(mapped);
            Assert.Equal(guid.ToString(), mapped!.Id);
        }

        [Fact]
        public void Validator_OK_Implies_Mapping_Succeeds_EnumToString()
        {
            var result = MapValidator.ValidateMapping(typeof(EnumToStringSource), typeof(EnumToStringTarget));
            Assert.Equal(MapValidationLevel.OK, result.Level);

            var mapped = Mapper.Map<EnumToStringSource, EnumToStringTarget>(new EnumToStringSource { C = Color.Green });
            Assert.NotNull(mapped);
            Assert.Equal("Green", mapped!.C);
        }

        [Fact]
        public void Validator_OK_Implies_Mapping_Succeeds_Nested()
        {
            var result = MapValidator.ValidateMapping(typeof(NestedSource), typeof(NestedTarget));
            Assert.Equal(MapValidationLevel.OK, result.Level);

            var mapped = Mapper.Map<NestedSource, NestedTarget>(new NestedSource { A = new NestedA { X = 5 } });
            Assert.NotNull(mapped);
            Assert.Equal(5, mapped!.A.X);
        }

        /// <summary>校验 Error（int→DateTime 判定 false）⇔ 运行时无转换路径（抛 MapException）。</summary>
        [Fact]
        public void Validator_Error_Implies_Mapping_Fails_IntToDateTime()
        {
            var result = MapValidator.ValidateMapping(typeof(IntToDateTimeSource), typeof(IntToDateTimeTarget));
            Assert.Equal(MapValidationLevel.Error, result.Level);
            Assert.NotEmpty(result.TypeMismatchProperties);

            Assert.Throws<MapException>(() =>
                Mapper.Map<IntToDateTimeSource, IntToDateTimeTarget>(new IntToDateTimeSource { Value = 42 }));
        }

        /// <summary>
        /// 校验 Error（string→Stream 判定 false，:341 宽松点收敛）⇔ 运行时无转换路径（抛 MapException）。
        /// v1.0 旧规则表对 string→任意类型放行（341），收敛后不再放行——预期校验结论变化（登记）。
        /// </summary>
        [Fact]
        public void Validator_Error_Implies_Mapping_Fails_StringToStream()
        {
            var result = MapValidator.ValidateMapping(typeof(StringToStreamSource), typeof(StringToStreamTarget));
            Assert.Equal(MapValidationLevel.Error, result.Level);
            Assert.Contains(result.TypeMismatchProperties, p => p.Contains("Data"));

            Assert.Throws<MapException>(() =>
                Mapper.Map<StringToStreamSource, StringToStreamTarget>(new StringToStreamSource()));
        }

        // ---------- 测试 3：匹配链一致 ----------

        public class CaseSource { public string UserName { get; set; } = "u"; }
        public class CaseTarget { public string username { get; set; } = ""; }

        public class FieldSource { public string RealName { get; set; } = "rn"; }
        [MapFrom(typeof(FieldSource))] // 补锚——[MapProperty] 消费以有锚为前提
        public class FieldTarget
        {
            [MapProperty(typeof(FieldSource), "RealName")]
            public string DisplayName { get; set; } = "";
        }

        public class MissingSource { public string Present { get; set; } = ""; }
        public class MissingTarget { public string Present { get; set; } = ""; public string Absent { get; set; } = ""; }

        /// <summary>大小写不敏感匹配：校验与映射侧 PropertyMatcher 匹配链一致（OrdinalIgnoreCase 默认路径）。</summary>
        [Fact]
        public void Validator_MatchChain_Consistent_CaseInsensitive()
        {
            var result = MapValidator.ValidateMapping(typeof(CaseSource), typeof(CaseTarget));
            Assert.Equal(MapValidationLevel.OK, result.Level);

            // 与映射侧同链：GetSourceItemByTargetItem 按 OrdinalIgnoreCase 命中
            var targetProp = typeof(CaseTarget).GetProperty("username")!;
            var matched = PropertyMatcher.GetSourceItemByTargetItem(
                typeof(CaseSource), targetProp,
                PropertyMatcher.GetPropertiesDictIgnoreCase(typeof(CaseSource))!.Values
                    .Where(p => p.GetIndexParameters().Length == 0).ToList(),
                sourceIsDefine: false, targetIsDefine: false, out _);
            Assert.NotNull(matched);
            Assert.Equal("UserName", matched!.Name);
        }

        /// <summary>[MapProperty(FromSource=当前源, FromField)] 消费：校验按 FromField 匹配（与映射侧同源，评审 M1）。</summary>
        [Fact]
        public void Validator_MatchChain_Consumes_FromField()
        {
            var result = MapValidator.ValidateMapping(typeof(FieldSource), typeof(FieldTarget));
            Assert.Equal(MapValidationLevel.OK, result.Level);
            Assert.Equal(1, result.MappedCount);
        }

        /// <summary>缺失属性：校验进 unmapped ⇔ PropertyMatcher 匹配链返回 null（两者一致）。</summary>
        [Fact]
        public void Validator_Unmapped_Matches_PropertyMatcher_Null()
        {
            var result = MapValidator.ValidateMapping(typeof(MissingSource), typeof(MissingTarget));
            // UnmappedProperties 元素为完整消息（"Absent (源字段 Absent 不存在)"），前缀匹配
            Assert.Contains(result.UnmappedProperties, p => p.StartsWith("Absent"));
            Assert.DoesNotContain(result.UnmappedProperties, p => p.StartsWith("Present"));

            var targetProp = typeof(MissingTarget).GetProperty("Absent")!;
            var matched = PropertyMatcher.GetSourceItemByTargetItem(
                typeof(MissingSource), targetProp,
                PropertyMatcher.GetPropertiesDictIgnoreCase(typeof(MissingSource))!.Values
                    .Where(p => p.GetIndexParameters().Length == 0).ToList(),
                sourceIsDefine: false, targetIsDefine: false, out _);
            Assert.Null(matched);
        }
    }
}
