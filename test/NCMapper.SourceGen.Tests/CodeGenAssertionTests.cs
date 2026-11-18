using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// 生成代码文本断言基线测试 — 验证 6 条生成器路径的输出代码正确性
/// <para>每条路径通过 raw string literal 精确匹配或关键片段匹配生成的方法体</para>
/// </summary>
public class CodeGenAssertionTests
{
    /// <summary>
    /// 核心映射：两个属性的简单类型应生成 null 守卫 + 对象初始化器 + return
    /// </summary>
    [Fact]
    public void CoreMap_TwoProperties_MatchesExpectedBody()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } public string Name { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBody("Map_CgEntity_To_CgDto", """
            if (src is null) return null;
            var dst = new CgDto
            {
                Id = src.Id,
                Name = src.Name
            };
            return dst;
            """);
    }

    /// <summary>
    /// 核心映射：异名字段（[MapProperty] 标注）应正确生成属性名映射
    /// </summary>
    [Fact]
    public void CoreMap_WithMapPropertyRename_MatchesExpectedBody()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgSrc { public int Key { get; set; } }
            [MapFrom(typeof(CgSrc))]
            public class CgDst
            {
                [MapProperty(typeof(CgSrc), nameof(CgSrc.Key))]
                public int Value { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBody("Map_CgSrc_To_CgDst", """
            if (src is null) return null;
            var dst = new CgDst
            {
                Value = src.Key
            };
            return dst;
            """);
    }

    /// <summary>
    /// 核心映射：[MapIgnore] 标注的属性不出现在生成的方法体中
    /// </summary>
    [Fact]
    public void CoreMap_WithMapIgnore_SkipsIgnoredProperty()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgIgnEntity { public int Id { get; set; } public string Name { get; set; } public string Computed { get; set; } }
            [MapFrom(typeof(CgIgnEntity))]
            public class CgIgnDto
            {
                public int Id { get; set; }
                public string Name { get; set; }
                [MapIgnore]
                public string Computed { get; set; }
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBody("Map_CgIgnEntity_To_CgIgnDto", """
            if (src is null) return null;
            var dst = new CgIgnDto
            {
                Id = src.Id,
                Name = src.Name
            };
            return dst;
            """);
    }

    /// <summary>
    /// Action 映射：应调用核心映射方法并在非 null 时执行 Action 回调
    /// </summary>
    [Fact]
    public void ActionMap_CallsCoreMapAndInvokesAction()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } public string Name { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBody("Map_CgEntity_To_CgDto_WithAction", """
            var dst = Map_CgEntity_To_CgDto(src);
            if (dst != null) action?.Invoke(src, dst);
            return dst;
            """);
    }

    /// <summary>
    /// Action 映射：方法签名应包含 Action 参数
    /// </summary>
    [Fact]
    public void ActionMap_SignatureContainsActionParameter()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethod("Map_CgEntity_To_CgDto_WithAction");
        var method = result.GetMethod("Map_CgEntity_To_CgDto_WithAction");
        Assert.Contains("Action<CgEntity, CgDto>", method!.Signature);
    }

    /// <summary>
    /// 更新映射：应生成 null 守卫 + 逐属性赋值 + Action 回调
    /// </summary>
    [Fact]
    public void UpdateMap_TwoProperties_MatchesExpectedBody()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } public string Name { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBody("Map_CgEntity_To_CgDto_Update", """
            if (src is null || dst is null) return;
            dst.Id = src.Id;
            dst.Name = src.Name;
            action?.Invoke(src, dst);
            """);
    }

    /// <summary>
    /// 更新映射：方法返回类型应为 void
    /// </summary>
    [Fact]
    public void UpdateMap_ReturnTypeIsVoid()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        var method = result.GetMethod("Map_CgEntity_To_CgDto_Update");
        Assert.StartsWith("void", method!.Signature);
    }

    /// <summary>
    /// 列表映射：应包含 null 守卫（A' 裁决后 return null! 透传）
    /// <para>签名改非空 List&lt;T&gt;，null 入参 return null!——
    /// 守卫仍在，仅返回值表达从 null 变为 null!（签名非空仅为满足 RegisterGeneratedList 委托契约）</para>
    /// </summary>
    [Fact]
    public void ListMap_HasNullGuard()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } public string Name { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        var method = result.GetMethod("MapList_CgEntity_To_CgDto");
        Assert.Contains("List<CgDto> MapList_CgEntity_To_CgDto", method!.Signature);
        result.Should().HaveMethodBodyContaining("MapList_CgEntity_To_CgDto", "if (sources is null) return null!;");
    }

    /// <summary>
    /// 列表映射：应包含数组路径（T[] 优化分支）
    /// </summary>
    [Fact]
    public void ListMap_HasArrayPath()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } public string Name { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("MapList_CgEntity_To_CgDto", """
            if (sources is CgEntity[] arr)
            """);
    }

    /// <summary>
    /// 列表映射：应包含 ICollection 路径（预分配容量优化）
    /// </summary>
    [Fact]
    public void ListMap_HasCollectionPath()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } public string Name { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        result.Should().HaveMethodBodyContaining("MapList_CgEntity_To_CgDto", """
            if (sources is System.Collections.Generic.ICollection<CgEntity> coll)
            """);
    }

    /// <summary>
    /// 深拷贝：应生成 null 守卫 + 无参构造 + 逐属性赋值 + return
    /// <para>深拷贝方法自动为所有参与 [MapFrom] 映射的类型生成，无需额外标注</para>
    /// <para>CgDto 为扁平类型 → 编译期分类 Acyclic，
    /// Acyclic 变体省略 RegisterDeepCopyTarget 登记行（层 1 免检测分支不消费 CycleCache）</para>
    /// <para>签名改非空 + return null!（DeepCopy 同构 A' 修复）</para>
    /// </summary>
    [Fact]
    public void DeepCopy_TwoProperties_MatchesExpectedBody()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } public string Name { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } public string Name { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 深拷贝自动为 CgDto 生成 DeepCopy_CgDto 方法
        // Acyclic 变体：无 RegisterDeepCopyTarget 登记（方案 F，零检测开销）
        result.Should().HaveMethodBody("DeepCopy_CgDto", """
            // 签名非空仅为满足 Mapper.RegisterGeneratedDeepCopy 委托契约（Func<T, DeepCopyContext, T>），null 透传与运行时 DeepCopy 入口语义一致
            if (src is null) return null!;
            var dst = new CgDto();
            dst.Id = src.Id;
            dst.Name = src.Name;
            return dst;
            """);
    }

    /// <summary>
    /// 深拷贝：方法应包含 null 守卫（A' 裁决后 return null! 透传）
    /// <para>签名改非空 List&lt;T&gt;（DeepCopy 同构），
    /// null 入参 return null! —— 守卫仍在，仅返回值表达从 null 变为 null!</para>
    /// </summary>
    [Fact]
    public void DeepCopy_HasNullGuard()
    {
        var source = """
            using NCMapper.Attributes;
            public class CgEntity { public int Id { get; set; } }
            [MapFrom(typeof(CgEntity))]
            public class CgDto { public int Id { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 深拷贝自动为 CgEntity 生成 DeepCopy_CgEntity 方法
        result.Should().HaveMethodBodyContaining("DeepCopy_CgEntity", "if (src is null) return null!;");
    }

    /// <summary>
    /// 深拷贝：字典属性应生成 new Dictionary + 逐 KeyValuePair 深拷贝
    /// <para>P1-5 字典分支镜像运行时 CollectionCopier.CopyDictionary 语义：
    /// key/value 简单类型原样复用、引用类型递归 DeepCopy。
    /// 守护生成代码形态，防止字典属性落入"其他单值集合"分支生成非法构造调用
    /// （Dictionary 无 IEnumerable&lt;KeyValuePair&gt; 构造器）。</para>
    /// </summary>
    [Fact]
    public void DeepCopy_DictionaryProperty_GeneratesKeyValueDeepCopy()
    {
        var source = """
            using NCMapper.Attributes;
            using System.Collections.Generic;
            public class CgDictItem { public string Name { get; set; } }
            public class CgDictEntity { public int Id { get; set; } public Dictionary<string, CgDictItem> Items { get; set; } }
            [MapFrom(typeof(CgDictEntity))]
            public class CgDictDto { public int Id { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 字典分支：new Dictionary + 逐 KeyValuePair 深拷贝（value 引用类型递归 DeepCopy）
        result.Should().HaveMethodBodyContaining("DeepCopy_CgDictEntity",
            "var _dict = new System.Collections.Generic.Dictionary<string, CgDictItem>();");
        result.Should().HaveMethodBodyContaining("DeepCopy_CgDictEntity",
            "NCMapper.Mapper.DeepCopy(_kv.Value, ctx)!");
    }

    /// <summary>
    /// 深拷贝：简单值类型 value 的字典应原样复用（不递归 DeepCopy，镜像运行时 IsValueType 分支）
    /// </summary>
    [Fact]
    public void DeepCopy_DictionaryProperty_SimpleValueCopiesDirectly()
    {
        var source = """
            using NCMapper.Attributes;
            using System.Collections.Generic;
            public class CgDictEntity { public int Id { get; set; } public Dictionary<string, int> Scores { get; set; } }
            [MapFrom(typeof(CgDictEntity))]
            public class CgDictDto { public int Id { get; set; } }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 值类型 value → 直接赋值，无 DeepCopy 递归调用
        result.Should().HaveMethodBodyContaining("DeepCopy_CgDictEntity",
            "var _dict = new System.Collections.Generic.Dictionary<string, int>();");
        result.Should().HaveMethodBodyContaining("DeepCopy_CgDictEntity", "_dict[_kv.Key] = _kv.Value;");
    }
}
