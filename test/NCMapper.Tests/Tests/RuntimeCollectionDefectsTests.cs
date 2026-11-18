// 运行时集合缺陷修复测试矩阵（L-01/L-02）—
// 载体：属性级集合映射（实体 List<X> → DTO 集合目标），运行时侧 DTO 不标 [MapFrom]
// （Mapper.Map 走运行时表达式树路径），生成侧对照 DTO 标 [MapFrom]（走源生成器路径）。
// 用例矩阵对应，1/2/3/4/5/6/7/8/9/10/10a/10b/10c/11；
// 用例 10b/10c 为等价断言（固化修复后现状行为基线，防短路门禁误杀合法注册对）。
using NCMapper.Attributes;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    // ===================== 模型：源实体 =====================

    /// <summary>
    /// L-01 基础源 — List&lt;int&gt; 属性（用例 1/3/4/5/6/9/10/10c/11 载体）
    /// </summary>
    public class RtIntListSource
    {
        /// <summary>整数集合</summary>
        public List<int>? Items { get; set; }
    }

    /// <summary>
    /// 反向源 — List&lt;long&gt; 属性（用例 2：long→int 显式转换）
    /// </summary>
    public class RtLongListSource
    {
        /// <summary>长整型集合</summary>
        public List<long>? Items { get; set; }
    }

    /// <summary>
    /// 混合对源 — List&lt;string&gt; 属性（用例 7：string→int 混合对异常）
    /// </summary>
    public class RtStrListSource
    {
        /// <summary>字符串集合</summary>
        public List<string>? Items { get; set; }
    }

    /// <summary>
    /// Nullable 源 — List&lt;int?&gt; 属性，测试数据含 null 元素（用例 10a）
    /// </summary>
    public class RtNullableIntListSource
    {
        /// <summary>可空整数集合（含 null 元素）</summary>
        public List<int?>? Items { get; set; }
    }

    /// <summary>
    /// 接口元素对源 — List&lt;IFoo&gt; 属性（用例 10b：未注册接口元素对经 Resolve bootstrap）
    /// </summary>
    public class RtInterfaceListSource
    {
        /// <summary>接口元素集合</summary>
        public List<IFoo>? Items { get; set; }
    }

    // ===================== 模型：运行时目标 DTO（不标 [MapFrom] → 运行时路径）=====================

    /// <summary>
    /// 强转目标 — List&lt;long&gt;（用例 1：int→long 隐式可转换强转路径）
    /// </summary>
    public class RtLongListDto
    {
        /// <summary>长整型集合</summary>
        public List<long>? Items { get; set; }
    }

    /// <summary>
    /// ConvertValue 目标 — List&lt;int&gt;（用例 2：long→int 显式转换 ConvertValue 路径）
    /// </summary>
    public class RtIntListDto
    {
        /// <summary>整数集合</summary>
        public List<int>? Items { get; set; }
    }

    /// <summary>
    /// 具体容器目标 — HashSet&lt;long&gt;（用例 3：容器构造档2）
    /// </summary>
    public class RtHashSetDto
    {
        /// <summary>长整型集合（HashSet 构造）</summary>
        public HashSet<long>? Items { get; set; }
    }

    /// <summary>
    /// 具体容器目标 — Queue&lt;long&gt;（用例 4：FIFO 序断言）
    /// </summary>
    public class RtQueueDto
    {
        /// <summary>长整型队列</summary>
        public Queue<long>? Items { get; set; }
    }

    /// <summary>
    /// 具体容器目标 — Stack&lt;long&gt;（用例 4：Pop 序断言，构造参数包 Reverse）
    /// </summary>
    public class RtStackDto
    {
        /// <summary>长整型栈</summary>
        public Stack<long>? Items { get; set; }
    }

    /// <summary>
    /// 混合对目标 — List&lt;int&gt;（用例 7：源 List&lt;string&gt; → 目标元素值类型 → 双语专用异常）
    /// </summary>
    public class RtIntFromStrDto
    {
        /// <summary>整数集合</summary>
        public List<int>? Items { get; set; }
    }

    /// <summary>
    /// 无构造器集合目标（用例 8：档3 双语 NotSupportedException 载体）
    /// </summary>
    public class RtNoCtorDto
    {
        /// <summary>自定义集合（仅默认构造器，无 IEnumerable&lt;T&gt; 单参构造器）</summary>
        public NoEnumerableCtorCollection<long>? Items { get; set; }
    }

    /// <summary>
    /// 接口目标 — IList&lt;long&gt;（用例 9：档1 引用转换防回归）
    /// </summary>
    public class RtIListDto
    {
        /// <summary>长整型集合（接口目标）</summary>
        public IList<long>? Items { get; set; }
    }

    /// <summary>
    /// 接口目标 — IEnumerable&lt;long&gt;（用例 9：档1 引用转换防回归）
    /// </summary>
    public class RtIEnumerableDto
    {
        /// <summary>长整型集合（接口目标）</summary>
        public IEnumerable<long>? Items { get; set; }
    }

    /// <summary>
    /// Nullable 目标 — List&lt;int?&gt;（用例 10：Nullable 元素对）
    /// </summary>
    public class RtNullableIntDto
    {
        /// <summary>可空整数集合</summary>
        public List<int?>? Items { get; set; }
    }

    /// <summary>
    /// Nullable 目标 — List&lt;long?&gt;（用例 10a：源含 null 元素）
    /// </summary>
    public class RtNullableLongDto
    {
        /// <summary>可空长整型集合</summary>
        public List<long?>? Items { get; set; }
    }

    /// <summary>
    /// 接口元素对目标 — List&lt;IBar&gt;（用例 10b：等价断言，未注册接口对）
    /// </summary>
    public class RtInterfaceListDto
    {
        /// <summary>接口元素集合</summary>
        public List<IBar>? Items { get; set; }
    }

    /// <summary>
    /// 值类型源×class 目标容器 — List&lt;IntWrapDto&gt;（用例 10c：等价断言，合法注册对）
    /// </summary>
    public class RtIntWrapListDto
    {
        /// <summary>包装类集合</summary>
        public List<IntWrapDto>? Items { get; set; }
    }

    // ===================== 模型：生成侧对照 DTO（标 [MapFrom] → 生成器路径，用例 11）=====================

    /// <summary>
    /// 生成侧对照 — List&lt;long&gt; 目标（[MapFrom] 触发 F-04 内联转换路径）
    /// </summary>
    [MapFrom(typeof(RtIntListSource))]
    public class RtGenLongListDto
    {
        /// <summary>长整型集合</summary>
        public List<long>? Items { get; set; }
    }

    /// <summary>
    /// 生成侧对照 — HashSet&lt;long&gt; 目标（生成侧具体集合构造分支）
    /// </summary>
    [MapFrom(typeof(RtIntListSource))]
    public class RtGenHashSetDto
    {
        /// <summary>长整型集合（HashSet 构造）</summary>
        public HashSet<long>? Items { get; set; }
    }

    /// <summary>
    /// 生成侧对照 — Queue&lt;long&gt; 目标（生成侧具体集合构造分支）
    /// </summary>
    [MapFrom(typeof(RtIntListSource))]
    public class RtGenQueueDto
    {
        /// <summary>长整型队列</summary>
        public Queue<long>? Items { get; set; }
    }

    /// <summary>
    /// 生成侧对照 — Stack&lt;long&gt; 目标（生成侧 Stack 构造参数包 Reverse，对照关键项）
    /// </summary>
    [MapFrom(typeof(RtIntListSource))]
    public class RtGenStackDto
    {
        /// <summary>长整型栈</summary>
        public Stack<long>? Items { get; set; }
    }

    // ===================== 模型：辅助类型 =====================

    /// <summary>
    /// 无 IEnumerable&lt;T&gt; 单参构造器的自定义集合（用例 8 档3 载体）——
    /// 仅默认构造器 + 私有存储，引用转换与构造器档均不可达
    /// </summary>
    public class NoEnumerableCtorCollection<T> : IEnumerable<T>
    {
        /// <summary>内部存储</summary>
        private readonly List<T> _items = new();

        /// <summary>枚举器</summary>
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        /// <summary>非泛型枚举器</summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// 接口元素对源接口（用例 10b）——[MapFrom] 为 AttributeTargets.Class 限定，接口不可标注
    /// </summary>
    public interface IFoo
    {
        /// <summary>值</summary>
        int V { get; }
    }

    /// <summary>
    /// 接口元素对目标接口（用例 10b）
    /// </summary>
    public interface IBar
    {
        /// <summary>值</summary>
        int V { get; }
    }

    /// <summary>
    /// IFoo 运行时实现（用例 10b 源数据载体）
    /// </summary>
    public class FooImpl : IFoo
    {
        /// <summary>值</summary>
        public int V { get; set; }
    }

    /// <summary>
    /// 值类型源×class 目标载体（用例 10c）——MapperBase&lt;int, IntWrapDto&gt;
    /// 单约束（TTarget : class）下合法封闭泛型（TSource 无约束），不得被短路门禁误杀。
    /// <para>不标 [MapFrom(typeof(int))]：源生成器不支持值类型 TSource（执行期发现 D-1——
    /// 触发生成 MapList_Int32_To_Int32 未定义调用 / null→int / RunPostMap 约束违反等 285 项
    /// 编译错误）；本用例验证点在运行时 Resolve bootstrap 路径（RunClassConstructor(MapperBase&lt;,&gt;)），
    /// 与特性标注无关，删除标注零语义损失。</para>
    /// </summary>
    public class IntWrapDto
    {
        /// <summary>包装值</summary>
        public long V { get; set; }
    }

    /// <summary>
    /// 运行时集合缺陷修复测试 — L-01（基元异类型集合）/ L-02（具体容器目标）行为锁定
    /// </summary>
    public class RuntimeCollectionDefectsTests
    {
        // ===================== 用例 1：List<int> → List<long>（强转路径）=====================

        /// <summary>
        /// 用例 1：属性级 List&lt;int&gt; → List&lt;long&gt; 映射成功，元素值逐位一致
        /// <para>L-01 修复前：构建期 MakeGenericType 违反 MapperBase TTarget:class 约束崩溃；
        /// 修复后走隐式转换强转路径（分支 2a）。</para>
        /// </summary>
        [Fact]
        public void L01_Case01_ListIntToListLong_MapsWithImplicitCast()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var dto = Mapper.Map<RtIntListSource, RtLongListDto>(src)!;

            Assert.NotNull(dto.Items);
            Assert.Equal([1L, 2L, 3L], dto.Items);
        }

        // ===================== 用例 2：List<long> → List<int>（ConvertValue 路径）=====================

        /// <summary>
        /// 用例 2a：值域内 List&lt;long&gt; → List&lt;int&gt; 成功且值一致（long→int 显式转换走 ConvertValue）
        /// </summary>
        [Fact]
        public void L01_Case02_ListLongToListInt_InRange_MapsViaConvertValue()
        {
            var src = new RtLongListSource { Items = [1L, 2L, 3L] };

            var dto = Mapper.Map<RtLongListSource, RtIntListDto>(src)!;

            Assert.NotNull(dto.Items);
            Assert.Equal([1, 2, 3], dto.Items);
        }

        /// <summary>
        /// 用例 2b：超 int 范围值抛 MapException（写死断言，评审 001-S4）——
        /// ConvertValue 内 ChangeType 溢出被吞后统一抛 MapException（方案书 O1 实证）
        /// </summary>
        [Fact]
        public void L01_Case02_ListLongToListInt_Overflow_ThrowsMapException()
        {
            var src = new RtLongListSource { Items = [3_000_000_000L] };

            var ex = Assert.Throws<NCMapper.MapException>(
                () => Mapper.Map<RtLongListSource, RtIntListDto>(src));

            Assert.False(string.IsNullOrEmpty(ex.Message));
        }

        // ===================== 用例 3：List<int> → HashSet<long>（容器构造档2）=====================

        /// <summary>
        /// 用例 3：List&lt;int&gt; → HashSet&lt;long&gt; 成功构造，元素一致
        /// <para>L-02 修复前：Expression.Convert(List→HashSet) 构建期 InvalidOperationException；
        /// 修复后走 IEnumerable&lt;T&gt; 构造器档（档2）。</para>
        /// </summary>
        [Fact]
        public void L02_Case03_ListIntToHashSetLong_ConstructsSet()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var dto = Mapper.Map<RtIntListSource, RtHashSetDto>(src)!;

            Assert.NotNull(dto.Items);
            Assert.Equal(3, dto.Items.Count);
            Assert.True(dto.Items.SetEquals([1L, 2L, 3L]));
        }

        // ===================== 用例 4：List<int> → Queue/Stack（顺序语义）=====================

        /// <summary>
        /// 用例 4a：List&lt;int&gt; → Queue&lt;long&gt; 成功构造，Dequeue 序 = 源序（FIFO）
        /// </summary>
        [Fact]
        public void L02_Case04_ListIntToQueue_FifoOrder()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var dto = Mapper.Map<RtIntListSource, RtQueueDto>(src)!;

            Assert.NotNull(dto.Items);
            Assert.Equal(3, dto.Items.Count);
            Assert.Equal(1L, dto.Items.Dequeue());
            Assert.Equal(2L, dto.Items.Dequeue());
            Assert.Equal(3L, dto.Items.Dequeue());
        }

        /// <summary>
        /// 用例 4b：List&lt;int&gt; → Stack&lt;long&gt; 成功构造，Pop 序 = 源序
        /// <para>Stack 构造参数包 Enumerable.Reverse（Pop 语义对齐，评审 001-B3；
        /// 直接构造会反转——Push 序=源序倒序，Pop 序=源序，与生成侧 CodeGenerator.Mapping.cs:1677 同构）。</para>
        /// </summary>
        [Fact]
        public void L02_Case04_ListIntToStack_PopOrder()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var dto = Mapper.Map<RtIntListSource, RtStackDto>(src)!;

            Assert.NotNull(dto.Items);
            Assert.Equal(3, dto.Items.Count);
            Assert.Equal(1L, dto.Items.Pop());
            Assert.Equal(2L, dto.Items.Pop());
            Assert.Equal(3L, dto.Items.Pop());
        }

        // ===================== 用例 5：null 守恒 =====================

        /// <summary>
        /// 用例 5：源集合 null × 各目标容器 → 目标 null（null 守恒，testItem 守卫不变）
        /// </summary>
        [Fact]
        public void L02_Case05_SourceNull_TargetNull_Passthrough()
        {
            var src = new RtIntListSource { Items = null };

            Assert.Null(Mapper.Map<RtIntListSource, RtLongListDto>(src)!.Items);
            Assert.Null(Mapper.Map<RtIntListSource, RtHashSetDto>(src)!.Items);
            Assert.Null(Mapper.Map<RtIntListSource, RtQueueDto>(src)!.Items);
            Assert.Null(Mapper.Map<RtIntListSource, RtStackDto>(src)!.Items);
            Assert.Null(Mapper.Map<RtIntListSource, RtIListDto>(src)!.Items);
        }

        // ===================== 用例 6：Update 路径 =====================

        /// <summary>
        /// 用例 6：Update 路径与 Map 一致（共享 GetListExpression）+ F-03 null 语义（源 null → 清空目标旧值）
        /// </summary>
        [Fact]
        public void L01_Case06_UpdatePath_ConsistentWithMap()
        {
            // 非 null 源：Update 写入与 Map 一致
            var src = new RtIntListSource { Items = [1, 2, 3] };
            var dst = new RtLongListDto { Items = [9L] };
            Mapper.Map(src, dst);
            Assert.Equal([1L, 2L, 3L], dst.Items);

            // null 源：F-03 语义——赋 null 清空旧值（不保留）
            var nullSrc = new RtIntListSource { Items = null };
            Mapper.Map(nullSrc, dst);
            Assert.Null(dst.Items);
        }

        // ===================== 用例 7：混合对（string→int）专用异常 =====================

        /// <summary>
        /// 用例 7：混合对 List&lt;string&gt; → List&lt;int&gt; 抛双语 MapConfigurationException（不崩溃）
        /// <para>修复前：MakeGenericType 构建期崩溃（ArgumentException 族）；
        /// 修复后：构建期受控抛出，消息语义"目标元素为值类型、无法实例化映射委托"（分支 2c）。</para>
        /// </summary>
        [Fact]
        public void L01_Case07_MixedPair_StringToInt_ThrowsWithBilingualMessage()
        {
            var src = new RtStrListSource { Items = ["1", "2"] };

            var ex = Assert.Throws<NCMapper.MapConfigurationException>(
                () => Mapper.Map<RtStrListSource, RtIntFromStrDto>(src));

            // 双语容忍断言（先例 KnownIssueTests / DualEngineSemanticsConformanceTests）
            Assert.True(
                ex.Message.Contains("目标元素为值类型") || ex.Message.Contains("target element type is a value type"),
                $"Expected bilingual mixed-pair message in: {ex.Message}");
        }

        // ===================== 用例 8：无构造器自定义集合（档3）=====================

        /// <summary>
        /// 用例 8：无 IEnumerable&lt;T&gt; 构造器的自定义集合目标 → 双语 NotSupportedException
        /// <para>档3：引用转换与构造器均不可达时明确失败（替换裸 Convert 构建期崩溃，档3）。</para>
        /// </summary>
        [Fact]
        public void L02_Case08_NoCtorCollection_ThrowsNotSupported()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var ex = Assert.Throws<NotSupportedException>(
                () => Mapper.Map<RtIntListSource, RtNoCtorDto>(src));

            Assert.True(
                ex.Message.Contains("无对应的转换方法") || ex.Message.Contains("no matching conversion method"),
                $"Expected bilingual unknown-collection message in: {ex.Message}");
        }

        // ===================== 用例 9：接口目标防回归（档1）=====================

        /// <summary>
        /// 用例 9：接口目标 IList&lt;long&gt; / IEnumerable&lt;long&gt; 维持现状成功映射（档1 引用转换防回归，评审 001-B1）
        /// </summary>
        [Fact]
        public void L02_Case09_InterfaceTarget_ReferenceConversion_NoRegression()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var listDto = Mapper.Map<RtIntListSource, RtIListDto>(src)!;
            Assert.NotNull(listDto.Items);
            Assert.Equal([1L, 2L, 3L], listDto.Items);

            var enumDto = Mapper.Map<RtIntListSource, RtIEnumerableDto>(src)!;
            Assert.NotNull(enumDto.Items);
            Assert.Equal([1L, 2L, 3L], enumDto.Items);
        }

        // ===================== 用例 10/10a：Nullable 元素对 =====================

        /// <summary>
        /// 用例 10：List&lt;int&gt; → List&lt;int?&gt; 映射成功且值一致
        /// <para>实际路径由 IsImplicitConvertible 的 Nullable 归一行为决定（表命中→强转提升 /
        /// 未命中→ConvertValue ChangeType 归一），非 null 元素两路径结果一致（方案书 O4 锁定）。</para>
        /// </summary>
        [Fact]
        public void L01_Case10_ListIntToNullableInt_MapsValues()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var dto = Mapper.Map<RtIntListSource, RtNullableIntDto>(src)!;

            Assert.NotNull(dto.Items);
            Assert.Equal([1, 2, 3], dto.Items); // int? 与 int 元素级 Equal
        }

        /// <summary>
        /// 用例 10a：List&lt;int?&gt;（含 null 元素）→ List&lt;long?&gt;——null 元素 → null，非 null 值一致
        /// </summary>
        [Fact]
        public void L01_Case10a_NullableIntToNullableLong_NullElementPreserved()
        {
            var src = new RtNullableIntListSource { Items = [1, null, 3] };

            var dto = Mapper.Map<RtNullableIntListSource, RtNullableLongDto>(src)!;

            Assert.NotNull(dto.Items);
            Assert.Equal(3, dto.Items.Count);
            Assert.Equal(1L, dto.Items[0]);
            Assert.Null(dto.Items[1]);
            Assert.Equal(3L, dto.Items[2]);
        }

        // ===================== 用例 10b/10c：等价断言（防短路门禁误杀）=====================

        /// <summary>
        /// 用例 10b：接口元素对 List&lt;IFoo&gt; → List&lt;IBar&gt;（未注册，经 Resolve bootstrap）——
        /// 等价断言：与档1 现状行为一致（targetArg 引用类型一律 Resolve，防 IsClass 误判接口，评审 002-B5）。
        /// 首跑固化基线（D-2 修正）：bootstrap 后 MapperBase&lt;IFoo, IBar&gt;.Map 构建期抛
        /// MapConfigurationException（IBar 无公共构造函数）——与修复前现状等价（同路径同异常）；
        /// 该异常特征同时区分误杀场景（若误入混合对分支则报"目标元素为值类型"消息）。
        /// </summary>
        [Fact]
        public void L01_Case10b_InterfaceElementPair_ResolveBootstrap_EquivalentBehavior()
        {
            var src = new RtInterfaceListSource { Items = [new FooImpl { V = 1 }, new FooImpl { V = 2 }] };

            var ex = Assert.Throws<NCMapper.MapConfigurationException>(
                () => Mapper.Map<RtInterfaceListSource, RtInterfaceListDto>(src));

            // 基线固化（首跑实证）：走档1 Resolve → MapperBase<IFoo,IBar> Map 构建 IBar 无法实例化
            Assert.True(
                ex.Message.Contains("无公共构造函数") || ex.Message.Contains("no public"),
                $"Expected NoPublicConstructor message in: {ex.Message}");
        }

        /// <summary>
        /// 用例 10c：值类型源×class 目标 List&lt;int&gt; → List&lt;IntWrapDto&gt;（MapperBase&lt;int, IntWrapDto&gt; bootstrap 注册）——
        /// 等价断言：与档1 现状行为一致（单约束下合法注册对，不得被 targetArg 值类型短路门禁误杀，评审 002-B4-new）。
        /// 首跑固化基线（D-2 修正）：走档1 Resolve → MapperBase&lt;int, IntWrapDto&gt;.MapList 构建期抛
        /// ArgumentException（MapperBase.cs:996 Constant(null, typeof(TSource)) 对值类型 TSource 非法——
        /// MapperBase 既有缺陷，超本任务范围，登记遗留）；异常类型区分误杀场景（若误入混合对分支则抛
        /// MapConfigurationException 混合对消息，类型不同）。
        /// </summary>
        [Fact]
        public void L01_Case10c_ValueTypeSourceToClassTarget_LegitimatePair_EquivalentBehavior()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            // 基线固化（首跑实证）：合法注册对 Resolve 命中（未被短路门禁误杀），
            // 随后在 MapperBase 值类型 TSource MapList 构建处失败（既有缺陷，与 L-01 无关）
            var ex = Assert.Throws<System.ArgumentException>(
                () => Mapper.Map<RtIntListSource, RtIntWrapListDto>(src));

            Assert.False(string.IsNullOrEmpty(ex.Message));
        }

        // ===================== 用例 11：生成侧对照（双引擎 SAME）=====================

        /// <summary>
        /// 用例 11a：List&lt;int&gt; → List&lt;long&gt; 生成侧（F-04 内联）与运行时侧（L-01 强转）双侧一致
        /// </summary>
        [Fact]
        public void L01_Case11_ListIntToListLong_BothEngines_Same()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var gen = Mapper.Map<RtIntListSource, RtGenLongListDto>(src)!;
            var rt = Mapper.Map<RtIntListSource, RtLongListDto>(src)!;

            Assert.Equal(gen.Items, rt.Items);
        }

        /// <summary>
        /// 用例 11b：List&lt;int&gt; → HashSet&lt;long&gt; 双侧一致（生成侧具体集合构造 vs 运行时档2 构造器）
        /// </summary>
        [Fact]
        public void L02_Case11_ListIntToHashSet_BothEngines_Same()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var gen = Mapper.Map<RtIntListSource, RtGenHashSetDto>(src)!;
            var rt = Mapper.Map<RtIntListSource, RtHashSetDto>(src)!;

            Assert.NotNull(gen.Items);
            Assert.NotNull(rt.Items);
            Assert.True(gen.Items.SetEquals(rt.Items));
        }

        /// <summary>
        /// 用例 11c：List&lt;int&gt; → Queue&lt;long&gt; 双侧一致（FIFO 序对照）
        /// </summary>
        [Fact]
        public void L02_Case11_ListIntToQueue_BothEngines_Same()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var gen = Mapper.Map<RtIntListSource, RtGenQueueDto>(src)!;
            var rt = Mapper.Map<RtIntListSource, RtQueueDto>(src)!;

            Assert.Equal(gen.Items!.Dequeue(), rt.Items!.Dequeue());
            Assert.Equal(gen.Items!.Dequeue(), rt.Items!.Dequeue());
            Assert.Equal(gen.Items!.Dequeue(), rt.Items!.Dequeue());
        }

        /// <summary>
        /// 用例 11d：List&lt;int&gt; → Stack&lt;long&gt; 双侧一致（Pop 序对照关键项——
        /// 生成侧 Reverse 已实证 777/999 修复，运行时档2 同构包 Reverse，双侧 Pop 序均为源序）
        /// </summary>
        [Fact]
        public void L02_Case11_ListIntToStack_BothEngines_SamePopOrder()
        {
            var src = new RtIntListSource { Items = [1, 2, 3] };

            var gen = Mapper.Map<RtIntListSource, RtGenStackDto>(src)!;
            var rt = Mapper.Map<RtIntListSource, RtStackDto>(src)!;

            // 双侧 Pop 序均为源序 [1,2,3]（Stack Reverse 同构）
            Assert.Equal(1L, gen.Items!.Pop());
            Assert.Equal(1L, rt.Items!.Pop());
            Assert.Equal(2L, gen.Items!.Pop());
            Assert.Equal(2L, rt.Items!.Pop());
            Assert.Equal(3L, gen.Items!.Pop());
            Assert.Equal(3L, rt.Items!.Pop());
        }
    }
}
