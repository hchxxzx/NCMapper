using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NCMapper.SourceGen;
using NCMapper.SourceGen.Tests.Helpers;
using Xunit;

namespace NCMapper.SourceGen.Tests;

/// <summary>
/// A2 测试——生成期嵌套互调环防护
/// <para>端到端不可达事实（登记）：异型嵌套对象属性（实体→DTO 类型）在匹配层被 NCMAP002 拦截，
/// GenerateNestedClassMapping 注册对委托分支当前不可达——A2 检测器为纵深防御（未来匹配层放行时生效）。
/// 故以白盒单元测试直接验证检测器逻辑（环检测 + 环上全部降级 + NCMAP029 诊断），
/// 端到端验证"无环场景零干扰 + 自引用深度保护保留"（验收 5，B1）。</para>
/// </summary>
public class NestedCycleDegradationTests
{
    /// <summary>
    /// 构造最小 TypeRef（EqualityKey=FullName——IsSameType 判定基准；MapFromSources=[MapFrom] 物化）
    /// </summary>
    /// <param name="fullName">类型全名</param>
    /// <param name="mapFromSources">[MapFrom] 源类型列表（DTO 注册对判定用）</param>
    /// <returns>TypeRef</returns>
    private static TypeRef MakeType(string fullName, params TypeRef[] mapFromSources)
        => new()
        {
            FullName = fullName,
            EqualityKey = fullName,
            SimpleName = fullName[(fullName.LastIndexOf('.') + 1)..],
            Kind = TypeKind.Class,
            MapFromSources = mapFromSources,
        };

    /// <summary>
    /// 构造属性映射（源/目标同名属性）
    /// </summary>
    /// <param name="name">属性名</param>
    /// <param name="src">源类型</param>
    /// <param name="tgt">目标类型</param>
    /// <returns>PropertyMappingSnapshot</returns>
    private static PropertyMappingSnapshot MakePm(string name, TypeRef src, TypeRef tgt)
        => new()
        {
            Source = new PropertyRef { Name = name, Type = src },
            Target = new PropertyRef { Name = name, Type = tgt },
        };

    /// <summary>
    /// 白盒：互调环（A→ADto 与 B→BDto 经嵌套属性互调）——环上全部边降级 + NCMAP029 诊断
    /// <para>S1：环上全部注册对降级（确定性，同输入同输出）；验证 IsDegraded 双侧命中。</para>
    /// </summary>
    [Fact]
    public void Detect_MutualCycle_MarksAllCycleEdgesDegraded()
    {
        var user = MakeType("App.User");
        var userDto = MakeType("App.UserDto", user);
        var addr = MakeType("App.Address");
        var addrDto = MakeType("App.AddressDto", addr);

        var mUser = new MappingSnapshot
        {
            SourceType = user,
            TargetType = userDto,
            PropertyMappings = new[] { MakePm("Address", addr, addrDto) },
        };
        var mAddr = new MappingSnapshot
        {
            SourceType = addr,
            TargetType = addrDto,
            PropertyMappings = new[] { MakePm("Owner", user, userDto) },
        };

        var ctx = new GenerationContext(CancellationToken.None);
        // W3_1，环检测决策前移 GenerationPreparation.PlanCycleDetection
        // （原 NestedCycleDetector.Prepare 白盒入口等价替换——typeNameMap 参数本就未用）
        GenerationPreparation.PlanCycleDetection(
            ImmutableArray.Create<MappingSnapshot?>(mUser, mAddr),
            ctx);

        // 环上全部边降级（S1：同输入同输出，快照守卫稳定）
        Assert.True(NestedCycleDetector.IsDegraded(addr, addrDto), "Address→AddressDto 应被降级（环边）");
        Assert.True(NestedCycleDetector.IsDegraded(user, userDto), "User→UserDto 应被降级（环边）");

        // 非环边不降级（防御：不误伤无关注册对）
        var other = MakeType("App.Other");
        var otherDto = MakeType("App.OtherDto");
        Assert.False(NestedCycleDetector.IsDegraded(other, otherDto));

        // NCMAP029 诊断输出
        Assert.Contains(ctx.Diagnostics.GetAppended(mUser), d => d.DescriptorId == "NCMAP029");
    }

    /// <summary>
    /// 白盒：无环图（单向依赖）——零降级 + 无 NCMAP029
    /// </summary>
    [Fact]
    public void Detect_NoCycle_NoDegradation()
    {
        var order = MakeType("App.Order");
        var orderDto = MakeType("App.OrderDto");
        var line = MakeType("App.OrderLine");
        var lineDto = MakeType("App.OrderLineDto");

        // 单向：Order→OrderDto 委托 Line→LineDto；Line→LineDto 无反向嵌套
        var mOrder = new MappingSnapshot
        {
            SourceType = order,
            TargetType = orderDto,
            PropertyMappings = new[] { MakePm("Line", line, lineDto) },
        };
        var mLine = new MappingSnapshot
        {
            SourceType = line,
            TargetType = lineDto,
            PropertyMappings = Array.Empty<PropertyMappingSnapshot>(),
        };

        var ctx = new GenerationContext(CancellationToken.None);
        // W3_1，环检测决策前移 GenerationPreparation.PlanCycleDetection（白盒入口等价替换）
        GenerationPreparation.PlanCycleDetection(
            ImmutableArray.Create<MappingSnapshot?>(mOrder, mLine),
            ctx);

        Assert.False(NestedCycleDetector.IsDegraded(line, lineDto), "单向依赖不应降级");
        Assert.False(NestedCycleDetector.IsDegraded(order, orderDto), "单向依赖不应降级");
        Assert.Empty(ctx.Diagnostics.GetAppended(mOrder));
        Assert.Empty(ctx.Diagnostics.GetAppended(mLine));
    }

