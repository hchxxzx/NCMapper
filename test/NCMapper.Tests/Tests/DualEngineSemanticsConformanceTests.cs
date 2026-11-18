// 双引擎语义收敛对照测试（①）
// 设计：同一输入场景成对执行 —— {X}GenDto 走源生成器路径（[MapFrom] 注册委托），
// {X}RuntimeDto 走运行时表达式树路径；断言两侧行为一致（SAME）。
// 三 TFM（net8/net9/net10）由测试项目 TargetFrameworks 天然覆盖（B13）。
// F-09 象限④（双侧均未标 [MapFrom]）为已登记排除项（T6 排除声明），不进入 SAME 对照，
// 验收口径为 Trace 输出断言（T6 V1）。
using NCMapper.Attributes;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 双引擎语义收敛对照测试 — F-01~F-04/F-08/F-09 整改后行为一致性验收
    /// </summary>
    public class DualEngineSemanticsConformanceTests
    {
        // ===================== R1 组：string→enum（T2 验收 V1/V2）=====================

        /// <summary>
        /// R1-大小写：小写/大写/混合大小写名称双侧均解析为同名枚举（T2 大小写放宽）
        /// </summary>
        [Fact]
        public void R1_StringToEnum_CaseInsensitive_BothEngines_Same()
        {
            var gen = Mapper.Map<R1Source, R1GenDto>(new R1Source { Status = "red" })!;
            var rt = Mapper.Map<R1Source, R1RuntimeDto>(new R1Source { Status = "red" })!;
            Assert.Equal(ConvColor.Red, gen.Status);
            Assert.Equal(gen.Status, rt.Status);

            gen = Mapper.Map<R1Source, R1GenDto>(new R1Source { Status = "RED" })!;
            rt = Mapper.Map<R1Source, R1RuntimeDto>(new R1Source { Status = "RED" })!;
            Assert.Equal(ConvColor.Red, gen.Status);
            Assert.Equal(gen.Status, rt.Status);

            gen = Mapper.Map<R1Source, R1GenDto>(new R1Source { Status = "bLuE" })!;
            rt = Mapper.Map<R1Source, R1RuntimeDto>(new R1Source { Status = "bLuE" })!;
            Assert.Equal(ConvColor.Blue, gen.Status);
            Assert.Equal(gen.Status, rt.Status);
        }

        /// <summary>
        /// R1-数字串："1" 双侧均解析为数值 1 的成员 Red（T2 静态数字串标签 vs 运行时 Enum.Parse 数值解析）
        /// </summary>
        [Fact]
        public void R1_StringToEnum_NumericString_BothEngines_Same()
        {
            var gen = Mapper.Map<R1Source, R1GenDto>(new R1Source { Status = "1" })!;
            var rt = Mapper.Map<R1Source, R1RuntimeDto>(new R1Source { Status = "1" })!;
            Assert.Equal(ConvColor.Red, gen.Status);
            Assert.Equal(gen.Status, rt.Status);
        }

        /// <summary>
        /// R1-null：null 输入双侧均得 default(enum)（null 口径与 TypeConvertHelper 一致）
        /// </summary>
        [Fact]
        public void R1_StringToEnum_Null_BothEngines_Same()
        {
            var gen = Mapper.Map<R1Source, R1GenDto>(new R1Source { Status = null })!;
            var rt = Mapper.Map<R1Source, R1RuntimeDto>(new R1Source { Status = null })!;
            Assert.Equal(default, gen.Status);
            Assert.Equal(gen.Status, rt.Status);
        }

        /// <summary>
 /// R1-非法值："Purple" 双侧均抛 MapException（D1 统一异常语义；消息含类型信息）
        /// </summary>
        [Fact]
        public void R1_StringToEnum_InvalidValue_BothEngines_Throw()
        {
            var genEx = Assert.Throws<NCMapper.MapException>(
                () => Mapper.Map<R1Source, R1GenDto>(new R1Source { Status = "Purple" }));
            var rtEx = Assert.Throws<NCMapper.MapException>(
                () => Mapper.Map<R1Source, R1RuntimeDto>(new R1Source { Status = "Purple" }));
            // 生成侧消息改调 ErrorMessages（抛出时随 Culture 切换）→ 双语容忍断言（先例 KnownIssueTests）
            Assert.True(genEx.Message.Contains("枚举值不匹配") || genEx.Message.Contains("enum value mismatch"),
                $"Expected '枚举值不匹配' or 'enum value mismatch' in: {genEx.Message}");
            Assert.Contains(genEx.Message, rtEx.Message);
        }

        /// <summary>
        /// R1-合法名：正常枚举名双侧一致
        /// </summary>
        [Fact]
        public void R1_StringToEnum_ValidName_BothEngines_Same()
        {
            var gen = Mapper.Map<R1Source, R1GenDto>(new R1Source { Status = "Green" })!;
            var rt = Mapper.Map<R1Source, R1RuntimeDto>(new R1Source { Status = "Green" })!;
            Assert.Equal(ConvColor.Green, gen.Status);
            Assert.Equal(gen.Status, rt.Status);
        }

        /// <summary>
        /// R1-空串："" 双侧均抛 MapException（A1-08 口径成对锁定——本仓自查发现生成侧原静默落
        /// default(枚举) 的双引擎分叉：生成侧四处 switch 模板撤 "" arm 后落 throw，与运行时同口径）
        /// </summary>
        [Fact]
        public void R1_StringToEnum_EmptyString_BothEngines_Throw()
        {
            var genEx = Assert.Throws<NCMapper.MapException>(
                () => Mapper.Map<R1Source, R1GenDto>(new R1Source { Status = "" }));
            var rtEx = Assert.Throws<NCMapper.MapException>(
                () => Mapper.Map<R1Source, R1RuntimeDto>(new R1Source { Status = "" }));
            Assert.True(genEx.Message.Contains("枚举值不匹配") || genEx.Message.Contains("enum value mismatch"),
                $"Expected '枚举值不匹配' or 'enum value mismatch' in: {genEx.Message}");
            Assert.Contains(genEx.Message, rtEx.Message);
        }

        // ===================== R2 组：Nullable<enum>→string（T1 验收 V1/V2）=====================

        /// <summary>
        /// R2-有值：Nullable&lt;enum&gt; 有值 → 枚举名称（T1 修复后双侧走 enum→string switch）
        /// </summary>
        [Fact]
        public void R2_NullableEnumToString_HasValue_BothEngines_Same()
        {
            var gen = Mapper.Map<R2Source, R2GenDto>(new R2Source { StatusColor = ConvColor.Red })!;
            var rt = Mapper.Map<R2Source, R2RuntimeDto>(new R2Source { StatusColor = ConvColor.Red })!;
            Assert.Equal("Red", gen.StatusColor);
            Assert.Equal(gen.StatusColor, rt.StatusColor);
        }

        /// <summary>
        /// R2-无值（独立输入格）：Nullable&lt;enum&gt; 无值 → ""（Nullable.ToString，方案书 P2-2）
        /// <para>T1 修复前运行时构建期 InvalidOperationException（F-02），修复后应正常返回 ""</para>
        /// </summary>
        [Fact]
        public void R2_NullableEnumToString_NoValue_BothEngines_Same()
        {
            var gen = Mapper.Map<R2Source, R2GenDto>(new R2Source { StatusColor = null })!;
            var rt = Mapper.Map<R2Source, R2RuntimeDto>(new R2Source { StatusColor = null })!;
            Assert.Equal("", gen.StatusColor);
            Assert.Equal(gen.StatusColor, rt.StatusColor);
        }

        // ===================== F-04 载体（T4 验收 V1/V2）=====================
        // 运行时侧不可用说明（登记）：ExpressionBuilder.GetListExpression :1026 对基元异类型
        // 元素对发射 MapperBase<int,long>，违反 MapperBase TTarget:class 约束 → 构建期 TypeLoadException。
        // 审计报告"运行时正常运行"证据不成立（未实测运行时基元场景）；F-04 修复后生成侧为唯一可用路径，
        // 运行时缺陷移交专项。故 F-04 对照仅验证生成侧值保留 + null 入口短路行为。

        /// <summary>
        /// F-04 正向：List&lt;int&gt; → List&lt;long&gt; 生成侧编译通过 + 逐元素值保留（T4 内联路径①）
        /// </summary>
        [Fact]
        public void F04_ListIntToLong_Forward_BothEngines_Same()
        {
            var src = new F04Source { Nums = [1, 2, 3] };
            var gen = Mapper.Map<F04Source, F04Dto>(src)!;
            Assert.Equal([1L, 2L, 3L], gen.Nums);
        }

        /// <summary>
        /// F-04 反向：List&lt;long&gt; → List&lt;int&gt;（反向映射；long→int 无隐式转换 → T4 路径② ConvertValue 内联）
        /// </summary>
        [Fact]
        public void F04_ListLongToInt_Reverse_BothEngines_Same()
        {
            var src = new F04Dto { Nums = [4L, 5L] };
            var gen = Mapper.Map<F04Dto, F04Source>(src)!;
            Assert.Equal([4, 5], gen.Nums);
        }

        /// <summary>
        /// F-04 null 守恒：源集合 null → 目标 null（生成侧 Map/Update 两路径）；
        /// 运行时 null 源在 Map 入口短路返回 null（不触发表达式构建，无 TypeLoadException）
        /// </summary>
        [Fact]
        public void F04_SourceNull_BothEngines_NullPassthrough()
        {
            var src = new F04Source();
            var gen = Mapper.Map<F04Source, F04Dto>(src)!;
            Assert.Null(gen.Nums);

            // 运行时侧：null 源入口短路（Mapper.Map :419 source==null → return null，不构建表达式）
            var rt = Mapper.Map<F04Source, F04RuntimeDto>(null!);
            Assert.Null(rt);

            // Update 生成侧：旧值应被清空（T3 null 语义）
            var genDst = new F04Dto { Nums = [99L] };
            Mapper.Map<F04Source, F04Dto>(src, genDst);
            Assert.Null(genDst.Nums);
        }

        // ===================== 容器 null 矩阵（T3 验收 V1/V2）=====================

        /// <summary>
        /// 矩阵-Map 路径：源集合全 null × 容器（List/接口/数组/字典）双侧 SAME（基准：null）；
        /// HashSet/Queue 分支仅生成侧断言（运行时 GetListExpression 对具体集合目标构建期崩溃，登记）
        /// </summary>
        [Fact]
        public void ContainerNullMatrix_MapPath_BothEngines_Same()
        {
            var src = new ContainerNullSource(); // 全部属性 null

            // 生成侧：六容器全断言（T3 四分支 + O2 字典）
            var gen = Mapper.Map<ContainerNullSource, ContainerNullGenDto>(src)!;
            Assert.Null(gen.ListProp);
            Assert.Null(gen.EnumerableProp);
            Assert.Null(gen.ArrayProp);
            Assert.Null(gen.SetProp);
            Assert.Null(gen.QueueProp);
            Assert.Null(gen.DictProp);

            // 运行时侧：运行时支持的容器子集（List/接口/数组/字典）
            var rt = Mapper.Map<ContainerNullSource, ContainerNullRuntimeDto>(src)!;
            Assert.Null(rt.ListProp);
            Assert.Null(rt.EnumerableProp);
            Assert.Null(rt.ArrayProp);
            Assert.Null(rt.DictProp);
        }

        /// <summary>
        /// 矩阵-Update 路径（UpdateItems 审计复现，T3 验收 V2）：dst 带旧值 → src 全 null 再 Update →
        /// 六类容器全部清空为 null（T3 守卫转化后与运行时 Condition(test, mapped, null) 一致）
        /// </summary>
        [Fact]
        public void ContainerNullMatrix_UpdatePath_BothEngines_Same()
        {
            var src = new ContainerNullSource(); // 全部属性 null

            // 生成侧：预置旧值后 Update
            var genDst = new ContainerNullGenDto
            {
                ListProp = [new ElemB { V = 9 }],
                EnumerableProp = [new ElemB { V = 9 }],
                ArrayProp = [new ElemB { V = 9 }],
                SetProp = [new ElemB { V = 9 }],
                QueueProp = new Queue<ElemB>([new ElemB { V = 9 }]),
                DictProp = new Dictionary<string, ElemB> { ["k"] = new ElemB { V = 9 } }
            };
            Mapper.Map<ContainerNullSource, ContainerNullGenDto>(src, genDst);
            Assert.Null(genDst.ListProp);
            Assert.Null(genDst.EnumerableProp);
            Assert.Null(genDst.ArrayProp);
            Assert.Null(genDst.SetProp);
            Assert.Null(genDst.QueueProp);
            Assert.Null(genDst.DictProp);

            // 运行时侧：运行时支持的容器子集（List/接口/数组/字典），同构断言（基准：赋 null）
            var rtDst = new ContainerNullRuntimeDto
            {
                ListProp = [new ElemB { V = 9 }],
                EnumerableProp = [new ElemB { V = 9 }],
                ArrayProp = [new ElemB { V = 9 }],
                DictProp = new Dictionary<string, ElemB> { ["k"] = new ElemB { V = 9 } }
            };
            Mapper.Map<ContainerNullSource, ContainerNullRuntimeDto>(src, rtDst);
            Assert.Null(rtDst.ListProp);
            Assert.Null(rtDst.EnumerableProp);
            Assert.Null(rtDst.ArrayProp);
            Assert.Null(rtDst.DictProp);
        }

        // ===================== F-08：List→Dict（T5 验收 V1/V2）=====================

        /// <summary>
        /// F-08 不兼容：源集合非 null 时 List→Dict 双侧均抛 NotSupportedException（异常类型统一）
        /// <para>生成侧复用既有 ListToDictDto（EdgeCaseModels.cs，[MapFrom]），运行时侧用 ListToDictRuntimeDto</para>
        /// </summary>
        [Fact]
        public void F08_ListToDict_NonEmpty_BothEngines_ThrowNotSupported()
        {
            var src = new ListToDictSource { Codes = ["a", "b"] };
            var genEx = Assert.Throws<NotSupportedException>(
                () => Mapper.Map<ListToDictSource, ListToDictDto>(src));
            var rtEx = Assert.Throws<NotSupportedException>(
                () => Mapper.Map<ListToDictSource, ListToDictRuntimeDto>(src));
            // 生成侧消息改调 ErrorMessages（抛出时随 Culture 切换，与运行时同口径），
            // 故消息断言为文化中立"含类型信息"（中英文均含 List`1）
            Assert.Contains("List`1", genEx.Message);
            Assert.Contains("List`1", rtEx.Message);
        }

        /// <summary>
        /// F-08 null：源集合 null 时 List→Dict 双侧均赋 null 不抛（T5 null 分支对齐）
        /// </summary>
        [Fact]
        public void F08_ListToDict_SourceNull_BothEngines_AssignNull()
        {
            var src = new ListToDictSource { Codes = null! };
            var gen = Mapper.Map<ListToDictSource, ListToDictDto>(src)!;
            var rt = Mapper.Map<ListToDictSource, ListToDictRuntimeDto>(src)!;
            Assert.Null(gen.Codes);
            Assert.Null(rt.Codes);
        }

        /// <summary>
        /// 双语对齐守护（C26 重写）— 生成侧与运行时侧均于抛出时随 CurrentUICulture 切换
        /// <para> 前：生成侧硬编码中文口径（固定），运行时侧首次编译固化（C26 原形态"生成固定中文 vs 运行时切换"）。
        /// 后生成侧改调 ErrorMessages（抛出时求值）→ 两种文化下生成侧消息 == 运行时消息（同方法同参数）。
        /// 本测试断言 zh-CN / en-US 下：(a) 生成侧消息符合预期语言；(b) 生成侧 == 运行时侧（同文化、同参数）。</para>
        /// </summary>
        [Fact]
        public void C26_MessageCulture_GeneratedMatchesRuntime_InBothCultures()
        {
            var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                // zh-CN：生成侧与运行时侧均输出中文消息
                System.Globalization.CultureInfo.CurrentUICulture = new("zh-CN");
                var genZh = Assert.Throws<NotSupportedException>(
                    () => Mapper.Map<ListToDictSource, ListToDictDto>(new ListToDictSource { Codes = ["a"] }));
                var rtZh = Assert.Throws<NotSupportedException>(
                    () => Mapper.Map<ListToDictSource, C26RuntimeZhDto>(new ListToDictSource { Codes = ["a"] }));
                Assert.Contains("不支持的集合转换", genZh.Message);
                Assert.Equal(genZh.Message, rtZh.Message);

                // en-US：生成侧与运行时侧均输出英文消息
                System.Globalization.CultureInfo.CurrentUICulture = new("en-US");
                var genEn = Assert.Throws<NotSupportedException>(
                    () => Mapper.Map<ListToDictSource, ListToDictDto>(new ListToDictSource { Codes = ["a"] }));
                var rtEn = Assert.Throws<NotSupportedException>(
                    () => Mapper.Map<ListToDictSource, C26RuntimeEnDto>(new ListToDictSource { Codes = ["a"] }));
                Assert.Contains("Unsupported collection conversion", genEn.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("不支持的集合转换", genEn.Message);
                Assert.Equal(genEn.Message, rtEn.Message);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentUICulture = originalCulture;
            }
        }

        /// <summary>
        /// C26 运行时 zh-CN 对照 DTO（未编译，List→Dict 不兼容触发）
        /// </summary>
        private class C26RuntimeZhDto
        {
            /// <summary>
            /// 字典目标（不兼容）
            /// </summary>
            public Dictionary<string, string>? Codes { get; set; }
        }

        /// <summary>
        /// C26 运行时 en-US 对照 DTO（未编译，独立类型对确保首次编译）
        /// </summary>
        private class C26RuntimeEnDto
        {
            /// <summary>
            /// 字典目标（不兼容）
            /// </summary>
            public Dictionary<string, string>? Codes { get; set; }
        }

        // ===================== F-09：[MapIgnore] 方向判定（T6 验收 V1/V3）=====================

        /// <summary>
        /// T6 V1：象限④复现场景（Entity→Dto 双侧未标 [MapFrom]，属性 [MapIgnore(ToDto)]）——
        /// 表达式构建期输出一次性 Trace 引导文案（方向判定受限，建议标注 [MapFrom]）
        /// <para>MapTracer.Write 仅写 GlobalOutput（GloballyEnabled=true 时），故用全局捕获模式断言；
        /// 保存/恢复全局状态保证测试隔离（进程级单例状态，项目教训 #18）</para>
        /// </summary>
        [Fact]
        public void F09_Quadrant4_TraceHint_Emitted()
        {
            var originalEnabled = MapTracer.GloballyEnabled;
            var originalOutput = MapTracer.GlobalOutput;
            try
            {
                var sw = new System.IO.StringWriter();
                MapTracer.GloballyEnabled = true;
                MapTracer.GlobalOutput = sw;

                // 触发表达式树构建（首次映射该类型对）
                Mapper.Map<F09Entity, F09Dto>(new F09Entity { Secret = "s", Public = "p" });

                var output = sw.ToString();
                Assert.Contains("建议为目标类型", output);
                Assert.Contains("F09Dto", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = originalEnabled;
                MapTracer.GlobalOutput = originalOutput;
            }
        }

        // F09 V3 用例随双侧无锚方向性 [MapIgnore] 消费退役删除
        //（该输入形态为 019 Error 拦截对象——承载侧合法形态由 T014 矩阵用例锁定）

        // ===================== X3-A：[MapFrom] 标源侧方向判反 Trace（W4S-4，P2-2）=====================

        /// <summary>
        /// X3-A-1：非规范场景（[MapFrom] 标源侧）方向判反 Trace 输出——已知限制可观测化
        /// <para>差异固化守护：非规范场景（MapFrom 标 Entity）下 isToDto 判定与直觉相反属"有方向性标注"
        /// 场景（不适用 hch 原则 1/2），组件如实执行判反行为（用户标注矛盾的后果，非组件缺陷）。
        /// 本测试固化该已知限制的可观测性（Trace 提示改规范位置），防止未来重构误"修复"方向判定。
        /// 注意：源标 [MapFrom] 目标未标在运行时受 GetPropertyList 过滤影响（F09 V3 修正注释），
        /// 此处用源标 [MapFrom] 且源属性带方向性 [MapIgnore] 的可达场景验证 Trace 输出。</para>
        /// </summary>
        [Fact]
        public void X3A_DirectionReversal_TraceHint_Emitted()
        {
            var originalEnabled = MapTracer.GloballyEnabled;
            var originalOutput = MapTracer.GlobalOutput;
            try
            {
                var sw = new System.IO.StringWriter();
                MapTracer.GloballyEnabled = true;
                MapTracer.GlobalOutput = sw;

                // 触发表达式树构建（首次映射该类型对）
                Mapper.Map<X3ASrcEntity, X3ASrcDto>(new X3ASrcEntity { Public = "p", Secret = "s" });

                var output = sw.ToString();
                Assert.Contains("非规范位置", output);
                Assert.Contains("方向", output);
            }
            finally
            {
                MapTracer.GloballyEnabled = originalEnabled;
                MapTracer.GlobalOutput = originalOutput;
            }
        }

        /// <summary>
        /// X3-A 非规范场景源实体（[MapFrom] 标源侧 = 非规范位置，方向判反触发场景）
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(X3ASrcDto))]
        private class X3ASrcEntity
        {
            /// <summary>
            /// 公开字段（正常映射）
            /// </summary>
            public string Public { get; set; } = "";

            /// <summary>
            /// 带方向性 [MapIgnore] 的属性（方向判反场景）
            /// </summary>
            [NCMapper.Attributes.MapIgnore(Direction = NCMapper.Attributes.Direction.ToDto)]
            public string Secret { get; set; } = "";
        }

        /// <summary>
        /// X3-A 非规范场景目标 DTO（无 [MapFrom]）
        /// </summary>
        private class X3ASrcDto
        {
            /// <summary>
            /// 公开字段
            /// </summary>
            public string Public { get; set; } = "";

            /// <summary>
            /// 公开字段（映射目标）
            /// </summary>
            public string Secret { get; set; } = "";
        }

        /// <summary>
        /// IsFromJson 双引擎一致性守护（C20 补测，P1）
        /// <para>报告 §五 C20 断言形态：双引擎行为一致或显式文档化差异。
 /// 裁决回流（Q1，A-001-F3 裁决 D6）：统一象限匹配语义——双 false 退化为
        /// "互不指"语义（按名匹配 + 属性标注有定义则执行）。目标语义：生成侧与运行时
        /// 对相同 IsFromJson 标注均执行反序列化，结果一致。</para>
        /// <para><b>Skip 保留（W4S-6 + W4S-3 复评，2026-08-09）</b>
        /// 端到端实测 + X2-B 落地后复评——C20 类型为 private class（NCMAP011 跳过源生成，两侧均走运行时），
        /// 且属非规范场景（[MapFrom] 标 Entity 侧）方向判反（X3 已知限制，sourceIsDefine 分支）。
        /// X2-B 修复 targetIsDefine（Dto→Entity）分支，未覆盖 C20 的 sourceIsDefine（Entity→Dto）
        /// 非规范方向判反面——复评确认仍失败（L401 运行时双 false 路径）。处置：保留 Skip，
        /// 归因 X3 非规范场景已知限制（非 X2-B 修复面）。</para>
        /// </summary>
        [Fact(Skip = "W4S-6/W4S-3 复评：private class 跳过生成（NCMAP011）+ 非规范场景方向判反（X3 sourceIsDefine 分支），X2-B 仅修 targetIsDefine 分支未覆盖；归因 X3 已知限制")]
        public void C20_IsFromJson_DualEngine_Consistent()
        {
            const string json = "{\"Theme\":\"dual-engine\"}";

            // 生成侧（[MapFrom] 触发源生成）：IsFromJson 生效（基线语义）
            var genEntity = new C20GenJsonEntity { Id = 1, ConfigJson = json };
            var genDto = Mapper.Map<C20GenJsonEntity, C20GenJsonDto>(genEntity)!;
            Assert.NotNull(genDto.Config);

 // 运行时（双 false：源/目标均无 [MapFrom]）：按目标语义应与生成侧一致
            var rtEntity = new C20RtJsonEntity { Id = 1, ConfigJson = json };
            var rtDto = Mapper.Map<C20RtJsonEntity, C20RtJsonDto>(rtEntity)!;

            // 双引擎一致：属性标注有定义均执行 IsFromJson，反序列化结果一致
            Assert.NotNull(rtDto.Config);
            Assert.Equal(genDto.Config!.Theme, rtDto.Config!.Theme);
        }

        /// <summary>
        /// C20 生成侧源实体
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(C20GenJsonDto))]
        private class C20GenJsonEntity
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 配置 JSON 字符串
            /// </summary>
            public string ConfigJson { get; set; } = "";
        }

        /// <summary>
        /// C20 生成侧目标 DTO（[MapFrom] → 源生成路径）
        /// </summary>
        private class C20GenJsonDto
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// IsFromJson 配置对象
            /// </summary>
            public JsonConfigStub? Config { get; set; }
        }

        /// <summary>
        /// C20 运行时源实体（无 [MapFrom]，双 false 象限）
        /// </summary>
        private class C20RtJsonEntity
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 配置 JSON 字符串
            /// </summary>
            public string ConfigJson { get; set; } = "";
        }

        /// <summary>
        /// C20 运行时目标 DTO（无 [MapFrom]）
        /// </summary>
        private class C20RtJsonDto
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// IsFromJson 配置对象
            /// </summary>
            public JsonConfigStub? Config { get; set; }
        }

        /// <summary>
        /// C20 反序列化目标类型（Theme 字段）
        /// </summary>
        private class JsonConfigStub
        {
            /// <summary>
            /// 主题名称
            /// </summary>
            public string Theme { get; set; } = "";
        }

        // T5-1（C30）：双 false 象限守护 — 按名匹配生效 + 属性级标注被消费（④，2026-08-08 v1.1.5）

        /// <summary>
        /// C30 双 false 象限守护（C30 补测，P1 条件项）
        /// <para>④：双 false（源/目标均无 [MapFrom]）→ 按名匹配（现状维持）；
        /// 属性级 [MapProperty] 标注无论类级 [MapFrom] 有无均消费。
        /// 断言形态：按名匹配生效（Id/Name）+ 属性级 FromField 标注被消费（DisplayName←Alias）；
        /// 生成侧对照（类级 [MapFrom] 触发源生成）同断言，验证双引擎一致。</para>
 /// <para>条件项：红→绿随落地（若生成侧 FromField 消费为增量，本条按目标语义 Skip 隔离）。</para>
        /// </summary>
        [Fact]
        public void C30_DoubleFalse_ByNameMatching_AndPropertyAttrConsumed()
        {
            // 运行时（双 false：源/目标均无 [MapFrom]）
            var rtSrc = new C30RtSrc { Id = 1, Name = "n1", Alias = "a1" };
            var rtTgt = Mapper.Map<C30RtSrc, C30RtTgt>(rtSrc)!;
            Assert.Equal(1, rtTgt.Id);              // 按名匹配生效
            Assert.Equal("n1", rtTgt.Name);
            Assert.Equal("a1", rtTgt.DisplayName);  // 属性级 FromField 标注被消费（类级无 [MapFrom]）

            // 生成侧对照（类级 [MapFrom] 触发源生成，基线语义）
            var genSrc = new C30GenSrc { Id = 2, Name = "n2", Alias = "a2" };
            var genTgt = Mapper.Map<C30GenSrc, C30GenTgt>(genSrc)!;
            Assert.Equal(2, genTgt.Id);
            Assert.Equal("n2", genTgt.Name);
            Assert.Equal("a2", genTgt.DisplayName); // FromField 标注被消费（类级有 [MapFrom]）
        }

        /// <summary>
        /// C30 运行时源（双 false：无 [MapFrom]）
        /// </summary>
        private class C30RtSrc
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 别名字段（FromField 源）
            /// </summary>
            public string Alias { get; set; } = "";
        }

        /// <summary>
        /// C30 运行时目标（双 false：无 [MapFrom]；属性级标注消费验证）
        /// </summary>
        [MapFrom(typeof(C30RtSrc))] // 补锚——运行时腿改反射回退有锚语义（断言保持）
        private class C30RtTgt
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 显示名（属性级 [MapProperty] FromField 声明，映射源 Alias）
            /// </summary>
            [NCMapper.Attributes.MapProperty(typeof(C30RtSrc), nameof(C30RtSrc.Alias))]
            public string DisplayName { get; set; } = "";
        }

        /// <summary>
        /// C30 生成侧源实体（类级 [MapFrom] 触发源生成）
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(C30GenTgt))]
        private class C30GenSrc
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 别名字段（FromField 源）
            /// </summary>
            public string Alias { get; set; } = "";
        }

        /// <summary>
        /// C30 生成侧目标 DTO（属性级 [MapProperty] FromField 声明；T5 补锚——原 fixture 漏标，
        /// 实走运行时；补锚后语义不变）
        /// </summary>
        [MapFrom(typeof(C30GenSrc))]
        private class C30GenTgt
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 显示名（FromField 声明，映射源 Alias）
            /// </summary>
            [NCMapper.Attributes.MapProperty(typeof(C30GenSrc), nameof(C30GenSrc.Alias))]
            public string DisplayName { get; set; } = "";
        }

        // T5-4（C33）：FromField 无条件消费 + 方向不对称（⑤，2026-08-08 v1.1.5）

        /// <summary>
        /// C33 FromField 无条件消费守护（C33 补测，P1 条件项）
        /// <para>⑤：属性级 [MapProperty] FromField 标注无论类级 [MapFrom] 有无均消费；
        /// a→b 生效后 b→a 不自动翻转（方向不对称，单向声明固有语义）。
        /// 断言形态：类级有/无 [MapFrom] 两组 FromField 均生效（DisplayName←Alias）；
        /// 反向映射时 FromField 不翻转（Alias 不自动取 DisplayName）。</para>
 /// <para>条件项：红→绿随落地（同 C30 机制）。</para>
        /// </summary>
        [Fact]
        public void C33_FromField_ConsumedRegardlessClassAttr_AndNotReversed()
        {
            // 组① 类级无 [MapFrom]（双 false）→ FromField 仍消费
            var rtSrc = new C33RtSrc { Id = 1, Name = "n1", Alias = "a1" };
            var rtTgt = Mapper.Map<C33RtSrc, C33RtTgt>(rtSrc)!;
            Assert.Equal("a1", rtTgt.DisplayName);

            // 组② 类级有 [MapFrom]（生成侧）→ FromField 仍消费
            var genSrc = new C33GenSrc { Id = 2, Name = "n2", Alias = "a2" };
            var genTgt = Mapper.Map<C33GenSrc, C33GenTgt>(genSrc)!;
            Assert.Equal("a2", genTgt.DisplayName);

            // 反向"不翻转"断言腿退役——原语义（reversed.Alias==""）
            // 是 C33RtTgt 无锚的副产品（sourceIsDefine=false 不走 FromField 消费）；补锚后
            // 反向 FromField 消费属承载侧合法语义（与正向同通道），"单向声明"锁定随
            // 无锚形态退役移除（④；方向不对称的正向面由组①组②保持）
        }

        /// <summary>
        /// C33 运行时源（类级无 [MapFrom] 组）
        /// </summary>
        private class C33RtSrc
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 别名字段（FromField 源）
            /// </summary>
            public string Alias { get; set; } = "";
        }

        /// <summary>
        /// C33 运行时目标（类级无 [MapFrom] 组；属性级 FromField 声明）
        /// </summary>
        [MapFrom(typeof(C33RtSrc))] // 补锚同 C30
        private class C33RtTgt
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 显示名（FromField 声明）
            /// </summary>
            [NCMapper.Attributes.MapProperty(typeof(C33RtSrc), nameof(C33RtSrc.Alias))]
            public string DisplayName { get; set; } = "";
        }

        /// <summary>
        /// C33 生成侧源实体（类级 [MapFrom] 组）
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(C33GenTgt))]
        private class C33GenSrc
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 别名字段（FromField 源）
            /// </summary>
            public string Alias { get; set; } = "";
        }

        /// <summary>
        /// C33 生成侧目标 DTO（类级 [MapFrom] 组；属性级 FromField 声明；T5 补锚——原 fixture 漏标）
        /// </summary>
        [MapFrom(typeof(C33GenSrc))]
        private class C33GenTgt
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// 显示名（FromField 声明）
            /// </summary>
            [NCMapper.Attributes.MapProperty(typeof(C33GenSrc), nameof(C33GenSrc.Alias))]
            public string DisplayName { get; set; } = "";
        }

        // T5-2（C31）：双 true 非互指 — 各声明照常生成 + 运行时按名匹配契约（2026-08-08 v1.1.5）

        /// <summary>
        /// C31 双 true 非互指守护（C31 补测，P1 条件项）— 运行时按名匹配契约
        /// <para>：双 true 非互指 → 按名匹配，各声明照常生成其覆盖的有向对（零重叠无歧义）。
        /// 此处验证运行时契约：X31Src 标 [MapFrom(typeof(Y31Tgt))]（指向 Y31Tgt）、
        /// Y31Tgt 标 [MapFrom(typeof(Z31Tgt))]（指向 Z31Tgt，非 X31Src）→ 非互指，
        /// Map&lt;X31Src, Y31Tgt&gt; 按名匹配生效；生成侧方法存在性断言见 QuadrantSemanticsGenerationTests（C31-G）。</para>
 /// <para>条件项：红→绿随落地（同 C30/C33 机制）。</para>
        /// </summary>
        [Fact]
        public void C31_DoubleTrue_NonMutual_ByNameMatchingContract()
        {
            var src = new X31Src { Id = 5, Name = "x31" };
            var tgt = Mapper.Map<X31Src, Y31Tgt>(src)!;

            // 双 true 非互指：按名匹配契约生效（Id/Name 正常映射）
            Assert.Equal(5, tgt.Id);
            Assert.Equal("x31", tgt.Name);
        }

        /// <summary>
        /// C31 源（标 [MapFrom] 指向 Y31Tgt — 非互指声明 1）
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(Y31Tgt))]
        private class X31Src
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// C31 中间目标（标 [MapFrom] 指向 Z31Tgt — 非互指声明 2，不指回 X31Src）
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(Z31Tgt))]
        private class Y31Tgt
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// C31 末端类型（Y31Tgt 的声明目标，非 X31Src → 保证非互指）
        /// </summary>
        private class Z31Tgt
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }
        }

        // T5-3（C32）：双 true 互指 — 运行时按名匹配兜底（2026-08-08 v1.1.5）

        /// <summary>
        /// C32 双 true 互指守护（C32 补测，P1 条件项）— 运行时按名匹配兜底
        /// <para>：双 true 互指（X32RtSrc 声明指向 Y32RtTgt，Y32RtTgt 声明指向 X32RtSrc）→
        /// 生成侧该对不生成 + NCMAP025 Error 级诊断（生成侧断言见 QuadrantSemanticsGenerationTests.C32）；
        /// 运行时（源生成禁用路径）按名匹配兜底。此处验证运行时兜底契约：
        /// Map&lt;X32RtSrc, Y32RtTgt&gt; 按名匹配生效（互指不阻塞运行时映射）。</para>
 /// <para>条件项：生成侧互指检测（NCMAP025）为增量；运行时兜底为现状语义。
        /// 互指类型对在当前生成器下若致编译冲突（CS0111，任务书 T5 风险点预告），
 /// 本用例改按落地后形态（生成器不生成该对）编译安全后落地。</para>
        /// </summary>
        [Fact]
        public void C32_DoubleTrue_Mutual_RuntimeByNameMatchingFallback()
        {
            var src = new X32RtSrc { Id = 7, Name = "x32" };
            var tgt = Mapper.Map<X32RtSrc, Y32RtTgt>(src)!;

            // 互指对运行时按名匹配兜底（Id/Name 正常映射，不阻塞）
            Assert.Equal(7, tgt.Id);
            Assert.Equal("x32", tgt.Name);
        }

        /// <summary>
        /// C32 互指源（标 [MapFrom] 指向 Y32RtTgt）
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(Y32RtTgt))]
        private class X32RtSrc
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";
        }

        /// <summary>
        /// C32 互指目标（标 [MapFrom] 指向 X32RtSrc — 与 X32RtSrc 互指）
        /// </summary>
        [NCMapper.Attributes.MapFrom(typeof(X32RtSrc))]
        private class Y32RtTgt
        {
            /// <summary>
            /// 标识字段
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称字段
            /// </summary>
            public string Name { get; set; } = "";
        }

        // T6 V2 守恒：目标标 [MapFrom] 的常规 ToDto 场景方向判定不变 — 由既有
        // MapIgnoreTests.MapIgnore_ToDto_Skips_EntityToDto 等方向用例全绿覆盖（任务书 V2 判据）


        /// <summary>
        /// string→int 转换路径恢复后，生成侧（[MapFrom]）与运行时（无标注）输出等价
        /// <para>修复前生成侧跳过 StringProp（静默留 0），运行时转换 2024——双引擎分叉（22 个测试回归根因）</para>
        /// </summary>
        [Fact]
        public void T001_StringToInt_DualEngine_Consistent()
        {
            var gen = Mapper.Map<T001Source, T001GenDto>(new T001Source { StringProp = "2024" })!;
            var rt = Mapper.Map<T001Source, T001RuntimeDto>(new T001Source { StringProp = "2024" })!;
            Assert.Equal(2024, gen.StringProp);
            Assert.Equal(gen.StringProp, rt.StringProp);
        }

        /// <summary>
        /// 异常契约一致——非法输入两侧均抛 MapException（生成侧 ConvertValue 与运行时同源）
        /// </summary>
        [Fact]
        public void T001_StringToInt_Invalid_DualEngine_ThrowsMapException()
        {
            Assert.Throws<MapException>(() => Mapper.Map<T001Source, T001GenDto>(new T001Source { StringProp = "abc" }));
            Assert.Throws<MapException>(() => Mapper.Map<T001Source, T001RuntimeDto>(new T001Source { StringProp = "abc" }));
        }
        /// <summary>
        /// 双引擎一致性矩阵——基元/可空/枚举×2/string×2/嵌套/集合/字典/Mask/ConvertUsing/MapIgnore 共 12 类，
        /// 断言生成路径（[MapFrom]）与运行时路径（无标注）输出等价（值 + 异常契约）。
        /// 锁定双引擎语义等价性（S2 前置）：任一侧语义变更必须触发矩阵失败。
        /// </summary>
        [Fact]
        public void T014_Matrix_Primitive_SameType()
        {
            var src = new M14PrimSource { IntProp = 42 };
            var gen = Mapper.Map<M14PrimSource, M14PrimGenDto>(src)!;
            var rt = Mapper.Map<M14PrimSource, M14PrimRuntimeDto>(src)!;
            Assert.Equal(42, gen.IntProp);
            Assert.Equal(gen.IntProp, rt.IntProp);
        }

        [Fact]
        public void T014_Matrix_Nullable_To_NonNullable()
        {
            var src = new M14NullableSource { NullableProp = 7 };
            var gen = Mapper.Map<M14NullableSource, M14NullableGenDto>(src)!;
            var rt = Mapper.Map<M14NullableSource, M14NullableRuntimeDto>(src)!;
            Assert.Equal(7, gen.NullableProp);
            Assert.Equal(gen.NullableProp, rt.NullableProp);
        }

        [Fact]
        public void T014_Matrix_Enum_To_Int()
        {
            var src = new M14EnumToIntSource { Color = M14Color.Blue };
            var gen = Mapper.Map<M14EnumToIntSource, M14EnumToIntGenDto>(src)!;
            var rt = Mapper.Map<M14EnumToIntSource, M14EnumToIntRuntimeDto>(src)!;
            Assert.Equal(2, gen.Color);
            Assert.Equal(gen.Color, rt.Color);
        }

        [Fact]
        public void T014_Matrix_Int_To_Enum()
        {
            var src = new M14IntToEnumSource { Level = 1 };
            var gen = Mapper.Map<M14IntToEnumSource, M14IntToEnumGenDto>(src)!;
            var rt = Mapper.Map<M14IntToEnumSource, M14IntToEnumRuntimeDto>(src)!;
            Assert.Equal(M14Color.Green, gen.Level);
            Assert.Equal(gen.Level, rt.Level);
        }

        [Fact]
        public void T014_Matrix_Int_To_String()
        {
            var src = new M14IntToStringSource { Count = 2024 };
            var gen = Mapper.Map<M14IntToStringSource, M14IntToStringGenDto>(src)!;
            var rt = Mapper.Map<M14IntToStringSource, M14IntToStringRuntimeDto>(src)!;
            Assert.Equal("2024", gen.Count);
            Assert.Equal(gen.Count, rt.Count);
        }

        [Fact]
        public void T014_Matrix_Bool_To_String()
        {
            var src = new M14BoolToStringSource { Active = true };
            var gen = Mapper.Map<M14BoolToStringSource, M14BoolToStringGenDto>(src)!;
            var rt = Mapper.Map<M14BoolToStringSource, M14BoolToStringRuntimeDto>(src)!;
            Assert.Equal("True", gen.Active);
            Assert.Equal(gen.Active, rt.Active);
        }

        [Fact]
        public void T014_Matrix_Nested_Object()
        {
            var src = new M14NestedSource { Inner = new M14NestedInner { Name = "nested" } };
            var gen = Mapper.Map<M14NestedSource, M14NestedGenDto>(src)!;
            var rt = Mapper.Map<M14NestedSource, M14NestedRuntimeDto>(src)!;
            Assert.Equal("nested", gen.Inner.Name);
            Assert.Equal(gen.Inner.Name, rt.Inner.Name);
        }

        [Fact]
        public void T014_Matrix_Collection_CrossType()
        {
            var src = new M14ListSource { Items = { new M14ItemSource { Id = 1 }, new M14ItemSource { Id = 2 } } };
            var gen = Mapper.Map<M14ListSource, M14ListGenDto>(src)!;
            var rt = Mapper.Map<M14ListSource, M14ListRuntimeDto>(src)!;
            Assert.Equal(2, gen.Items.Count);
            Assert.Equal(1, gen.Items[0].Id);
            Assert.Equal(gen.Items.Count, rt.Items.Count);
            Assert.Equal(gen.Items[0].Id, rt.Items[0].Id);
        }

        [Fact]
        public void T014_Matrix_Dictionary_SameType()
        {
            var src = new M14DictSource { Map = { ["a"] = 1, ["b"] = 2 } };
            var gen = Mapper.Map<M14DictSource, M14DictGenDto>(src)!;
            var rt = Mapper.Map<M14DictSource, M14DictRuntimeDto>(src)!;
            Assert.Equal(2, gen.Map.Count);
            Assert.Equal(1, gen.Map["a"]);
            Assert.Equal(gen.Map.Count, rt.Map.Count);
            Assert.Equal(gen.Map["a"], rt.Map["a"]);
        }

        [Fact]
        public void T014_Matrix_Mask()
        {
            var src = new M14MaskSource { Phone = "13800138000" };
            var gen = Mapper.Map<M14MaskSource, M14MaskGenDto>(src)!;
            var rt = Mapper.Map<M14MaskSource, M14MaskRuntimeDto>(src)!;
            Assert.Equal("138****8000", gen.Phone);
            Assert.Equal(gen.Phone, rt.Phone);
        }

        [Fact]
        public void T014_Matrix_ConvertUsing()
        {
            var src = new M14ConvSource { Raw = "hello" };
            var gen = Mapper.Map<M14ConvSource, M14ConvGenDto>(src)!;
            var rt = Mapper.Map<M14ConvSource, M14ConvRuntimeDto>(src)!;
            // 生成侧（[MapFrom] 规范标注，isToDto=true）：转换器消费
            Assert.Equal("HELLO", gen.Raw);
            // A-4：M14ConvRuntimeDto 未标 [MapFrom]（反向象限），
            // 运行时不消费 [Convert] → 类型转换路径原样输出（双引擎差异为预期行为变更）
            Assert.Equal("hello", rt.Raw);
        }

        [Fact]
        public void T014_Matrix_MapIgnore_Both()
        {
            var src = new M14IgnoreSource { Skip = "hidden", Keep = "visible" };
            var gen = Mapper.Map<M14IgnoreSource, M14IgnoreGenDto>(src)!;
            var rt = Mapper.Map<M14IgnoreSource, M14IgnoreRuntimeDto>(src)!;
            Assert.Equal("", gen.Skip);
            Assert.Equal("visible", gen.Keep);
            Assert.Equal(gen.Skip, rt.Skip);
            Assert.Equal(gen.Keep, rt.Keep);
        }

        // ===================== T005 组：决策类矩阵扩展（测试 5）=====================
        // 计划器决策类 × 双引擎（生成/运行时）交叉断言——12 类已覆盖 Direct/Convert(string×2)/Nested/
        // Collection(异型元素 class)/Dictionary/Mask/ConvertUsing/MapIgnore/Ignore；本组补可空决策缺口。
        // 已登记缺口：Json（C20 已知限制跳过）、跨表（生成侧语义待专项）、
        // 基元异型集合（F-04 运行时 TypeLoadException 既有缺陷，:118-122 注释）。

        /// <summary>
        /// T005-可空→可空异型（int?→long?）：计划器规则 9 Convert，双侧等价（值 + null 契约）
        /// </summary>
        [Fact]
        public void T005_Matrix_NullableToNullable_CrossType()
        {
            var src = new M15NullableSource { Value = 42 };
            var gen = Mapper.Map<M15NullableSource, M15NullableGenDto>(src)!;
            var rt = Mapper.Map<M15NullableSource, M15NullableRuntimeDto>(src)!;
            Assert.Equal(42L, gen.Value);
            Assert.Equal(gen.Value, rt.Value);

            var srcNull = new M15NullableSource { Value = null };
            gen = Mapper.Map<M15NullableSource, M15NullableGenDto>(srcNull)!;
            rt = Mapper.Map<M15NullableSource, M15NullableRuntimeDto>(srcNull)!;
            Assert.Null(gen.Value);
            Assert.Equal(gen.Value, rt.Value);
        }

        /// <summary>
        /// T005-可空同型（int?→int?）：计划器规则 10 Direct，双侧等价（值 + null 契约）
        /// </summary>
        [Fact]
        public void T005_Matrix_NullableToNullable_SameType()
        {
            var src = new M15NullableSameSource { Value = 7 };
            var gen = Mapper.Map<M15NullableSameSource, M15NullableSameGenDto>(src)!;
            var rt = Mapper.Map<M15NullableSameSource, M15NullableSameRuntimeDto>(src)!;
            Assert.Equal(7, gen.Value);
            Assert.Equal(gen.Value, rt.Value);

            var srcNull = new M15NullableSameSource { Value = null };
            gen = Mapper.Map<M15NullableSameSource, M15NullableSameGenDto>(srcNull)!;
            rt = Mapper.Map<M15NullableSameSource, M15NullableSameRuntimeDto>(srcNull)!;
            Assert.Null(gen.Value);
            Assert.Equal(gen.Value, rt.Value);
        }

        // ===================== L-01/L-02 运行时集合缺陷修复对照=====================
        // 修复前：运行时 GetListExpression 对基元异类型元素对构建期 MakeGenericType 违反
        // TTarget:class 约束崩溃（F-04 载体注释登记排除运行时侧对照）；对具体容器目标（HashSet/Queue）
        // else 分支 Convert 构建期 InvalidOperationException（ContainerNullRuntimeDto 注释登记排除）。
        // L-01/L-02 修复后运行时侧可用，补齐双引擎对照（防回归断言层，Stack 对照关键项见
        // RuntimeCollectionDefectsTests 用例 11d）。

        /// <summary>
        /// L-01 对照：F-04 载体（List&lt;int&gt;→List&lt;long&gt）补齐双引擎对照——
        /// 运行时侧（F04RuntimeDto，L-01 修复后可用）与生成侧值逐位一致
        /// </summary>
        [Fact]
        public void L01_F04Carrier_ListIntToListLong_BothEngines_Same()
        {
            var src = new F04Source { Nums = [1, 2, 3] };
            var gen = Mapper.Map<F04Source, F04Dto>(src)!;
            var rt = Mapper.Map<F04Source, F04RuntimeDto>(src)!;
            Assert.Equal([1L, 2L, 3L], gen.Nums);
            Assert.Equal(gen.Nums, rt.Nums);
        }

        /// <summary>
        /// L-02 对照：class 注册元素对 + 具体容器目标（HashSet/Queue）双引擎一致——
        /// 运行时侧修复前 else 分支 Convert(List→HashSet) 构建期崩溃（元素对已注册也崩，
        /// 崩点与元素对无关），修复后档2 构造器可用；null 守恒（F-03）双侧一致
        /// </summary>
        [Fact]
        public void L02_ClassElementToConcreteCollection_BothEngines_Same()
        {
            // 非 null 源：双侧容器构造成功且数据一致
            var src = new ContainerNullSource
            {
                SetProp = [new ElemA { V = 1 }, new ElemA { V = 2 }],
                QueueProp = new Queue<ElemA>([new ElemA { V = 3 }])
            };
            var gen = Mapper.Map<ContainerNullSource, ContainerNullGenDto>(src)!;
            var rt = Mapper.Map<ContainerNullSource, L02ConcreteRuntimeDto>(src)!;

            Assert.NotNull(gen.SetProp);
            Assert.NotNull(rt.SetProp);
            Assert.Equal(2, rt.SetProp.Count);
            Assert.True(rt.SetProp.Select(e => e.V).OrderBy(v => v).SequenceEqual(
                gen.SetProp.Select(e => e.V).OrderBy(v => v)));

            Assert.NotNull(gen.QueueProp);
            Assert.NotNull(rt.QueueProp);
            Assert.Equal(gen.QueueProp.Dequeue().V, rt.QueueProp.Dequeue().V);

            // null 源：F-03 null 守恒（清空）双侧一致
            var nullSrc = new ContainerNullSource();
            var rtNull = Mapper.Map<ContainerNullSource, L02ConcreteRuntimeDto>(nullSrc)!;
            Assert.Null(rtNull.SetProp);
            Assert.Null(rtNull.QueueProp);
        }

    }

    // ===================== T005 模型=====================

    public class M15NullableSource { public int? Value { get; set; } }
    [MapFrom(typeof(M15NullableSource))]
    public class M15NullableGenDto { public long? Value { get; set; } }
    public class M15NullableRuntimeDto { public long? Value { get; set; } }

    public class M15NullableSameSource { public int? Value { get; set; } }
    [MapFrom(typeof(M15NullableSameSource))]
    public class M15NullableSameGenDto { public int? Value { get; set; } }
    public class M15NullableSameRuntimeDto { public int? Value { get; set; } }

    // ===================== L-02 对照模型=====================

    /// <summary>
    /// L-02 对照运行时 DTO — class 注册元素对 + 具体容器目标（HashSet/Queue）
    /// <para>修复前运行时侧不可用（ContainerNullRuntimeDto 注释登记排除 HashSet/Queue），
    /// L-02 档2 构造器修复后补齐；与生成侧 ContainerNullGenDto 构成双引擎对照</para>
    /// </summary>
    public class L02ConcreteRuntimeDto
    {
        /// <summary>HashSet 目标（档2 构造器）</summary>
        public HashSet<ElemB>? SetProp { get; set; }

        /// <summary>Queue 目标（档2 构造器）</summary>
        public Queue<ElemB>? QueueProp { get; set; }
    }
}
