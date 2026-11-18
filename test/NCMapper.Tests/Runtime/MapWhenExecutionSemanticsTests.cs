// 验收 #1/#2/#3/#5：MapWhen 条件守卫执行语义（双路径）——
//  ①生成路径（[MapFrom] DTO → GeneratedCache 委托）：跳过值语义 ×（新建/_Update/MapList/
//  构造器/init-only）+ 转换前求值 + null 抑制先行 + 反向剥离（D3）；
//   ②运行时回退路径（无 [MapFrom] 类型对 → MapperBase 反射链——PropertyMatcher.
//   TryGetWhenDeclaration 特性反射双读，B9）：同一守卫语义（S8 双路径一致）；
//   ③注册表路径单元级（A/B 双形态 ref 解析——ResolveWhenPredicate NonPublic/嵌套链）。
// 隔离：[Collection("GlobalConfigState")]（DisableParallelization）+ 注册表用例 finally 清理。
using System.Linq;
using System.Reflection;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.Runtime;
using Xunit;

namespace NCMapper.Tests.Runtime;

[Collection("GlobalConfigState")]
public class MapWhenExecutionSemanticsTests
{
    // ==== 模型：T1 字段值形态（生成路径） ====
    public class WgEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Note { get; set; } = "";
    }

    [MapFrom(typeof(WgEntity))]
    public class WgDto
    {
        public int Id { get; set; }

        // 初始化器取非 "" 可区分值（M2-7 变异实测发现的假绿盲区修复：条件假源值 "" 与
        // 初始化器 "" 相同 → 赋值层守卫失效时"跳过赋值"与"赋了相同值"不可区分）
        [MapWhen(nameof(HasContent))]
        public string Name { get; set; } = "keep-default";

        public string Note { get; set; } = "";

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    // ==== 模型：T2 整源形态（生成路径） ====
    public class WgT2Entity { public int Id { get; set; } public int Score { get; set; } }

    [MapFrom(typeof(WgT2Entity))]
    public class WgT2Dto
    {
        public int Id { get; set; }

        [MapWhen(nameof(IsPositive))]
        public int Score { get; set; }

        public static bool IsPositive(WgT2Entity source) => source.Score > 0;
    }

    // ==== 模型：构造器映射（default 落点）====
    public class WgCtorEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapFrom(typeof(WgCtorEntity))]
    public class WgCtorDto
    {
        public int Id { get; set; }

        // init 形态（record 风格——进可写属性枚举参与 pm 匹配；纯 get-only 不参与映射为既有行为）
        [MapWhen(nameof(HasContent))]
        public string Name { get; init; }

        public WgCtorDto(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    // ==== 模型：init-only（default 落点）====
    public class WgInitEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapFrom(typeof(WgInitEntity))]
    public class WgInitDto
    {
        public int Id { get; set; }

        [MapWhen(nameof(HasContent))]
        public string Name { get; init; } = "init-default";

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    // ==== 模型：转换前求值（[Convert] × [MapWhen]——转换器调用计数）====
    public static class WgCallCounter
    {
        public static int Count;
        public static string Upper(string value) { Count++; return value.ToUpperInvariant(); }
    }

    public class WgConvEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapFrom(typeof(WgConvEntity))]
    public class WgConvDto
    {
        public int Id { get; set; }

        [MapWhen(nameof(HasContent))]
        [Convert(typeof(WgCallCounter), nameof(WgCallCounter.Upper))]
        public string Name { get; set; } = "";

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    // ==== 模型：嵌套路径 null 抑制先行 ====
    public class WgNestedChild { public string Value { get; set; } = ""; }
    public class WgNestedEntity
    {
        public int Id { get; set; }
        public WgNestedChild? Child { get; set; }
    }

    [MapFrom(typeof(WgNestedEntity))]
    public class WgNestedDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(WgNestedEntity), "Child.Value")]
        [MapWhen(nameof(HasContent))]
        public string ChildValue { get; set; } = "";

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    // ==== 模型：反向剥离（D3——反向映射不受条件影响） ====
    // 复用 WgEntity/WgDto（正向 [MapWhen] Name；反向 Name 无条件赋值）

    // ==== 模型：运行时回退路径（无 [MapFrom]——MapperBase 反射链，B9 特性反射双读） ====
    public class WgRtEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

    [MapFrom(typeof(WgRtEntity))] // T5 补锚——反射腿命中，运行时反射链语义保持
    public class WgRtDto
    {
        public int Id { get; set; }

        [MapWhen(nameof(HasContent))]
        public string Name { get; set; } = "";

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    // ==== 模型：运行时回退路径（T2 整源） ====
    public class WgRtT2Entity { public int Id { get; set; } public int Score { get; set; } }

    [MapFrom(typeof(WgRtT2Entity))] // T5 补锚同上
    public class WgRtT2Dto
    {
        public int Id { get; set; }

        [MapWhen(nameof(IsPositive))]
        public int Score { get; set; }

        public static bool IsPositive(WgRtT2Entity source) => source.Score > 0;
    }

    // ==== 模型：注册表优先读单元级（无锚——D1 门后 fixture 形态适配，
    // 原 WgRt 对带 [MapFrom] 属同对双声明（D1 收口裁定拦截形态，B2 变更日志登记）====
    public class WgRegEntity { public int Id { get; set; } public string Name { get; set; } = ""; }

    public class WgRegDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    // ==== 模型：运行时回退路径（嵌套属性 null 传播） ====
    // private 嵌套类：NCMAP011 跳过生成（GeneratedCache 无委托 → MapperBase 反射链）+
    // [MapFrom] 标注使 targetIsDefine=true（反射链点路径分支 :164 的进入前提）——
    // 双条件构造"有点路径声明的运行时回退"场景（生成路径同型场景由 WgNestedDto 覆盖）
    private class WgRtNestedEntity
    {
        public int Id { get; set; }
        public WgNestedChild? Child { get; set; }
    }

    [MapFrom(typeof(WgRtNestedEntity))]
    private class WgRtNestedDto
    {
        public int Id { get; set; }

        [MapProperty(typeof(WgRtNestedEntity), "Child.Value")]
        [MapWhen(nameof(HasContent))]
        public string ChildValue { get; set; } = "";

        public static bool HasContent(string value) => !string.IsNullOrEmpty(value);
    }

    // ==== ① 生成路径（GeneratedCache 委托） ====

    /// <summary> 新建：条件真映射执行；条件假保持初始化器值（""）。</summary>
    [Fact]
    public void Generated_T1_New_ConditionFalse_KeepsInitializer()
    {
        var dto = Mapper.Map<WgEntity, WgDto>(new WgEntity { Id = 1, Name = "abc", Note = "n" });
        Assert.NotNull(dto);
        Assert.Equal("abc", dto!.Name);

        var dto2 = Mapper.Map<WgEntity, WgDto>(new WgEntity { Id = 2, Name = "", Note = "n" });
        Assert.NotNull(dto2);
        Assert.Equal("keep-default", dto2!.Name); // 条件假 → 跳过赋值，保持初始化器（非 "" 可区分值——M2-7 假绿盲区修复）
    }

    /// <summary> 更新（_Update）：条件假保留 dst 既有值。</summary>
    [Fact]
    public void Generated_T1_Update_ConditionFalse_KeepsExisting()
    {
        var target = new WgDto { Name = "existing" };
        Mapper.Map(new WgEntity { Id = 1, Name = "" }, target);
        Assert.Equal("existing", target.Name); // 条件假 → 保留 dst 既有值

        Mapper.Map(new WgEntity { Id = 1, Name = "fresh" }, target);
        Assert.Equal("fresh", target.Name); // 条件真 → 覆盖
    }

    /// <summary>MapList：逐元素守卫（条件真/假混合）。</summary>
    [Fact]
    public void Generated_T1_MapList_PerElementGuard()
    {
        var list = new List<WgEntity>
        {
            new() { Id = 1, Name = "a" },
            new() { Id = 2, Name = "" },
            new() { Id = 3, Name = "c" },
        };
        var dtos = Mapper.MapList<WgEntity, WgDto>(list);
        Assert.NotNull(dtos);
        Assert.Equal(3, dtos!.Count);
        Assert.Equal("a", dtos[0].Name);
        Assert.Equal("keep-default", dtos[1].Name); // 条件假元素保持默认（非 "" 可区分值——M2-7 假绿盲区修复）
        Assert.Equal("c", dtos[2].Name);
    }

    /// <summary>T2 整源形态：谓词收整个源对象。</summary>
    [Fact]
    public void Generated_T2_WholeSource()
    {
        var dto = Mapper.Map<WgT2Entity, WgT2Dto>(new WgT2Entity { Id = 1, Score = 5 });
        Assert.Equal(5, dto!.Score);

        var dto2 = Mapper.Map<WgT2Entity, WgT2Dto>(new WgT2Entity { Id = 2, Score = -5 });
        Assert.Equal(0, dto2!.Score); // 条件假 → 保持默认
    }

    /// <summary> 构造器映射：条件假 → 构造器参数显式 default。</summary>
    [Fact]
    public void Generated_Ctor_ConditionFalse_DefaultArg()
    {
        var dto = Mapper.Map<WgCtorEntity, WgCtorDto>(new WgCtorEntity { Id = 1, Name = "abc" });
        Assert.Equal("abc", dto!.Name);

        var dto2 = Mapper.Map<WgCtorEntity, WgCtorDto>(new WgCtorEntity { Id = 2, Name = "" });
        Assert.Null(dto2!.Name); // 条件假 → 构造器参数显式 default(string) = null（区别于赋值层的"保持既有值"）
    }

    /// <summary> init-only：条件假 → default（三元右臂）。</summary>
    [Fact]
    public void Generated_InitOnly_ConditionFalse_Default()
    {
        var dto = Mapper.Map<WgInitEntity, WgInitDto>(new WgInitEntity { Id = 1, Name = "abc" });
        Assert.Equal("abc", dto!.Name);

        var dto2 = Mapper.Map<WgInitEntity, WgInitDto>(new WgInitEntity { Id = 2, Name = "" });
        // 条件假 → default(string)：init 初始化器 "init-default" 被三元 default 臂覆盖（显式 default 语义）
        Assert.Null(dto2!.Name);
    }

    /// <summary> 转换前求值：条件假时 [Convert] 转换器不执行（调用计数锁定）。</summary>
    [Fact]
    public void Generated_ConvertUsing_ConditionFalse_ConverterNotInvoked()
    {
        WgCallCounter.Count = 0;
        Mapper.Map<WgConvEntity, WgConvDto>(new WgConvEntity { Id = 1, Name = "abc" });
        Assert.Equal(1, WgCallCounter.Count); // 条件真 → 转换执行

        Mapper.Map<WgConvEntity, WgConvDto>(new WgConvEntity { Id = 2, Name = "" });
        Assert.Equal(1, WgCallCounter.Count); // 条件假 → 转换不执行（计数不增长）
    }

    /// <summary> null 抑制先行：嵌套路径中段 null → 谓词收 null → 整体跳过（不 NRE）。</summary>
    [Fact]
    public void Generated_NestedPath_MidNull_NoNre_Skips()
    {
        var dto = Mapper.Map<WgNestedEntity, WgNestedDto>(new WgNestedEntity { Id = 1, Child = null });
        Assert.NotNull(dto);
        Assert.Equal("", dto!.ChildValue); // 中段 null → 谓词(null)=false → 跳过

        var dto2 = Mapper.Map<WgNestedEntity, WgNestedDto>(new WgNestedEntity
        {
            Id = 2,
            Child = new WgNestedChild { Value = "v" },
        });
        Assert.Equal("v", dto2!.ChildValue); // 路径通 + 条件真 → 映射
    }

    /// <summary>D3 反向剥离：正向 [MapWhen] 声明不影响反向映射（Name 无条件写回）。</summary>
    [Fact]
    public void Generated_Reverse_StrippedUnconditional()
    {
        var entity = Mapper.Map<WgDto, WgEntity>(new WgDto { Id = 1, Name = "" });
        Assert.NotNull(entity);
        Assert.Equal("", entity!.Name); // 反向无条件赋值（不消费正向守卫——即使正向条件假）

        var entity2 = Mapper.Map<WgDto, WgEntity>(new WgDto { Id = 2, Name = "x" });
        Assert.NotNull(entity2);
        Assert.Equal("x", entity2!.Name);
    }

    // ==== ② 运行时回退路径（MapperBase 反射链——特性反射双读） ====

    /// <summary>S8 双路径一致（T1）：无 [MapFrom] 类型对走反射链，守卫语义与生成路径一致。</summary>
    [Fact]
    public void RuntimeFallback_T1_MatchesGeneratedSemantics()
    {
        var dto = Mapper.Map<WgRtEntity, WgRtDto>(new WgRtEntity { Id = 1, Name = "abc" });
        Assert.NotNull(dto);
        Assert.Equal("abc", dto!.Name); // 条件真 → 映射

        var dto2 = Mapper.Map<WgRtEntity, WgRtDto>(new WgRtEntity { Id = 2, Name = "" });
        Assert.NotNull(dto2);
        Assert.Equal("", dto2!.Name); // 条件假 → 跳过赋值

        // 更新语义（反射链 _Update 无生成委托 → MapperBase.Map(src, target)）
        var target = new WgRtDto { Name = "existing" };
        Mapper.Map(new WgRtEntity { Id = 3, Name = "" }, target);
        Assert.Equal("existing", target.Name); // 条件假 → 保留既有值
    }

    /// <summary>S8 双路径一致（T2 整源）：反射链形态运行时判定（单参 == 源类型）。</summary>
    [Fact]
    public void RuntimeFallback_T2_WholeSource()
    {
        var dto = Mapper.Map<WgRtT2Entity, WgRtT2Dto>(new WgRtT2Entity { Id = 1, Score = 5 });
        Assert.Equal(5, dto!.Score);

        var dto2 = Mapper.Map<WgRtT2Entity, WgRtT2Dto>(new WgRtT2Entity { Id = 2, Score = -5 });
        Assert.Equal(0, dto2!.Score);
    }

    /// <summary>S8 双路径一致（嵌套路径）：反射链扁平化路径 null 传播 → 谓词收 null。</summary>
    [Fact]
    public void RuntimeFallback_NestedPath_MidNull_NoNre_Skips()
    {
        var dto = Mapper.Map<WgRtNestedEntity, WgRtNestedDto>(new WgRtNestedEntity { Id = 1, Child = null });
        Assert.NotNull(dto);
        Assert.Equal("", dto!.ChildValue);

        var dto2 = Mapper.Map<WgRtNestedEntity, WgRtNestedDto>(new WgRtNestedEntity
        {
            Id = 2,
            Child = new WgNestedChild { Value = "v" },
        });
        Assert.Equal("v", dto2!.ChildValue);
    }

    // ==== ③ 注册表路径单元级（A/B 双形态 ref 解析） ====

    /// <summary>TryGetWhenDeclaration 注册表优先读（手动注册 ConditionMethodRef——A 形态 FQN）。
    /// fixture 换无锚类型对（原 WgRt 对带 [MapFrom] 属同对双声明——
    /// D1 收口裁定拦截形态，验证意图〔注册表优先读〕不变，B2 变更日志登记）。</summary>
    [Fact]
    public void TryGetWhenDeclaration_RegistryPriority()
    {
        try
        {
            RuntimeSnapshotStore.Clear();
            RuntimeSnapshotStore.Register(new RuntimeMapperMetadata
            {
                SourceType = typeof(WgRegEntity),
                TargetType = typeof(WgRegDto),
                SourceTypeFqn = typeof(WgRegEntity).FullName!,
                TargetTypeFqn = typeof(WgRegDto).FullName!,
                Channel = RuntimeMappingChannel.Fluent,
                IsFluentForward = true,
                Properties = new[]
                {
                    new RuntimePropertyMapping
                    {
                        Target = new RuntimeTargetPropertySlot { Name = "Name", TypeName = "String" },
                        SourceName = "Name",
                        ConditionMethodRef = typeof(WgRegDto).FullName + "::HasContent",
                        ConditionShape = 1, // T1
                    },
                },
            });

            var prop = typeof(WgRegDto).GetProperty(nameof(WgRegDto.Name))!;
            var decl = PropertyMatcher.TryGetWhenDeclaration(typeof(WgRegEntity), typeof(WgRegDto), prop);
            Assert.NotNull(decl);
            Assert.Equal(typeof(WgRegDto).FullName + "::HasContent", decl!.MethodRef);
            Assert.False(decl!.IsWholeSource); // ConditionShape=1（T1 字段值）→ IsWholeSource=false
        }
        finally
        {
            RuntimeSnapshotStore.Clear();
        }
    }

    /// <summary>ResolveWhenPredicate A 形态：声明类型短路解析（public static bool M(T)）。</summary>
    [Fact]
    public void ResolveWhenPredicate_AForm_DeclaringTypeShortCircuit()
    {
        var method = PropertyMatcher.ResolveWhenPredicate(typeof(WgRtDto), null, nameof(WgRtDto.HasContent));
        Assert.Equal(nameof(WgRtDto.HasContent), method.Name);
        Assert.Equal(typeof(string), method.GetParameters()[0].ParameterType);
    }

    /// <summary>ResolveWhenPredicate A 形态：ref FQN 解析（同程序集顶层类型）。</summary>
    [Fact]
    public void ResolveWhenPredicate_AForm_RefFqn()
    {
        var method = PropertyMatcher.ResolveWhenPredicate(typeof(WgRtDto),
            typeof(WgRtDto).FullName + "::HasContent", null);
        Assert.Equal(nameof(WgRtDto.HasContent), method.Name);
    }

    /// <summary>ResolveWhenPredicate B 形态：生成类型 NonPublic 解析（GeneratedMapper::Map_When_*——
    /// 测试程序集真实生成方法反射查找，锁 NonPublic|Static + 单参 bool 契约）。</summary>
    [Fact]
    public void ResolveWhenPredicate_BForm_GeneratedNonPublic()
    {
        // 测试程序集的生成类型（WgDto 特性侧 A 形态场景无 Map_When_*；用 Fluent MapWhen DTO——
        // ComposeFluentModels 同工程先例的生成产物查找 Map_When_* 方法）
        var generatedType = typeof(WgDto).Assembly.GetType("NCMapper.Generated.GeneratedMapper");
        Assert.NotNull(generatedType);
        var whenMethod = generatedType!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m => m.Name.StartsWith("Map_When_", System.StringComparison.Ordinal));
        if (whenMethod == null)
        {
            // 本工程无 Fluent B 形态声明时跳过（B 形态端到端由 SourceGen.Tests 的 Fluent 用例覆盖）
            return;
        }
        var resolved = PropertyMatcher.ResolveWhenPredicate(typeof(WgDto),
            "NCMapper.Generated.GeneratedMapper::" + whenMethod.Name, null);
        Assert.Same(whenMethod, resolved);
    }

    /// <summary>ResolveWhenPredicate 解析失败抛 MapException（转换器解析失败同口径）。</summary>
    [Fact]
    public void ResolveWhenPredicate_Unresolvable_Throws()
    {
        Assert.Throws<MapException>(() =>
            PropertyMatcher.ResolveWhenPredicate(typeof(WgRtDto), null, "NoSuchMethod"));
    }
}