    /// <summary>
    /// 端到端（B1）：树形自引用（Category.Children）维持深度保护（不降级、无 NCMAP029）——深度保护形态不变
    /// </summary>
    [Fact]
    public void SelfRecursive_KeepsDepthProtection()
    {
        var source = """
            using System.Collections.Generic;
            using NCMapper.Attributes;
            public class Category
            {
                public int Id { get; set; }
                public List<Category> Children { get; set; } = new();
            }
            [MapFrom(typeof(Category))]
            public class CategoryDto
            {
                public int Id { get; set; }
                public List<CategoryDto> Children { get; set; } = new();
            }
            """;

        var result = TestHelper.GenerateMapper(source);

        // 深度保护保留（P0：ThreadStatic 深度字段 + 超限抛 MapException）
        var body = result.GetMethod("Map_Category_To_CategoryDto")?.Body ?? "";
        Assert.Contains("MaxRecursionDepth", body);
        // 不降级：无 NCMAP029
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "NCMAP029");
    }

    /// <summary>
    /// B4-01 回归（批次一）：互调环发射点诊断经 FromId 反查表命中 NCMAP029——
    /// BuildById 反查表漏登记 NestedCycleDegradedRule.Id 时 FromId 返 null，生成器发射回落
    /// NCMAP099（Warning 级"生成失败"语义；其消息模板 3 占位符 vs 发射点 2 实参 → FormatException）。
    /// 本用例锚定"发射点提交 Id ↔ 反查表登记"链路完整（端到端发射路径当前不可达——类头登记
    /// 纵深防御，此白盒形态贴住发射点 GenerationPreparation.DegradeCycle 真实提交物）。
    /// </summary>
    [Fact]
    public void MutualCycleDiagnostic_FromIdResolves_NCMAP029_InfoSeverity()
    {
        // 互调环 harness（复用 Detect_MutualCycle 场景形态，独立类型键）
        var user = MakeType("App.B401User");
        var userDto = MakeType("App.B401UserDto", user);
        var addr = MakeType("App.B401Address");
        var addrDto = MakeType("App.B401AddressDto", addr);

        var mUser = new MappingSnapshot
        {
            SourceType = user,
            TargetType = userDto,
            PropertyMappings = new[] { MakePm("Address", addr, addrDto) },
        };
        var mAddr = new MappingSnapshot
        {
            SourceType = addr,
            TargetType = addrDto,
            PropertyMappings = new[] { MakePm("Owner", user, userDto) },
        };

        var ctx = new GenerationContext(CancellationToken.None);
        GenerationPreparation.PlanCycleDetection(
            ImmutableArray.Create<MappingSnapshot?>(mUser, mAddr),
            ctx);

        // 发射点真实提交 NCMAP029 快照（DescriptorId + 2 实参，GenerationPreparation.DegradeCycle）
        var snapshot = ctx.Diagnostics.GetAppended(mUser).Single(d => d.DescriptorId == "NCMAP029");
        // 反查表命中（B4-01 缺陷形态：BuildById 漏登记 → FromId 返 null → 以下断言红）
        var descriptor = DiagnosticDescriptors.FromId(snapshot.DescriptorId);
        Assert.NotNull(descriptor);
        Assert.Equal("NCMAP029", descriptor!.Id);
        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
        // 镜像生产发射（NCMapperSourceGenerator.EmitDiagnostics "FromId ?? GenerationFailedRule"
        // 同构）：2 实参按 NCMAP029 模板（2 占位符）格式化成功——NCMAP099 回落路径 3 占位符
        // vs 2 实参将抛 FormatException，本断言锁定不回落
        var message = Diagnostic.Create(descriptor, Location.None, snapshot.Args.ToArray()).GetMessage();
        Assert.NotEmpty(message);
    }

    /// <summary>
    /// B4-01 护栏（否定面，不参与红/绿判别——首评 P3-7）：非互调环样例零 NCMAP029——
    /// 与既有端到端基线 GenerationPreparationTests（物化期已降级场景检测器不触发）同口径，
    /// 防新断言恒真。
    /// </summary>
    [Fact]
    public void NoCycle_ZeroNCMAP029_FromIdGuard()
    {
        // 单向依赖（同 Detect_NoCycle_NoDegradation 形态，独立类型键）
        var order = MakeType("App.B401Order");
        var orderDto = MakeType("App.B401OrderDto");
        var line = MakeType("App.B401Line");
        var lineDto = MakeType("App.B401LineDto");

        var mOrder = new MappingSnapshot
        {
            SourceType = order,
            TargetType = orderDto,
            PropertyMappings = new[] { MakePm("Line", line, lineDto) },
        };
        var mLine = new MappingSnapshot
        {
            SourceType = line,
            TargetType = lineDto,
            PropertyMappings = Array.Empty<PropertyMappingSnapshot>(),
        };

        var ctx = new GenerationContext(CancellationToken.None);
        GenerationPreparation.PlanCycleDetection(
            ImmutableArray.Create<MappingSnapshot?>(mOrder, mLine),
            ctx);

        Assert.DoesNotContain(ctx.Diagnostics.GetAppended(mOrder), d => d.DescriptorId == "NCMAP029");
        Assert.DoesNotContain(ctx.Diagnostics.GetAppended(mLine), d => d.DescriptorId == "NCMAP029");
    }
}
