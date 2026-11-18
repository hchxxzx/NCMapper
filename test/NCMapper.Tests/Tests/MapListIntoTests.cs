using NCMapper;
using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// MapListInto 批量就地映射测试
    /// 覆盖：行为契约 C1-C10、四路径分派、测试矩阵（含 P1-3 路径 2 断言）、
    /// MapperBase.MapListInto 批量回退协议（ctx/递归防护进出一次性、null 语义、action）、
    /// 递归深度防护（B3 修复语义：异常后差值不残留）、AOT 标注存在性。
    /// <para>路径判据（哨兵委托技术）：RegisterGeneratedListInto/RegisterGeneratedUpdate 为公共槽位写入 API——
    /// 注册哨兵委托后断言其被/不被调用，即可区分实际命中的分派路径，无需触碰 private 的 GeneratedCache。</para>
    /// <para>全局状态（Pipeline/槽位/MaxRecursionDepth）每个测试内 try-finally 恢复；
    /// 本程序集已禁用测试并行（AssemblyInfo.cs）。</para>
    /// </summary>
    [Collection("DeepCopyGlobalState")]
    public class MapListIntoTests : IDisposable
    {
        private readonly int _originalMaxDepth;

        public MapListIntoTests()
        {
            _originalMaxDepth = Mapper.MaxRecursionDepth;
        }

        public void Dispose()
        {
            Mapper.MaxRecursionDepth = _originalMaxDepth;
            Mapper.Pipeline.Clear();
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static List<ListIntoFlatSource> BuildFlatSources(int count, bool withChild = false)
        {
            var list = new List<ListIntoFlatSource>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new ListIntoFlatSource
                {
                    Id = i,
                    Name = $"N{i}",
                    Active = i % 2 == 0,
                    Score = i * 1.5,
                    Child = withChild ? new ListIntoFlatChild { ChildId = i * 10, Label = $"L{i}" } : null
                });
            }
            return list;
        }

        private static ListIntoFlatDto[] BuildDestination(int length)
        {
            var arr = new ListIntoFlatDto[length];
            for (int i = 0; i < length; i++) arr[i] = new ListIntoFlatDto();
            return arr;
        }

        /// <summary>恢复 ListIntoAction 槽位为生成方法（哨兵测试后的还原）</summary>
        private static void RestoreListIntoSlot()
            => Mapper.RegisterGeneratedListInto<ListIntoFlatSource, ListIntoFlatDto>(
                (sources, destination, action) =>
                    NCMapper.Generated.GeneratedMapper.MapListInto_ListIntoFlatSource_To_ListIntoFlatDto(sources, destination, action));

        /// <summary>恢复 UpdateAction 槽位为生成方法（哨兵测试后的还原）</summary>
        private static void RestoreUpdateSlot()
            => Mapper.RegisterGeneratedUpdate<ListIntoFlatSource, ListIntoFlatDto>(
                (src, dst, action) =>
                    NCMapper.Generated.GeneratedMapper.Map_ListIntoFlatSource_To_ListIntoFlatDto_Update(src, dst, action));

        private static void AssertFlatDtoEquals(ListIntoFlatSource src, ListIntoFlatDto dst)
        {
            Assert.Equal(src.Id, dst.Id);
            Assert.Equal(src.Name, dst.Name);
            Assert.Equal(src.Active, dst.Active);
            Assert.Equal(src.Score, dst.Score);
            if (src.Child is null)
            {
                Assert.Null(dst.Child);
            }
            else
            {
                Assert.NotNull(dst.Child);
                Assert.Equal(src.Child!.ChildId, dst.Child!.ChildId);
                Assert.Equal(src.Child.Label, dst.Child.Label);
            }
        }

        // ================================================================
        // 语义边界（C1-C10，注册生成对 ListIntoFlatSource→ListIntoFlatDto）
        // ================================================================

        /// <summary>C1：destination==null 抛 ArgumentNullException（ParamName=destination）</summary>
        [Fact]
        public void C1_DestinationNull_ThrowsArgumentNullException()
        {
            var sources = BuildFlatSources(2);
            var ex = Assert.Throws<ArgumentNullException>(
                () => Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, null!));
            Assert.Equal("destination", ex.ParamName);
        }

        /// <summary>P1-2 组合（C1 优先）：sources==null 且 destination==null 仍抛 ArgumentNullException（不受 C8 豁免）</summary>
        [Fact]
        public void C1_C8_P12_SourcesAndDestinationBothNull_C1Wins()
        {
            List<ListIntoFlatSource>? sources = null;
            ListIntoFlatDto[]? destination = null;
            var ex = Assert.Throws<ArgumentNullException>(
                () => Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources!, destination!));
            Assert.Equal("destination", ex.ParamName);
        }

        /// <summary>C8：sources==null（destination 非 null）直接返回，不做任何填充</summary>
        [Fact]
        public void C8_SourcesNull_DestinationNonNull_ReturnsWithoutFill()
        {
            var destination = BuildDestination(3);
            destination[0].Id = 111;
            destination[2].Name = "keep";

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(null!, destination);

            Assert.Equal(111, destination[0].Id);
            Assert.Equal("keep", destination[2].Name);
            Assert.Equal(0, destination[1].Id);
        }

        /// <summary>C2：destination.Length &lt; sources.Count 抛 ArgumentException（消息含双方数值，ParamName=destination）</summary>
        [Fact]
        public void C2_LengthLessThanCount_ThrowsArgumentException()
        {
            var sources = BuildFlatSources(3);
            var destination = BuildDestination(2);
            var ex = Assert.Throws<ArgumentException>(
                () => Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination));
            Assert.Equal("destination", ex.ParamName);
            // ArgumentException.Message 自动附加 " (Parameter '...')" 后缀（ParamName 已在上一行断言）
            Assert.StartsWith("destination.Length (2) must be >= sources.Count (3)", ex.Message);
        }

        /// <summary>C3：destination.Length &gt; sources.Count 合法，超量槽位保持原值</summary>
        [Fact]
        public void C3_LengthGreaterThanCount_ExtraSlotsPreserved()
        {
            var sources = BuildFlatSources(3);
            var destination = BuildDestination(5);
            var extra3 = destination[3];
            var extra4 = destination[4];
            destination[3].Id = 333;
            destination[4].Name = "extra";

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);

            for (int i = 0; i < 3; i++) AssertFlatDtoEquals(sources[i], destination[i]);
            Assert.Same(extra3, destination[3]);
            Assert.Same(extra4, destination[4]);
            Assert.Equal(333, destination[3].Id);
            Assert.Equal("extra", destination[4].Name);
        }

        /// <summary>C4：sources[i]==null 时 destination[i] 保持原值（不抛）</summary>
        [Fact]
        public void C4_SourceElementNull_SlotPreserved_NoThrow()
        {
            var sources = BuildFlatSources(3);
            var kept = new ListIntoFlatDto { Id = 900 };
            var destination = new[] { new ListIntoFlatDto(), kept, new ListIntoFlatDto() };
            var list = new List<ListIntoFlatSource> { sources[0], null!, sources[2] };

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(list, destination);

            AssertFlatDtoEquals(sources[0], destination[0]);
            Assert.Same(kept, destination[1]);
            Assert.Equal(900, destination[1].Id);
            AssertFlatDtoEquals(sources[2], destination[2]);
        }

        /// <summary>C5：destination[i]==null（未预初始化）no-op 跳过、不抛；其余元素正常映射</summary>
        [Fact]
        public void C5_DestinationElementNull_NoOp_NoThrow()
        {
            var sources = BuildFlatSources(3);
            var destination = new[] { new ListIntoFlatDto(), null!, new ListIntoFlatDto() };

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);

            AssertFlatDtoEquals(sources[0], destination[0]);
            Assert.Null(destination[1]);
            AssertFlatDtoEquals(sources[2], destination[2]);
        }

        /// <summary>C6：未映射字段（Extra）复用数组时保留上轮残留；映射字段被覆盖</summary>
        [Fact]
        public void C6_UnmappedField_ResiduePreserved_MappedFieldsOverwritten()
        {
            var sources = BuildFlatSources(2);
            var destination = BuildDestination(2);
            destination[0].Extra = "residue";

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);
            AssertFlatDtoEquals(sources[0], destination[0]);
            Assert.Equal("residue", destination[0].Extra);

            // 第二轮：修改源值 + 修改残留 → 映射字段覆盖、残留仍在
            sources[0].Id = 900;
            destination[0].Extra = "residue2";
            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);
            Assert.Equal(900, destination[0].Id);
            Assert.Equal("residue2", destination[0].Extra);
        }

        /// <summary>C7：嵌套引用属性每轮新建（子对象引用跨轮不同），顶层对象复用（引用不变）</summary>
        [Fact]
        public void C7_NestedChild_NewInstanceEachRound_TopLevelReused()
        {
            var sources = BuildFlatSources(2, withChild: true);
            var destination = BuildDestination(2);

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);
            var firstRoundChild0 = destination[0].Child;
            var topRef0 = destination[0];
            Assert.NotNull(firstRoundChild0);

            // 第二轮：同槽位再映射 → 顶层引用不变，子对象引用新建
            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);

            Assert.Same(topRef0, destination[0]);
            Assert.NotNull(destination[0].Child);
            Assert.NotSame(firstRoundChild0, destination[0].Child);
            AssertFlatDtoEquals(sources[0], destination[0]);
        }

        /// <summary>C10：action 每元素映射后调用一次，参数 (src, dst) 正确，且映射已完成（action 内读到的已是映射值）</summary>
        [Fact]
        public void C10_Action_InvokedPerElement_AfterAssignment()
        {
            var sources = BuildFlatSources(3);
            var destination = BuildDestination(3);
            var invocations = new List<(int Id, int DstId, bool SamePair)>();

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination,
                (src, dst) => invocations.Add((src.Id, dst.Id, ReferenceEquals(sources[src.Id], src) && ReferenceEquals(destination[src.Id], dst))));

            Assert.Equal(3, invocations.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(i, invocations[i].Id);
                Assert.Equal(i, invocations[i].DstId);          // action 触发时映射已完成
                Assert.True(invocations[i].SamePair);
            }
        }

        // ================================================================
        // 路径覆盖（四路径，哨兵委托判据）
        // ================================================================

        /// <summary>路径 2：注册生成对 + 无 Pipeline → ListIntoAction 批量委托命中（哨兵被调用 1 次 = 批量单次调用）</summary>
        [Fact]
        public void Path2_ListIntoAction_Hit_BulkSingleInvocation()
        {
            int sentinelCalls = 0;
            try
            {
                Mapper.RegisterGeneratedListInto<ListIntoFlatSource, ListIntoFlatDto>(
                    (sources, destination, action) => { sentinelCalls++; for (int i = 0; i < sources.Count; i++) destination[i].Id = -999; });

                var sources = BuildFlatSources(3);
                var destination = BuildDestination(3);
                Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);

                Assert.Equal(1, sentinelCalls);                 // 批量委托单次调用（非逐元素）
                for (int i = 0; i < 3; i++) Assert.Equal(-999, destination[i].Id);
            }
            finally
            {
                RestoreListIntoSlot();
            }
        }

        /// <summary>P1-3 断言：action 非 null 时仍走路径 2（ListIntoAction 命中），不降级路径 3——槽位消费不依赖 action==null</summary>
        [Fact]
        public void Path2_P13_ActionNonNull_StillListIntoActionHit()
        {
            int sentinelCalls = 0;
            bool actionObservedNonNull = false;
            try
            {
                Mapper.RegisterGeneratedListInto<ListIntoFlatSource, ListIntoFlatDto>(
                    (sources, destination, action) => { sentinelCalls++; actionObservedNonNull = action is not null; });

                var sources = BuildFlatSources(2);
                var destination = BuildDestination(2);
                Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination, (src, dst) => { });

                Assert.Equal(1, sentinelCalls);                 // action 非 null 仍命中 ListIntoAction（无 action==null 前置条件）
                Assert.True(actionObservedNonNull);             // action 参数透传到批量委托
            }
            finally
            {
                RestoreListIntoSlot();
            }
        }

        /// <summary>路径 1（C9/P0-1）：Pipeline 启用 → 逐元素内联 UpdateAction + PostMap；批量委托（ListIntoAction）被跳过</summary>
        [Fact]
        public void Path1_PipelineEnabled_PerElementInlineUpdate_PostMapFires_BulkSkipped()
        {
            int updateCalls = 0;
            int listIntoCalls = 0;
            int postMapCalls = 0;
            try
            {
                Mapper.RegisterGeneratedUpdate<ListIntoFlatSource, ListIntoFlatDto>(
                    (src, dst, action) => { updateCalls++; dst.Id = -999; });
                Mapper.RegisterGeneratedListInto<ListIntoFlatSource, ListIntoFlatDto>(
                    (sources, destination, action) => listIntoCalls++);
                Mapper.Pipeline.AddPostMap<ListIntoFlatSource, ListIntoFlatDto>((src, dto) => { postMapCalls++; dto.Extra = "postmap"; });

                Assert.True(Mapper.Pipeline.IsEnabled);

                var sources = BuildFlatSources(3);
                var destination = BuildDestination(3);
                Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);

                Assert.Equal(3, updateCalls);                   // 逐元素内联 UpdateAction（非批量）
                Assert.Equal(3, postMapCalls);                  // 每元素 PostMap
                Assert.Equal(0, listIntoCalls);                 // 批量委托被跳过（P0-1）
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(-999, destination[i].Id);      // 哨兵 Update 生效 = 逐元素内联分发
                    Assert.Equal("postmap", destination[i].Extra);
                }
            }
            finally
            {
                Mapper.Pipeline.Clear();
                RestoreUpdateSlot();
                RestoreListIntoSlot();
            }
        }

        /// <summary>路径 3：ListIntoAction 注销（注册 null）→ UpdateAction 循环命中（逐元素哨兵计数）；还原真实 Update 后结果正确</summary>
        [Fact]
        public void Path3_ListIntoCleared_UpdateActionLoop_Used_ThenRealUpdateCorrect()
        {
            int updateCalls = 0;
            try
            {
                // 阶段 A：哨兵 Update — 证明逐元素 UpdateAction 循环
                Mapper.RegisterGeneratedListInto<ListIntoFlatSource, ListIntoFlatDto>(null);
                Mapper.RegisterGeneratedUpdate<ListIntoFlatSource, ListIntoFlatDto>(
                    (src, dst, action) => { updateCalls++; dst.Id = -999; });

                var sources = BuildFlatSources(3);
                var destination = BuildDestination(3);
                Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);

                Assert.Equal(3, updateCalls);                   // 逐元素（循环形态）
                for (int i = 0; i < 3; i++) Assert.Equal(-999, destination[i].Id);

                // 阶段 B：还原真实 Update（ListInto 仍注销）— 路径 3 全字段正确
                RestoreUpdateSlot();
                var destination2 = BuildDestination(3);
                Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination2);
                for (int i = 0; i < 3; i++) AssertFlatDtoEquals(sources[i], destination2[i]);
            }
            finally
            {
                RestoreListIntoSlot();
                RestoreUpdateSlot();
            }
        }

        /// <summary>路径 4：未注册类型对（无 [MapFrom]）→ MapperBase.MapListInto 批量回退，结果正确 + action + C4 null 源跳过</summary>
        [Fact]
        public void Path4_UnregisteredPair_RuntimeFallback_Correct()
        {
            var list = new List<UnregListSource>
            {
                new() { Id = 1, Name = "a" },
                null!,
                new() { Id = 3, Name = null }
            };
            var kept = new UnregListDto { Id = 888 };
            var destination = new[] { new UnregListDto(), kept, new UnregListDto() };
            int actions = 0;

            Mapper.MapListInto<UnregListSource, UnregListDto>(list, destination, (src, dst) => actions++);

            Assert.Equal(1, destination[0].Id);
            Assert.Equal("a", destination[0].Name);
            Assert.Same(kept, destination[1]);                   // C4：null 源槽位保持原值
            Assert.Equal(888, destination[1].Id);
            Assert.Equal(3, destination[2].Id);
            Assert.Null(destination[2].Name);
            Assert.Equal(2, actions);                            // null 源元素不触发 action
        }

        // ================================================================
        // 与 MapList 结果一致性（同源数据逐字段断言）
        // ================================================================

        /// <summary>一致性：MapList vs MapListInto 扁平对（50 元素全字段；MapListInto 不替换顶层引用）</summary>
        [Fact]
        public void Consistency_MapListVsMapListInto_Flat_AllFieldsEqual()
        {
            var sources = BuildFlatSources(50);
            var expected = Mapper.MapList<ListIntoFlatSource, ListIntoFlatDto>(sources)!;
            var destination = BuildDestination(50);
            var preAllocated = (ListIntoFlatDto[])destination.Clone();

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);

            for (int i = 0; i < 50; i++)
            {
                Assert.Same(preAllocated[i], destination[i]);   // 预分配对象未被替换（就地填充）
                AssertFlatDtoEquals(sources[i], destination[i]);
                Assert.Equal(expected[i]!.Id, destination[i].Id);
                Assert.Equal(expected[i].Name, destination[i].Name);
                Assert.Equal(expected[i].Active, destination[i].Active);
                Assert.Equal(expected[i].Score, destination[i].Score);
            }
        }

        /// <summary>一致性：MapList vs MapListInto 嵌套对（子对象逐字段一致；顶层复用、子对象每轮新建）</summary>
        [Fact]
        public void Consistency_MapListVsMapListInto_Nested_DeepCompare()
        {
            var sources = BuildFlatSources(30, withChild: true);
            var expected = Mapper.MapList<ListIntoFlatSource, ListIntoFlatDto>(sources)!;
            var destination = BuildDestination(30);

            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);
            var topRefs = (ListIntoFlatDto[])destination.Clone();
            Mapper.MapListInto<ListIntoFlatSource, ListIntoFlatDto>(sources, destination);   // 第二轮：值应不变

            for (int i = 0; i < 30; i++)
            {
                Assert.Same(topRefs[i], destination[i]);       // 顶层跨轮复用
                Assert.NotNull(destination[i].Child);
                Assert.NotNull(expected[i]!.Child);
                Assert.Equal(expected[i].Child!.ChildId, destination[i].Child!.ChildId);
                Assert.Equal(expected[i].Child!.Label, destination[i].Child!.Label);
            }
        }

        // ================================================================
        // MapperBase.MapListInto 批量回退协议（未注册对直调）
        // ================================================================

        /// <summary>MapperBase 直调：结果正确 + action 每元素一次</summary>
        [Fact]
        public void MapperBase_MapListInto_Direct_ResultAndAction()
        {
            var sources = new List<UnregListSource>
            {
                new() { Id = 1, Name = "a" },
                new() { Id = 2, Name = null },
                new() { Id = 3, Name = "c" }
            };
            var destination = new[] { new UnregListDto(), new UnregListDto(), new UnregListDto() };
            int actions = 0;

            MapperBase<UnregListSource, UnregListDto>.MapListInto(sources, destination, (src, dst) => actions++);

            Assert.Equal(1, destination[0].Id);
            Assert.Equal("a", destination[0].Name);
            Assert.Equal(2, destination[1].Id);
            Assert.Null(destination[1].Name);
            Assert.Equal(3, destination[2].Id);
            Assert.Equal(3, actions);
        }

        /// <summary>MapperBase 直调：sources/destination 为 null 防御性返回（不抛、不填充）</summary>
        [Fact]
        public void MapperBase_MapListInto_DefensiveNull_ReturnsWithoutSideEffects()
        {
            var destination = new[] { new UnregListDto { Id = 555 } };

            MapperBase<UnregListSource, UnregListDto>.MapListInto(null!, destination);
            Assert.Equal(555, destination[0].Id);

            var sources = new List<UnregListSource> { new() { Id = 1 } };
            MapperBase<UnregListSource, UnregListDto>.MapListInto(sources, null!);
            Assert.Equal(555, destination[0].Id);               // 无异常即通过
        }

        /// <summary>MapperBase 直调：null 语义 C4（src null 槽位保持）/ C5（dst null no-op）</summary>
        [Fact]
        public void MapperBase_MapListInto_NullElements_C4C5()
        {
            var list = new List<UnregListSource> { new() { Id = 1 }, null!, new() { Id = 3 } };
            var kept = new UnregListDto { Id = 777 };
            var destination = new[] { new UnregListDto(), kept, null! };

            MapperBase<UnregListSource, UnregListDto>.MapListInto(list, destination);

            Assert.Equal(1, destination[0].Id);
            Assert.Same(kept, destination[1]);
            Assert.Equal(777, destination[1].Id);
            Assert.Null(destination[2]);
        }

        /// <summary>MapperBase 协议：ctx/递归防护进出一次性（批量级）— 深层链批量映射后 Depth 无残留，第二轮结果一致</summary>
        [Fact]
        public void MapperBase_MapListInto_CtxProtocol_NoResidue_SecondBatchIdentical()
        {
            var batch = new List<StressNode>
            {
                DeepNestingStressTests.BuildDeepNode(8),
                DeepNestingStressTests.BuildDeepNode(8),
                DeepNestingStressTests.BuildDeepNode(8)
            };
            var destination = new[] { new StressNodeDto(), new StressNodeDto(), new StressNodeDto() };

            Mapper.MapListInto<StressNode, StressNodeDto>(batch, destination);

            for (int e = 0; e < 3; e++)
            {
                var src = batch[e];
                var dst = destination[e];
                for (int level = 8; level >= 0; level--)
                {
                    Assert.NotNull(dst);
                    Assert.Equal(level, dst.Id);
                    Assert.Equal($"L{level}", dst.Name);
                    src = src.Child!;
                    dst = dst.Child!;
                }
                Assert.Null(dst);                               // 叶子 Child 为 null
            }

            // ctx 无残留（一次性进出协议的可观测后果）
            Assert.Equal(0, Mapper.GetOrCreateDeepCopyCtx().Depth);

            // 第二轮：状态无泄漏，结果一致
            var destination2 = new[] { new StressNodeDto(), new StressNodeDto(), new StressNodeDto() };
            Mapper.MapListInto<StressNode, StressNodeDto>(batch, destination2);
            for (int e = 0; e < 3; e++)
            {
                var d1 = destination[e];
                var d2 = destination2[e];
                for (int level = 8; level >= 0; level--)
                {
                    Assert.Equal(d1!.Id, d2!.Id);
                    Assert.Equal(d1.Name, d2.Name);
                    d1 = d1.Child!;
                    d2 = d2.Child!;
                }
            }
        }

        /// <summary>递归防护（B3 语义）：批量含循环引用元素抛异常（MapException），异常后差值不残留、后续批量正常</summary>
        [Fact]
        public void Recursion_Batch_CircularReference_Throws_ThenRecover()
        {
            var a = new StressNode { Id = 1, Name = "A" };
            a.Child = a;                                        // 自引用
            var batch = new List<StressNode> { a, new StressNode { Id = 2, Name = "B" } };
            var destination = new[] { new StressNodeDto(), new StressNodeDto() };

            Assert.ThrowsAny<InvalidOperationException>(
                () => Mapper.MapListInto<StressNode, StressNodeDto>(batch, destination));

            // B3：异常后计数器自清洁 — ctx 无残留 + 后续正常批量映射
            Assert.Equal(0, Mapper.GetOrCreateDeepCopyCtx().Depth);
            var good = new List<StressNode> { DeepNestingStressTests.BuildDeepNode(3) };
            var goodDst = new[] { new StressNodeDto() };
            Mapper.MapListInto<StressNode, StressNodeDto>(good, goodDst);
            Assert.Equal(3, goodDst[0].Id);
            Assert.Equal("L3", goodDst[0].Name);
            Assert.NotNull(goodDst[0].Child?.Child?.Child);
            Assert.Equal(0, goodDst[0].Child!.Child!.Child!.Id);
        }

        /// <summary>
        /// 递归防护（一次性进出协议）：MaxRecursionDepth 收紧至 2 时，7 层链 ×3 的批量映射不误触发深度守卫——
        /// ctx/递归防护在批量级进出一次（MapperBase.cs:319/:335，非逐元素），有限深度链的活跃差值恒定在限制内；
        /// 若协议退化为逐元素/逐层进出，此批量将误触发抛异常。批量后 ctx 无残留，恢复后映射正常。
        /// <para>（循环引用的批量抛异常由 Recursion_Batch_CircularReference_Throws_ThenRecover 经 CycleCache 防护覆盖）</para>
        /// </summary>
        [Fact]
        public void Recursion_LowDepth_DeepBatch_NoFalseTrigger_OneShotProtocol()
        {
            var original = Mapper.MaxRecursionDepth;
            try
            {
                Mapper.MaxRecursionDepth = 2;

                // 7 层链 ×3 — 深链批量在收紧限制下仍完整映射（不误触发）
                var batch = new List<StressNode>
                {
                    DeepNestingStressTests.BuildDeepNode(7),
                    DeepNestingStressTests.BuildDeepNode(7),
                    DeepNestingStressTests.BuildDeepNode(7)
                };
                var destination = new[] { new StressNodeDto(), new StressNodeDto(), new StressNodeDto() };
                Mapper.MapListInto<StressNode, StressNodeDto>(batch, destination);

                for (int e = 0; e < 3; e++)
                {
                    var dst = destination[e];
                    for (int level = 7; level >= 0; level--)
                    {
                        Assert.NotNull(dst);
                        Assert.Equal(level, dst.Id);
                        dst = dst.Child!;
                    }
                    Assert.Null(dst);
                }

                // 一次性进出协议的可观测后果：批量后 ctx 无残留
                Assert.Equal(0, Mapper.GetOrCreateDeepCopyCtx().Depth);
            }
            finally
            {
                Mapper.MaxRecursionDepth = original;
            }

            // 恢复后正常映射
            var flat = new FullTypeEntity { IntProp = 77, StringProp = "Recover" };
            Assert.Equal(77, Mapper.Map<FullTypeEntity, SameTypeDto>(flat)!.IntProp);
            Assert.Equal(0, Mapper.GetOrCreateDeepCopyCtx().Depth);
        }

        // ================================================================
        // AOT 标注存在性（属性扫描）
        // ================================================================

        /// <summary>AOT：入口与 MapperBase 批量回退均带 RequiresDynamicCode + RequiresUnreferencedCode</summary>
        [Fact]
        public void Aot_Attributes_Present_OnEntryAndMapperBase()
        {
            var entry = typeof(Mapper).GetMethod(nameof(Mapper.MapListInto))!;
            Assert.True(HasAttribute(entry, "RequiresDynamicCodeAttribute"), "入口缺 RequiresDynamicCode");
            Assert.True(HasAttribute(entry, "RequiresUnreferencedCodeAttribute"), "入口缺 RequiresUnreferencedCode");

            var fallback = typeof(MapperBase<UnregListSource, UnregListDto>)
                .GetMethod(nameof(MapperBase<UnregListSource, UnregListDto>.MapListInto))!;
            Assert.True(HasAttribute(fallback, "RequiresDynamicCodeAttribute"), "MapperBase 缺 RequiresDynamicCode");
            Assert.True(HasAttribute(fallback, "RequiresUnreferencedCodeAttribute"), "MapperBase 缺 RequiresUnreferencedCode");
        }

        private static bool HasAttribute(System.Reflection.MethodInfo method, string attributeName)
            => method.GetCustomAttributesData().Any(a => a.AttributeType.Name == attributeName);
    }
}
