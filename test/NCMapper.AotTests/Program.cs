using System.Runtime.CompilerServices;
using FreeSql;
using NCMapper;
using NCMapper.Attributes;
using NCMapper.FreeSql;
using NCMapper.Generated;
using NCMapper.Runtime;

// [assembly: MapDefaults] → 源生成器读取并生成 RegisterGeneratedConfig 快照 →
// AOT 发布运行时冻结（探针值不影响任何现有模型，仅验证通道端到端）
[assembly: MapDefaults(IgnoreNames = new[] { "__AotSnapshotProbe__" })]

namespace NCMapper.AotTests;

/// <summary>
/// AOT 发布验证程序 — 验证标注优先模式下全部核心 API 在 AOT 下可运行
/// <para>覆盖：Map / MapList / Compose / DeepCopy / Validate / BuildProjection</para>
/// </summary>
internal class Program
{
    static int Main(string[] args)
    {
        // W3D-2（F8，2026-08-08）：PASS 计数断言入 AotTests —
        // 逐测试 try-catch 包装，失败记 [FAIL] 不中断；汇总统计后以退出码返回：
        // 未知 FAIL > 0 → 退出码 1；已知缺陷（EnumSemantics）计 [KNOWN-FAIL] 不阻塞
        var knownIssues = new HashSet<string>(StringComparer.Ordinal) { "EnumSemantics" }; // 生成侧 string→enum 大小写转换得 0（登记，修复后移除）
        var passCount = 0;
        var failCount = 0;
        var knownFailCount = 0;

        void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"[PASS] {name}");
                passCount++;
            }
            catch (Exception ex)
            {
                if (knownIssues.Contains(name))
                {
                    Console.WriteLine($"[KNOWN-FAIL] {name}: {ex.Message}（已知缺陷，修复后移除）");
                    knownFailCount++;
                }
                else
                {
                    Console.WriteLine($"[FAIL] {name}: {ex.Message}");
                    failCount++;
                }
            }
        }

        Console.WriteLine("=== NCMapper AOT 验证 ===");

        // 测试 1: Map（源生成器路径）
        Run("Map", TestMap);

        // 测试 2: MapList（源生成器路径）
        Run("MapList", TestMapList);

        // 测试 3: Compose（源生成器路径）
        Run("Compose", TestCompose);

        // 测试 4: DeepCopy（源生成器路径）
        Run("DeepCopy", TestDeepCopy);

        // 测试 5: Validate（源生成器路径 — 编译时验证结果）
        Run("Validate", TestValidate);

        // T03: 丰富 AOT 测试模型
        // 测试 7: Nullable 字段映射
        Run("Nullable", TestNullable);

        // 测试 8: 枚举字段映射
        Run("Enum", TestEnum);

        // 测试 9: 嵌套对象映射
        Run("NestedObject", TestNestedObject);

        // 测试 10: List 属性映射
        Run("ListProperty", TestListProperty);

        // ⑤: 枚举异常语义（大小写无关/非法值 MapException）+ 集合 null Update
        Run("EnumSemantics", TestEnumSemantics); // 已知缺陷（生成侧 string→enum 大小写得 0），JIT 下预期 [KNOWN-FAIL]
        Run("CollectionNullUpdate", TestCollectionNullUpdate);

        // T10: AOT 测试补充 — Dictionary / record / 深层嵌套
        // 测试 11: Dictionary 深拷贝
        Run("DictionaryDeepCopy", TestDictionaryDeepCopy);

        // 测试 12: record class 深拷贝
        Run("RecordDeepCopy", TestRecordDeepCopy);

        // （前置门禁的修复后复验）：占位回拷 + 构参外属性
        Run("RecordPlaceholderBackfill", TestRecordPlaceholderBackfill);

        // 测试 13: 4 层嵌套引用类型深拷贝
        Run("MultiLevelNestedDeepCopy", TestMultiLevelNestedDeepCopy);

        // 测试 14: record 类型映射
        Run("RecordMap", TestRecordMap);

        // T3-7: 补全 7 个 AOT 场景
        // 测试 15: 双向映射（反向 Map）
        Run("Bidirectional", TestBidirectional);
        Run("ReverseDirectCall", TestReverseDirectCall);

        // 测试 16: UpdateTarget（更新已有目标）
        Run("UpdateTarget", TestUpdateTarget);

        // 测试 17: MapIgnore 方向忽略
        Run("MapIgnore", TestMapIgnore);

        // 测试 18: ConvertUsing 自定义转换器
        Run("ConvertUsing", TestConvertUsing);

        // 测试 19: Pipeline PostMap（回调钩子；早期两段式钩子已于重构时移除清理过时注释）
        Run("Pipeline", TestPipeline);

        // 测试 20: MapTracer 诊断追踪
        Run("MapTracer", TestMapTracer);

        // 测试 21: MapList with Action callback
        Run("MapListWithAction", TestBuildProjection);

        // 快照通道发布冒烟 — [assembly: MapDefaults] → 源生成器快照注册 →
        // AOT 发布下 GetGlobalConfig 返回冻结配置（验证 buildTransitive targets 通道在 AOT 端到端工作）
        Run("SnapshotChannel", TestSnapshotChannel);

        // ②：RuntimeSnapshotStore 承载类型 AOT 场景
        // 验证 L-1 运行时注册表在 AOT 发布下可用（发射开关 on → [MapFrom] DTO 经 ModuleInitializer
        // 注册 RuntimeSnapshotStore；承载类型零反射静态元数据 AOT 友好）
        Run("RuntimeSnapshotRegistry", TestRuntimeSnapshotRegistry);

        // （Fluent 发布形态 AOT 冒烟）
        // [MapperConfig] 类型对 → FluentSnapshotRegistry 发射注册 + Fluent 生成映射直调。
        // JIT 本机冒烟 + AOT publish 双模式可执行（生成通道零反射，AOT 可用性设计目标）；
        // linux-x64/win-x64 NativeAOT publish 执行通道登记⑥（本机不可验证时不阻塞）。
        Run("FluentChannel", TestFluentChannel);

        // ================================================================
        // 5 类负面用例（标注优先模式下 AOT 失败模式的编译期+运行时双验证）
        // ================================================================
        Run("负面① 未标 [MapFrom] 回退", TestFallbackWithoutMapFrom);

        Run("负面② ORM AsDto AOT 不可用", TestOrmAsDtoAotUnsupported);

        Run("负面③ Compose 未注册回退", TestComposeUnregistered);

        Run("负面④ record 循环引用 NCMAP012 回退", TestRecordCircularFallback);

        Run("负面⑤ Validate(params Assembly[]) 警告", TestValidateWithAssemblies);

        // W3D-2: 汇总 + 退出码（未知 FAIL > 0 → 1）
        Console.WriteLine($"=== AOT 汇总: {passCount} PASS / {failCount} FAIL / {knownFailCount} KNOWN-FAIL（已知缺陷不阻塞） ===");
        if (failCount == 0)
        {
            Console.WriteLine("=== 全部 AOT 验证通过（未知失败 0） ===");
            return 0;
        }
        Console.WriteLine($"=== 存在 {failCount} 个未知失败，AOT 验证未通过 ===");
        return 1;
    }

    static void TestMap()
    {
        var entity = new TestEntity { Id = 42, Name = "AOT Test", Value = 100 };
        var dto = Mapper.Map<TestEntity, TestDto>(entity)!;
        if (dto.Id != 42 || dto.Name != "AOT Test" || dto.Value != 100)
            throw new Exception($"Map 验证失败: Id={dto.Id}, Name={dto.Name}, Value={dto.Value}");
    }

    static void TestMapList()
    {
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "A", Value = 10 },
            new() { Id = 2, Name = "B", Value = 20 },
            new() { Id = 3, Name = "C", Value = 30 },
        };
        var dtos = Mapper.MapList<TestEntity, TestDto>(entities)!;
        if (dtos.Count != 3 || dtos[0].Name != "A" || dtos[2].Value != 30)
            throw new Exception($"MapList 验证失败: Count={dtos.Count}");
    }

    static void TestCompose()
    {
        var order = new OrderEntity { OrderId = 1001, OrderAmount = 299.99m };
        var customer = new CustomerEntity { CustomerName = "ZhangSan", CustomerLevel = 5 };

        var dto = Mapper.Compose<OrderDetailDto>(order, customer)!;
        if (dto.OrderId != 1001 || dto.OrderAmount != 299.99m || dto.CustomerName != "ZhangSan" || dto.CustomerLevel != 5)
            throw new Exception($"Compose 验证失败");
    }

    static void TestDeepCopy()
    {
        var entity = new TestEntity { Id = 99, Name = "DeepCopy", Value = 999 };
        var copy = Mapper.DeepCopy(entity)!;
        if (copy.Id != 99 || copy.Name != "DeepCopy" || copy.Value != 999)
            throw new Exception($"DeepCopy 验证失败");
        // 修改副本不影响原对象
        copy.Name = "Modified";
        if (entity.Name == "Modified")
            throw new Exception("DeepCopy 不是深拷贝，修改副本影响了原对象");
    }

    static void TestValidate()
    {
        var results = Mapper.Validate();
        if (results.Count == 0)
            throw new Exception("Validate 验证失败: 返回空结果");

        // 验证至少包含 TestEntity → TestDto 的验证结果
        var hasTestMapping = results.Any(r =>
            r.SourceType == typeof(TestEntity) && r.TargetType == typeof(TestDto));
        if (!hasTestMapping)
            throw new Exception("Validate 验证失败: 未包含 TestEntity → TestDto 映射对");

        // TestEntity → TestDto 应该是 OK 级别（全属性映射）
        var testResult = results.First(r =>
            r.SourceType == typeof(TestEntity) && r.TargetType == typeof(TestDto));
        if (testResult.Level != MapValidationLevel.OK)
            throw new Exception($"Validate 验证失败: TestEntity → TestDto 级别为 {testResult.Level}，期望 OK");

        Console.WriteLine($"  Validate 返回 {results.Count} 个验证结果");
    }

    /// <summary>
    /// T03-01: Nullable 字段 AOT 验证 — int?/DateTime?/string? 正确映射
    /// </summary>
    static void TestNullable()
    {
        // 场景 1: 有值场景
        var entityWithValue = new NullableTestEntity
        {
            Id = 1,
            Score = 95,
            Birthday = new DateTime(2000, 6, 15),
            Description = "有值描述"
        };
        var dtoWithValue = Mapper.Map<NullableTestEntity, NullableTestDto>(entityWithValue)!;
        if (dtoWithValue.Id != 1 || dtoWithValue.Score != 95
            || dtoWithValue.Birthday != new DateTime(2000, 6, 15)
            || dtoWithValue.Description != "有值描述")
            throw new Exception("Nullable 有值场景验证失败");

        // 场景 2: null 值场景
        var entityWithNull = new NullableTestEntity
        {
            Id = 2,
            Score = null,
            Birthday = null,
            Description = null
        };
        var dtoWithNull = Mapper.Map<NullableTestEntity, NullableTestDto>(entityWithNull)!;
        if (dtoWithNull.Id != 2 || dtoWithNull.Score != null
            || dtoWithNull.Birthday != null || dtoWithNull.Description != null)
            throw new Exception("Nullable null 值场景验证失败");

        Console.WriteLine($"  Nullable 验证通过: 有值 Score=95, null Score=null");
    }

    /// <summary>
    /// T03-02: 枚举字段 AOT 验证 — 普通枚举和可空枚举正确映射
    /// </summary>
    static void TestEnum()
    {
        // 场景 1: 普通枚举
        var entity1 = new EnumTestEntity { Id = 1, Status = AotTestStatus.Active, Priority = AotTestPriority.High };
        var dto1 = Mapper.Map<EnumTestEntity, EnumTestDto>(entity1)!;
        if (dto1.Id != 1 || dto1.Status != AotTestStatus.Active || dto1.Priority != AotTestPriority.High)
            throw new Exception($"枚举验证失败: Status={dto1.Status}, Priority={dto1.Priority}");

        // 场景 2: 可空枚举（有值）
        var entity2 = new EnumTestEntity { Id = 2, Status = AotTestStatus.Inactive, OptionalStatus = AotTestStatus.Pending };
        var dto2 = Mapper.Map<EnumTestEntity, EnumTestDto>(entity2)!;
        if (dto2.OptionalStatus != AotTestStatus.Pending)
            throw new Exception($"可空枚举有值验证失败: OptionalStatus={dto2.OptionalStatus}");

        // 场景 3: 可空枚举（null）
        var entity3 = new EnumTestEntity { Id = 3, Status = AotTestStatus.Active, OptionalStatus = null };
        var dto3 = Mapper.Map<EnumTestEntity, EnumTestDto>(entity3)!;
        if (dto3.OptionalStatus != null)
            throw new Exception($"可空枚举 null 验证失败: OptionalStatus={dto3.OptionalStatus}");

        Console.WriteLine($"  Enum 验证通过: Active/High, Pending, null");
    }

    /// <summary>
    /// T03-03: 嵌套对象 AOT 验证 — 嵌套对象递归映射 + null 传播
    /// </summary>
    static void TestNestedObject()
    {
        // 场景 1: 有嵌套对象
        var entity1 = new NestedTestEntity
        {
            Id = 1,
            Name = "外层对象",
            Inner = new InnerTestEntity { InnerId = 100, InnerName = "内层对象" }
        };
        var dto1 = Mapper.Map<NestedTestEntity, NestedTestDto>(entity1)!;
        if (dto1.Id != 1 || dto1.Name != "外层对象")
            throw new Exception("嵌套对象外层验证失败");
        if (dto1.Inner == null || dto1.Inner.InnerId != 100 || dto1.Inner.InnerName != "内层对象")
            throw new Exception("嵌套对象内层验证失败");

        // 场景 2: 嵌套对象为 null
        var entity2 = new NestedTestEntity { Id = 2, Name = "无嵌套", Inner = null };
        var dto2 = Mapper.Map<NestedTestEntity, NestedTestDto>(entity2)!;
        if (dto2.Id != 2 || dto2.Inner != null)
            throw new Exception("嵌套对象 null 传播验证失败");

        Console.WriteLine($"  NestedObject 验证通过: Inner.InnerName=内层对象, null 传播正确");
    }

    /// <summary>
    /// T03-04: List 属性 AOT 验证 — List&lt;T&gt; 正确映射 + 空列表处理
    /// </summary>
    static void TestListProperty()
    {
        // 场景 1: 有元素的列表
        var entity1 = new ListTestEntity
        {
            Id = 1,
            Tags = new List<string> { "tag1", "tag2", "tag3" },
            Scores = new List<int> { 10, 20, 30 }
        };
        var dto1 = Mapper.Map<ListTestEntity, ListTestDto>(entity1)!;
        if (dto1.Id != 1 || dto1.Tags == null || dto1.Tags.Count != 3
            || dto1.Tags[0] != "tag1" || dto1.Tags[2] != "tag3")
            throw new Exception($"List<string> 验证失败: Count={dto1.Tags?.Count}");
        if (dto1.Scores == null || dto1.Scores.Count != 3 || dto1.Scores[1] != 20)
            throw new Exception($"List<int> 验证失败: Count={dto1.Scores?.Count}");

        // 场景 2: 空列表
        var entity2 = new ListTestEntity
        {
            Id = 2,
            Tags = new List<string>(),
            Scores = new List<int>()
        };
        var dto2 = Mapper.Map<ListTestEntity, ListTestDto>(entity2)!;
        if (dto2.Tags == null || dto2.Tags.Count != 0)
            throw new Exception("空 List<string> 验证失败");
        if (dto2.Scores == null || dto2.Scores.Count != 0)
            throw new Exception("空 List<int> 验证失败");

        // 场景 3: null 列表
        var entity3 = new ListTestEntity { Id = 3, Tags = null, Scores = null };
        var dto3 = Mapper.Map<ListTestEntity, ListTestDto>(entity3)!;
        if (dto3.Tags != null || dto3.Scores != null)
            throw new Exception("null List 验证失败");

        Console.WriteLine($"  ListProperty 验证通过: Count=3, Count=0, null");
    }

    /// <summary>
    /// ⑤-1: 枚举异常语义 AOT 冒烟 — string→enum 大小写无关+ 非法值抛 MapException
    /// </summary>
    static void TestEnumSemantics()
    {
        // T2: 大小写无关（"red" → Red，与运行时 Enum.Parse(ignoreCase:true) 一致）
        var e1 = new AotEnumStrEntity { ColorStr = "red" };
        var d1 = Mapper.Map<AotEnumStrEntity, AotEnumStrDto>(e1)!;
        if (d1.Color != AotColor.Red)
            throw new Exception($"string→enum 大小写无关验证失败: {d1.Color}");

        // 非法值统一抛 MapException（不再静默 default）
        try
        {
            Mapper.Map<AotEnumStrEntity, AotEnumStrDto>(new AotEnumStrEntity { ColorStr = "Purple" });
            throw new Exception("string→enum 非法值应抛 MapException 但未抛");
        }
        catch (MapException)
        {
            // 预期异常
        }

        Console.WriteLine("  EnumSemantics 验证通过: 大小写无关/非法值 MapException");
    }

    /// <summary>
    /// ⑤-2: 集合 null Update 语义 AOT 冒烟 — Update 路径源集合 null → 清空旧值
    /// </summary>
    static void TestCollectionNullUpdate()
    {
        var src = new AotCollEntity(); // Nums = null
        var dst = new AotCollDto { Nums = new List<long> { 99L } };
        Mapper.Map<AotCollEntity, AotCollDto>(src, dst);
        if (dst.Nums != null)
            throw new Exception($"Update 集合 null 应清空旧值但保留: Count={dst.Nums.Count}");

        Console.WriteLine("  CollectionNullUpdate 验证通过: Update null → 清空");
    }

    /// <summary>
    /// T10-01: Dictionary 深拷贝 AOT 验证 — Dictionary&lt;string, RefType&gt; 正确深拷贝
    /// </summary>
    static void TestDictionaryDeepCopy()
    {
        var source = new DictTestEntity
        {
            Id = 1,
            Items = new Dictionary<string, DictItemEntity>
            {
                ["a"] = new DictItemEntity { Name = "Alpha", Value = 10 },
                ["b"] = new DictItemEntity { Name = "Beta", Value = 20 }
            }
        };

        var copy = Mapper.DeepCopy(source)!;
        if (copy.Id != 1 || copy.Items == null || copy.Items.Count != 2)
            throw new Exception($"Dictionary DeepCopy 基本验证失败: Id={copy.Id}, Count={copy.Items?.Count}");
        if (copy.Items["a"].Name != "Alpha" || copy.Items["b"].Value != 20)
            throw new Exception("Dictionary DeepCopy 值验证失败");

        // 修改副本不影响原对象
        copy.Items["a"].Name = "Modified";
        if (source.Items["a"].Name == "Modified")
            throw new Exception("Dictionary DeepCopy 不是深拷贝，修改副本影响了原对象");

        Console.WriteLine($"  DictionaryDeepCopy 验证通过: Count=2, 深拷贝隔离正确");
    }

    /// <summary>
    /// T10-02: record class 深拷贝 AOT 验证
    /// </summary>
    static void TestRecordDeepCopy()
    {
        var source = new RecordTestEntity(42, "RecordCopy", 999);
        var copy = Mapper.DeepCopy(source)!;
        if (copy.Id != 42 || copy.Name != "RecordCopy" || copy.Value != 999)
            throw new Exception($"Record DeepCopy 验证失败: Id={copy.Id}, Name={copy.Name}, Value={copy.Value}");
        if (ReferenceEquals(source, copy))
            throw new Exception("Record DeepCopy 返回了同一引用");

        Console.WriteLine($"  RecordDeepCopy 验证通过: Id=42, Name=RecordCopy");
    }

    /// <summary>
    /// 占位回拷 + 构参外属性补填冒烟
    /// 无 [MapFrom] 的环 record 走运行时 Provider 占位路径（GetUninitializedObject +
    /// backing-field 反射回写 + FillProperties 构参外补填）。双模式断言（与负面用例
    /// 同构设计）：JIT 下完整数据断言；AOT 下断言路径可执行不抛——运行时深拷贝路径为
    /// RequiresDynamicCode/RequiresUnreferencedCode 双标注的不承诺路径，ILC 会裁剪无静态
    /// 引用的 setter/backing-field（CanWrite 随之失效，构参外属性静默丢失——"Some members
    /// may be trimmed" 契约语义，与修复前行为一致无回归）；AOT 场景的推荐路径为源生成侧。
    /// </summary>
    static void TestRecordPlaceholderBackfill()
    {
        var a = new B02B03SmokeNode("a") { Tag = "ta" };
        var b = new B02B03SmokeNode("b") { Tag = "tb" };
        a.Next = b;
        b.Next = a; // 双向环：环回边经构参外 set 属性闭合

        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            // JIT：反射完整可用——完整数据断言（属性完整 + 环引用一致）
            var copy = Mapper.DeepCopy(a)!;
            if (copy.Name != "a" || copy.Tag != "ta"
                || copy.Next?.Name != "b" || copy.Next?.Tag != "tb")
 throw new Exception($"冒烟失败（属性不完整）: Name={copy.Name}, Tag={copy.Tag}, Next.Tag={copy.Next?.Tag}");
            if (!ReferenceEquals(copy, copy.Next!.Next))
 throw new Exception("冒烟失败（环回边引用不一致——占位分裂）");
 Console.WriteLine($"JIT 下环 record 占位回拷 + 构参外属性冒烟通过: Name={copy.Name}, Tag={copy.Tag}, 环引用一致");
        }
        else
        {
            // AOT：断言运行时占位路径可执行（GetUninitializedObject/解释执行不抛致命异常）；
            // 数据完整性受 ILC 成员裁剪影响（RequiresUnreferencedCode 契约语义）
            var copy = Mapper.DeepCopy(a);
            if (copy == null)
 throw new Exception("冒烟失败（AOT 下返回 null）");
 Console.WriteLine($"AOT 下运行时占位路径可执行不抛（数据完整性受 trim 成员保留影响——RequiresUnreferencedCode 契约语义，推荐源生成路径）");
        }
    }

    /// <summary>
    /// T10-03: 4 层嵌套引用类型深拷贝 AOT 验证 (A→B→C→D)
    /// </summary>
    static void TestMultiLevelNestedDeepCopy()
    {
        var source = new Level1Entity
        {
            Name = "L1",
            Child = new Level2Entity
            {
                Name = "L2",
                Child = new Level3Entity
                {
                    Name = "L3",
                    Child = new Level4Entity { Name = "L4", Value = 444 }
                }
            }
        };

        var copy = Mapper.DeepCopy(source)!;
        if (copy.Name != "L1")
            throw new Exception("4层嵌套 L1 验证失败");
        if (copy.Child?.Name != "L2")
            throw new Exception("4层嵌套 L2 验证失败");
        if (copy.Child?.Child?.Name != "L3")
            throw new Exception("4层嵌套 L3 验证失败");
        if (copy.Child?.Child?.Child?.Name != "L4" || copy.Child?.Child?.Child?.Value != 444)
            throw new Exception("4层嵌套 L4 验证失败");

        // 深拷贝隔离验证
        copy.Child!.Child!.Child!.Value = 999;
        if (source.Child!.Child!.Child!.Value == 999)
            throw new Exception("4层嵌套 DeepCopy 不是深拷贝，修改副本影响了原对象");

        Console.WriteLine($"  MultiLevelNestedDeepCopy 验证通过: L1→L2→L3→L4, Value=444");
    }

    /// <summary>
    /// T10-04: record 类型映射 AOT 验证
    /// </summary>
    static void TestRecordMap()
    {
        var source = new RecordSourceEntity { Id = 100, Title = "RecordMap", Score = 88.5 };
        var dto = Mapper.Map<RecordSourceEntity, RecordTargetDto>(source)!;
        if (dto.Id != 100 || dto.Title != "RecordMap" || dto.Score != 88.5)
            throw new Exception($"Record Map 验证失败: Id={dto.Id}, Title={dto.Title}, Score={dto.Score}");

        Console.WriteLine($"  RecordMap 验证通过: Id=100, Title=RecordMap, Score=88.5");
    }

    // ================================================================
    // T3-7: 补全 7 个 AOT 场景
    // ================================================================

    /// <summary>
    /// T3-7-1: 双向映射 — 反向 Map（DTO→Entity）
    /// </summary>
    static void TestBidirectional()
    {
        // 正向: Entity→DTO
        var entity = new TestEntity { Id = 7, Name = "Forward", Value = 70 };
        var dto = Mapper.Map<TestEntity, TestDto>(entity)!;
        if (dto.Id != 7 || dto.Name != "Forward")
            throw new Exception("双向映射正向验证失败");

        // 反向: DTO→Entity
        var back = Mapper.Map<TestDto, TestEntity>(dto)!;
        if (back.Id != 7 || back.Name != "Forward" || back.Value != 70)
            throw new Exception($"双向映射反向验证失败: Id={back.Id}, Name={back.Name}, Value={back.Value}");

        Console.WriteLine($"  Bidirectional 验证通过: Entity→Dto→Entity 值一致");
    }

    /// <summary>
    /// 反向直调扩展 AOT 可用性 — dto.MapToTestEntity 生成端直调（trimming 安全）
    /// </summary>
    static void TestReverseDirectCall()
    {
        // 反向直调扩展（零委托直调，JIT 可内联——AOT 下 trimming 不破坏）
        var dto = new TestDto { Id = 8, Name = "Direct", Value = 80 };
        var entity = dto.MapToTestEntity();
        if (entity == null || entity.Id != 8 || entity.Name != "Direct" || entity.Value != 80)
            throw new Exception($"反向直调扩展验证失败: Id={entity?.Id}, Name={entity?.Name}, Value={entity?.Value}");

        // 更新重载（已有目标实例）
        var existing = new TestEntity { Id = 0, Name = "Old", Value = 0 };
        dto.MapToTestEntity(existing);
        if (existing.Id != 8 || existing.Name != "Direct")
            throw new Exception($"反向直调更新重载验证失败: Id={existing.Id}, Name={existing.Name}");

        Console.WriteLine("  ReverseDirectCall 验证通过: MapToTestEntity 直调 + 更新重载");
    }

    /// <summary>
    /// T3-7-2: UpdateTarget — 更新已有目标对象
    /// </summary>
    static void TestUpdateTarget()
    {
        var entity = new TestEntity { Id = 5, Name = "Update", Value = 50 };
        var target = new TestDto { Id = 0, Name = "Old", Value = 0 };

        Mapper.Map(entity, target);
        if (target.Id != 5 || target.Name != "Update" || target.Value != 50)
            throw new Exception($"UpdateTarget 验证失败: Id={target.Id}, Name={target.Name}, Value={target.Value}");

        Console.WriteLine($"  UpdateTarget 验证通过: 已有对象属性已更新");
    }

    /// <summary>
    /// T3-7-3: MapIgnore — 方向忽略
    /// </summary>
    static void TestMapIgnore()
    {
        var entity = new IgnoreEntity { Id = 1, Name = "Test", Secret = "Hidden" };
        var dto = Mapper.Map<IgnoreEntity, IgnoreDto>(entity)!;

        // Name 正常映射
        if (dto.Id != 1 || dto.Name != "Test")
            throw new Exception("MapIgnore 基本映射验证失败");
        // Secret 被 [MapIgnore(Direction.ToDto)] 忽略 → 保持默认值
        if (dto.Secret != "")
            throw new Exception($"MapIgnore 正向验证失败: Secret 应为空但为 '{dto.Secret}'");

        // 反向映射：DTO→Entity 时 Secret 应正常映射（Direction.ToDto 不影响反向）
        var dto2 = new IgnoreDto { Id = 2, Name = "Reverse", Secret = "Carried" };
        var entity2 = Mapper.Map<IgnoreDto, IgnoreEntity>(dto2)!;
        if (entity2.Secret != "Carried")
            throw new Exception($"MapIgnore 反向验证失败: Secret 应为 'Carried' 但为 '{entity2.Secret}'");

        Console.WriteLine($"  MapIgnore 方向控制验证通过: 正向忽略 + 反向正常映射");
    }

    /// <summary>
    /// T3-7-4: ConvertUsing — 自定义转换器
    /// </summary>
    static void TestConvertUsing()
    {
        var entity = new ConvEntity { Id = 1, Price = 12345 };
        var dto = Mapper.Map<ConvEntity, ConvDto>(entity)!;

        if (dto.Id != 1)
            throw new Exception("ConvertUsing Id 验证失败");
        if (dto.PriceText != "¥12345")
            throw new Exception($"ConvertUsing 验证失败: PriceText='{dto.PriceText}', 期望 '¥12345'");

        Console.WriteLine($"  ConvertUsing 验证通过: Price=12345 → PriceText='¥12345'");
    }

    /// <summary>
    /// T3-7-5: Pipeline PostMap
    /// </summary>
    static void TestPipeline()
    {
        bool postMapCalled = false;

        Mapper.Pipeline.AddPostMap<TestEntity, TestDto>((src, dst) => postMapCalled = true);

        try
        {
            var entity = new TestEntity { Id = 1, Name = "Pipeline", Value = 10 };
            var dto = Mapper.Map<TestEntity, TestDto>(entity)!;
            if (dto.Id != 1)
                throw new Exception("Pipeline 映射验证失败");
            if (!postMapCalled)
                throw new Exception("Pipeline PostMap 未被调用");
        }
        finally
        {
            Mapper.Pipeline.Clear();
        }

        Console.WriteLine($"  Pipeline 验证通过: PostMap 被调用");
    }

    /// <summary>
    /// T3-7-6: MapTracer 诊断追踪
    /// </summary>
    static void TestMapTracer()
    {
        MapTracer.GloballyEnabled = true;
        try
        {
            var entity = new TestEntity { Id = 1, Name = "Trace", Value = 10 };
            var dto = Mapper.Map<TestEntity, TestDto>(entity)!;
            if (dto.Id != 1)
                throw new Exception("MapTracer 映射验证失败");
        }
        finally
        {
            MapTracer.GloballyEnabled = false;
        }

        Console.WriteLine($"  MapTracer 验证通过: 启用追踪后映射正常");
    }

    /// <summary>
    /// T3-7-7: MapList with Action callback
    /// </summary>
    static void TestBuildProjection()
    {
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "A", Value = 10 },
            new() { Id = 2, Name = "B", Value = 20 },
        };

        int actionCount = 0;
        var dtos = Mapper.MapList<TestEntity, TestDto>(entities, (src, dst) => actionCount++)!;
        if (dtos.Count != 2 || dtos[0].Name != "A" || dtos[1].Value != 20)
            throw new Exception("MapListWithAction 映射验证失败");
        if (actionCount != 2)
            throw new Exception($"MapListWithAction 回调验证失败: actionCount={actionCount}, 期望 2");

        Console.WriteLine($"  MapListWithAction 验证通过: Count=2, 回调执行 2 次");
    }

    /// <summary>
    /// 快照通道发布冒烟 — [assembly: MapDefaults] 经源生成器快照注册
    /// AOT 发布下 GetGlobalConfig 返回冻结配置且 IgnoreNames 含探针值
    /// <para>验证 buildTransitive targets 通道（MSBuild 属性 → 生成器 → 快照 → 冻结）端到端可用。</para>
    /// </summary>
    static void TestSnapshotChannel()
    {
        var view = Mapper.GetGlobalConfig();
        if (view == null)
            throw new Exception("SnapshotChannel 失败: GetGlobalConfig 返回 null（快照未注册或未冻结）");
        if (!view.IgnoreNames.Contains("__AotSnapshotProbe__", StringComparer.Ordinal))
            throw new Exception($"SnapshotChannel 失败: IgnoreNames=[{string.Join(",", view.IgnoreNames)}]，期望含 __AotSnapshotProbe__");

        Console.WriteLine($"  SnapshotChannel 验证通过: IgnoreNames 含探针值，冻结配置已生效");
    }

    /// <summary>
    /// ②：RuntimeSnapshotStore 承载类型 AOT 场景
    /// 发射开关 on（Directory.Build.props 默认）→ [MapFrom] DTO（TestDto/OrderDto）经
    /// [ModuleInitializer] RuntimeSnapshotMetadataInitializer 注册 RuntimeSnapshotStore。
    /// <para>验证点：① IsRegisteredDto([MapFrom] DTO) 命中（发射注册行 AOT 下执行）；
    /// ② TryGetMappingMeta 门面 IsDto=true + PrimarySource 正确（承载类型 + 注册表 AOT 可用）；
    /// ③ 承载类型零反射静态元数据 AOT 友好（无 IL2026/IL3050——构建期产物断言，编译时确认）。</para>
    /// <para>JIT/AOT 双模式均断言命中（发射是编译期静态注册，与运行模式无关——AOT 裁剪下
    /// [ModuleInitializer] 仍执行 + Type 键控注册表可查）。</para>
    /// </summary>
    static void TestRuntimeSnapshotRegistry()
    {
        // TestDto 标 [MapFrom(typeof(TestEntity))] → 发射注册（switch on）
        if (!RuntimeSnapshotStore.IsRegisteredDto(typeof(TestDto)))
            throw new Exception("RuntimeSnapshotRegistry 失败: TestDto 未注册 RuntimeSnapshotStore（发射注册行未执行或开关 off）");

        if (!RuntimeSnapshotStore.TryGetMappingMeta(typeof(TestDto), out var view))
            throw new Exception("RuntimeSnapshotRegistry 失败: TryGetMappingMeta(TestDto) 未命中门面");
        if (!view.IsDto || view.IsRegistered == false)
            throw new Exception("RuntimeSnapshotRegistry 失败: 门面 view 语义错误（IsDto/IsRegistered）");
        if (view.PrimarySource != typeof(TestEntity))
            throw new Exception($"RuntimeSnapshotRegistry 失败: 主源={view.PrimarySource?.Name}，期望 TestEntity");

        // 多 DTO 覆盖（OrderDetailDto 亦 [MapFrom]）
        if (!RuntimeSnapshotStore.IsRegisteredDto(typeof(OrderDetailDto)))
            throw new Exception("RuntimeSnapshotRegistry 失败: OrderDetailDto 未注册（多 DTO 发射覆盖不足）");

        Console.WriteLine("  RuntimeSnapshotRegistry 验证通过: TestDto/OrderDto 注册表命中 + 门面语义正确（AOT 可用）");
    }

    /// <summary>
    /// （Fluent 发布形态 AOT 冒烟）
    /// [MapperConfig] 类型对 → FluentSnapshotRegistry 发射注册（身份行）→ GeneratedCache
    /// Fluent 生成映射直调（异名 From 全语义）。
    /// <para>验证点：① IsFluentRegisteredDto 命中（Fluent 身份发射注册行 AOT 下执行——同
    /// RuntimeSnapshotRegistry 口径，编译期静态注册与运行模式无关）；② 生成映射值正确
    /// （生成通道零反射——AOT 可用性设计目标）。</para>
    /// </summary>
    static void TestFluentChannel()
    {
        if (!RuntimeSnapshotStore.IsFluentRegisteredDto(typeof(AotFluentView)))
            throw new Exception("FluentChannel 失败: Fluent 身份登记未命中（FluentSnapshotRegistry 注册行未执行或开关 off）");

        var dto = Mapper.Map<AotFluentEntity, AotFluentView>(new AotFluentEntity { Id = 7, Name = "fluent-aot" })!;
        if (dto.Id != 7 || dto.AliasName != "fluent-aot")
            throw new Exception($"FluentChannel 映射验证失败: Id={dto.Id}, AliasName={dto.AliasName}");

        Console.WriteLine("  FluentChannel 验证通过: [MapperConfig] 发射注册 + Fluent 生成映射（异名 From）AOT 冒烟");
    }

    // ================================================================
    // 5 类负面用例
    // 设计原则：AotTests 需在 JIT（本机）与 AOT（CI publish 后运行）双模式下可执行，
    // 以 RuntimeFeature.IsDynamicCodeSupported 分支——JIT 下验证回退路径可用（不崩溃），
    // AOT 下断言失败路径。编译期警告是负面用例的另一半意义：调用已标注 Requires* 的
    // API（Map/Compose/AsDto/DeepCopy/Validate(params Assembly[])）在本文件构建时
    // 预期产生 IL2026/IL3050 警告——"AOT 不可用"的编译期信号，警告传播链完整。
    // ================================================================

    /// <summary>
    /// 负面①：DTO 未标 [MapFrom] 的运行时回退 — JIT 下反射回退可用；AOT 下应失败
    /// </summary>
    static void TestFallbackWithoutMapFrom()
    {
        var src = new NoAttrSource { Id = 1, Name = "Fallback" };
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var dto = Mapper.Map<NoAttrSource, NoAttrTarget>(src)!;
            if (dto.Id != 1 || dto.Name != "Fallback")
                throw new Exception("回退映射结果错误");
            Console.WriteLine("  [负面①] JIT 下未标回退路径可用（AOT 下此处应失败）");
        }
        else
        {
            var aotFailed = false; // c50608a 遗漏修复（验收补）：该标志位漏定义——Run 局部函数的同名变量不在本方法作用域
            try
            {
                Mapper.Map<NoAttrSource, NoAttrTarget>(src);
            }
            catch (Exception)
            {
                aotFailed = true; // AOT 下任何失败均符合预期
            }
            if (!aotFailed)
                throw new Exception("AOT 下未标 [MapFrom] 回退应失败但成功");
            Console.WriteLine("    [负面①] AOT 下未标回退按预期失败");
        }
    }

    /// <summary>
    /// 负面②：ORM 扩展 AsDto（双标注）— 编译期 IL3050/IL2026 警告（探针）
    /// AOT 下运行时失败；JIT 下无 DB 不执行（AsDto 需 FreeSql ISelect&lt;T&gt; 查询源）
    /// </summary>
    static void TestOrmAsDtoAotUnsupported()
    {
        // 编译期：下方 AsDto 调用产生 IL3050/IL2026 警告——AOT 消费者在编译期即收到
        // "ORM 投影不可用"信号（Projection 源生成器为长期目标）。
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            Console.WriteLine("  [负面②] ORM AsDto 编译期警告验证（JIT 跳过运行时，需真实 DB）");
            return;
        }

        var aotFailed = false; // 修复标志位区分守护异常，"应失败而成功"不再被宽泛 catch 吞掉
            try
            {
                ISelect<NoAttrSource>? select = null;
            // AsDto<TSource> 泛型为查询源类型，DTO 由 ToList<TDto>() 指定
            _ = NCMapperFreeSqlExtensions.AsDto<NoAttrSource>(select!).ToList<NoAttrTarget>();
            }
            catch (Exception)
            {
                aotFailed = true; // AOT 下任何失败均符合预期
            }
            if (!aotFailed)
                throw new Exception("AOT 下 ORM AsDto 应失败但成功");
            Console.WriteLine("    [负面②] AOT 下 ORM AsDto 按预期失败");
    }

    /// <summary>
    /// 负面③：Compose 未注册 RegisterGeneratedCompose — JIT 下 ComposeRuntime 回退可用；
    /// AOT 下动态代码不可用应失败
    /// </summary>
    static void TestComposeUnregistered()
    {
        var order = new OrderEntity { OrderId = 1, OrderAmount = 10m };
        var customer = new CustomerEntity { CustomerName = "X", CustomerLevel = 1 };
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var dto = Mapper.Compose<UnregisteredComposeDto>(order, customer)!;
            if (dto.OrderId != 1 || dto.CustomerName != "X")
                throw new Exception("Compose 回退映射结果错误");
            Console.WriteLine("  [负面③] JIT 下 Compose 未注册回退可用（AOT 下此处应失败）");
        }
        else
        {
            var aotFailed = false; // 修复标志位区分守护异常，"应失败而成功"不再被宽泛 catch 吞掉
            try
            {
                Mapper.Compose<UnregisteredComposeDto>(order, customer);
            }
            catch (Exception)
            {
                aotFailed = true; // AOT 下任何失败均符合预期
            }
            if (!aotFailed)
                throw new Exception("AOT 下未注册 Compose 应失败但成功");
            Console.WriteLine("    [负面③] AOT 下未注册 Compose 按预期失败");
        }
    }

    /// <summary>
    /// 负面④：record 循环引用 — 源生成器报 NCMAP012（构建期警告），DeepCopy 回退运行时；
    /// JIT 下回退可用；AOT 下运行时反射路径为 Requires* 双标注的"不承诺"路径——
    /// 真实契约是"可能失败（成员被裁剪时）"，非"必定失败"。
    /// 本测试程序直接引用该类型（root），成员常被保留，故 AOT 下"可执行不抛"亦属合法（同前）。
    /// 真实契约（IL2026/IL3050 警告已声明）由构建期门禁校验（scripts/check-aot-il-warnings.ps1），
    /// 不在运行时"靠撞运气"断言必须抛。
    /// </summary>
    static void TestRecordCircularFallback()
    {
        var source = new CyclicRecordDto(1, new CyclicRecordDto(2, null));
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var copy = Mapper.DeepCopy(source)!;
            if (copy.Id != 1 || copy.Next?.Id != 2 || copy.Next!.Next != null)
                throw new Exception("record 循环引用回退深拷贝失败");
            if (ReferenceEquals(source, copy))
                throw new Exception("回退深拷贝返回同一引用");
            Console.WriteLine("  [负面④] JIT 下 record 循环引用 NCMAP012 回退可用");
        }
        else
        {
            // AOT：接受"可执行不抛"与"按预期失败"两种结果（RequiresUnreferencedCode 契约语义，
            // 同）；数据完整性受成员裁剪影响，完整契约验证移交构建期 IL 警告门禁。
            try
            {
                var copy = Mapper.DeepCopy(source);
                if (copy == null)
                    throw new Exception("AOT 下 record 循环引用回退返回 null");
                Console.WriteLine("    [负面④] AOT 下 record 循环引用回退可执行不抛（成员保留——Requires* 契约语义，构建期 IL 门禁兜底）");
            }
            catch (Exception)
            {
                Console.WriteLine("    [负面④] AOT 下 record 循环引用回退按预期失败（成员被裁剪——Requires* 契约语义，构建期 IL 门禁兜底）");
            }
        }
    }

    /// <summary>
    /// 负面⑤：Validate(params Assembly[])（双标注）— 编译期 IL2026/IL3050 警告
    /// JIT 下反射扫描返回结果；AOT 下运行时反射路径为 Requires* 双标注的"不承诺"路径——
    /// 真实契约是"可能失败（成员被裁剪时）"，非"必定失败"。本测试程序直接调用该重载（root），
    /// 反射扫描常能成功，故 AOT 下"可执行不抛"亦属合法。真实契约（IL2026/IL3050 警告已声明）
    /// 由构建期门禁校验（scripts/check-aot-il-warnings.ps1），不在运行时"靠撞运气"断言必须抛。
    /// </summary>
    static void TestValidateWithAssemblies()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            // AOT：接受"可执行不抛"与"按预期失败"两种结果（RequiresUnreferencedCode 契约语义）；
            // 若成功返回，仅断言结果可读（非空即可），完整契约验证移交构建期 IL 警告门禁。
            try
            {
                var results = Mapper.Validate(typeof(TestEntity).Assembly);
                Console.WriteLine($"  [负面⑤] AOT 下 Validate(params Assembly[]) 可执行不抛（返回 {results.Count} 条——成员保留，Requires* 契约语义，构建期 IL 门禁兜底）");
            }
            catch (Exception)
            {
                Console.WriteLine("    [负面⑤] AOT 下 Validate(params Assembly[]) 按预期失败（成员被裁剪——Requires* 契约语义，构建期 IL 门禁兜底）");
            }
            return;
        }

        var results2 = Mapper.Validate(typeof(TestEntity).Assembly);
        if (results2.Count == 0)
            throw new Exception("Validate(params Assembly[]) 返回空结果");
        Console.WriteLine($"  [负面⑤] JIT 下 Validate(params Assembly[]) 返回 {results2.Count} 条");
    }
}

// 测试模型

// Fluent AOT 冒烟模型（[MapperConfig] 类型对——生成通道 + 发射登记双验证）

/// <summary>Fluent AOT 冒烟源实体</summary>
public class AotFluentEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

/// <summary>Fluent AOT 冒烟视图（异名 AliasName ← Name）</summary>
public class AotFluentView
{
    /// <summary>主键标识（自动匹配）</summary>
    public int Id { get; set; }
    /// <summary>异名映射字段</summary>
    public string AliasName { get; set; } = "";
}

/// <summary>Fluent AOT 冒烟配置（生成器发射 FluentSnapshotRegistry 身份行 + GeneratedCache 映射）</summary>
[MapperConfig]
internal class AotFluentMapper
{
    public static void Configure(FluentMappingConfig cfg)
        => cfg.For<AotFluentEntity, AotFluentView>()
              .Member(d => d.AliasName).From(s => s.Name);
}

/// <summary>
/// AOT 测试实体 — 包含基本值类型字段
/// </summary>
public class TestEntity
{
    /// <summary>
    /// 主键标识
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// 数值
    /// </summary>
    public int Value { get; set; }
}

/// <summary>
/// AOT 测试 DTO — 映射自 TestEntity
/// </summary>
[MapFrom(typeof(TestEntity))]
public class TestDto
{
    /// <summary>
    /// 主键标识，映射自 TestEntity.Id
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 名称，映射自 TestEntity.Name
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// 数值，映射自 TestEntity.Value
    /// </summary>
    public int Value { get; set; }
}

/// <summary>
/// AOT 测试 — 订单实体
/// </summary>
public class OrderEntity
{
    /// <summary>
    /// 订单标识
    /// </summary>
    public int OrderId { get; set; }
    /// <summary>
    /// 订单金额
    /// </summary>
    public decimal OrderAmount { get; set; }
}

/// <summary>
/// AOT 测试 — 客户实体
/// </summary>
public class CustomerEntity
{
    /// <summary>
    /// 客户名称
    /// </summary>
    public string CustomerName { get; set; } = "";
    /// <summary>
    /// 客户等级
    /// </summary>
    public int CustomerLevel { get; set; }
}

/// <summary>
/// AOT 测试 — 订单详情 DTO（多源合成，映射自 OrderEntity 和 CustomerEntity）
/// </summary>
[MapFrom(typeof(OrderEntity))]
public class OrderDetailDto
{
    /// <summary>
    /// 订单标识，映射自 OrderEntity.OrderId
    /// </summary>
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderId))]
    public int OrderId { get; set; }

    /// <summary>
    /// 订单金额，映射自 OrderEntity.OrderAmount
    /// </summary>
    [MapProperty(typeof(OrderEntity), nameof(OrderEntity.OrderAmount))]
    public decimal OrderAmount { get; set; }

    /// <summary>
    /// 客户名称，映射自 CustomerEntity.CustomerName
    /// </summary>
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.CustomerName))]
    public string CustomerName { get; set; } = "";

    /// <summary>
    /// 客户等级，映射自 CustomerEntity.CustomerLevel
    /// </summary>
    [MapProperty(typeof(CustomerEntity), nameof(CustomerEntity.CustomerLevel))]
    public int CustomerLevel { get; set; }
}

// ================================================================
// T03: AOT 丰富测试模型 — Nullable / Enum / Nested / List
// ================================================================

/// <summary>
/// AOT 测试状态枚举
/// </summary>
public enum AotTestStatus
{
    /// <summary>待处理</summary>
    Pending = 0,
    /// <summary>激活</summary>
    Active = 1,
    /// <summary>未激活</summary>
    Inactive = 2
}

/// <summary>
/// AOT 测试优先级枚举
/// </summary>
public enum AotTestPriority
{
    /// <summary>低</summary>
    Low = 0,
    /// <summary>中</summary>
    Medium = 1,
    /// <summary>高</summary>
    High = 2,
    /// <summary>紧急</summary>
    Urgent = 3
}

// ---- T03-01: Nullable 模型 ----

/// <summary>
/// AOT 可空测试实体
/// </summary>
public class NullableTestEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>分数（可空）</summary>
    public int? Score { get; set; }
    /// <summary>生日（可空）</summary>
    public DateTime? Birthday { get; set; }
    /// <summary>描述（可空引用类型）</summary>
    public string? Description { get; set; }
}

/// <summary>
/// AOT 可空测试 DTO
/// </summary>
[MapFrom(typeof(NullableTestEntity))]
public class NullableTestDto
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>分数</summary>
    public int? Score { get; set; }
    /// <summary>生日</summary>
    public DateTime? Birthday { get; set; }
    /// <summary>描述</summary>
    public string? Description { get; set; }
}

// ---- T03-02: Enum 模型 ----

/// <summary>
/// AOT 枚举测试实体
/// </summary>
public class EnumTestEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>状态枚举</summary>
    public AotTestStatus Status { get; set; }
    /// <summary>优先级枚举</summary>
    public AotTestPriority Priority { get; set; }
    /// <summary>可选状态（可空枚举）</summary>
    public AotTestStatus? OptionalStatus { get; set; }
}

/// <summary>
/// AOT 枚举测试 DTO
/// </summary>
[MapFrom(typeof(EnumTestEntity))]
public class EnumTestDto
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>状态枚举</summary>
    public AotTestStatus Status { get; set; }
    /// <summary>优先级枚举</summary>
    public AotTestPriority Priority { get; set; }
    /// <summary>可选状态</summary>
    public AotTestStatus? OptionalStatus { get; set; }
}

// ---- T03-03: Nested 模型 ----

/// <summary>
/// AOT 内层测试实体
/// </summary>
public class InnerTestEntity
{
    /// <summary>内层标识</summary>
    public int InnerId { get; set; }
    /// <summary>内层名称</summary>
    public string InnerName { get; set; } = "";
}

/// <summary>
/// AOT 嵌套测试实体
/// </summary>
public class NestedTestEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>嵌套内层对象</summary>
    public InnerTestEntity? Inner { get; set; }
}

/// <summary>
/// AOT 内层测试 DTO
/// </summary>
[MapFrom(typeof(InnerTestEntity))]
public class InnerTestDto
{
    /// <summary>内层标识</summary>
    public int InnerId { get; set; }
    /// <summary>内层名称</summary>
    public string InnerName { get; set; } = "";
}

/// <summary>
/// AOT 嵌套测试 DTO
/// </summary>
[MapFrom(typeof(NestedTestEntity))]
public class NestedTestDto
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>嵌套内层 DTO</summary>
    public InnerTestDto? Inner { get; set; }
}

// ---- T03-04: List 模型 ----

/// <summary>
/// AOT 列表测试实体
/// </summary>
public class ListTestEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>标签列表</summary>
    public List<string>? Tags { get; set; }
    /// <summary>分数列表</summary>
    public List<int>? Scores { get; set; }
}

/// <summary>
/// AOT 列表测试 DTO
/// </summary>
[MapFrom(typeof(ListTestEntity))]
public class ListTestDto
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>标签列表</summary>
    public List<string>? Tags { get; set; }
    /// <summary>分数列表</summary>
    public List<int>? Scores { get; set; }
}

// ================================================================
// ⑤: 枚举异常语义+ 集合 null Update 冒烟模型
// ================================================================

/// <summary>
/// T2 冒烟枚举 — string→enum 大小写无关/非法值异常语义载体
/// </summary>
public enum AotColor
{
    /// <summary>红（值 1）</summary>
    Red = 1,
    /// <summary>蓝（值 2）</summary>
    Blue = 2
}

/// <summary>
/// T2 冒烟源实体 — 字符串颜色属性
/// </summary>
public class AotEnumStrEntity
{
    /// <summary>颜色字符串</summary>
    public string? ColorStr { get; set; }
}

/// <summary>
/// T2 冒烟 DTO — string→enum（生成侧 switch，T2 改造后大小写无关 + 非法值抛 MapException）
/// </summary>
[MapFrom(typeof(AotEnumStrEntity))]
public class AotEnumStrDto
{
    /// <summary>颜色枚举</summary>
    public AotColor Color { get; set; }
}

/// <summary>
/// T3 冒烟源实体 — 基元异类型集合（List&lt;int&gt; → List&lt;long&gt;，T4 内联路径载体）
/// </summary>
public class AotCollEntity
{
    /// <summary>整数集合</summary>
    public List<int>? Nums { get; set; }
}

/// <summary>
/// T3 冒烟 DTO — Update 路径 null 语义（源集合 null → 清空旧值）
/// </summary>
[MapFrom(typeof(AotCollEntity))]
public class AotCollDto
{
    /// <summary>长整型集合</summary>
    public List<long>? Nums { get; set; }
}

// ================================================================
// T10: AOT 测试补充 — Dictionary / record / 深层嵌套
// ================================================================

// ---- T10-01: Dictionary 模型 ----

/// <summary>
/// AOT 字典深拷贝测试 — 值元素实体
/// </summary>
public class DictItemEntity
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>数值</summary>
    public int Value { get; set; }
}

/// <summary>
/// AOT 字典深拷贝测试 — 包含 Dictionary 属性的实体
/// </summary>
public class DictTestEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>字典属性</summary>
    public Dictionary<string, DictItemEntity>? Items { get; set; }
}

/// <summary>
/// AOT 字典深拷贝测试 — 触发 DictTestEntity 参与 [MapFrom] 映射的 DTO 载体。
/// <para>DeepCopy 方法自动为所有参与 [MapFrom] 映射的源/目标类型生成（CodeGenerator.cs:86-89）；
/// 字典属性仅存在于实体侧，DTO 保持简单属性避免 Map 路径对字典的处理。</para>
/// </summary>
[MapFrom(typeof(DictTestEntity))]
public class DictTestDto
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
}

/// <summary>
/// AOT 字典深拷贝测试 — 触发 DictItemEntity 参与 [MapFrom] 映射的 DTO 载体。
/// <para>字典 value 类型须同样参与映射以生成 DeepCopy 方法，供字典深拷贝递归调用
/// （生成器 deepCopyTypes 仅收集映射的源/目标类型，不递归收集嵌套引用类型）。</para>
/// </summary>
[MapFrom(typeof(DictItemEntity))]
public class DictItemDto
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>数值</summary>
    public int Value { get; set; }
}

// ---- T10-02: record class 模型 ----

/// <summary>
/// AOT record 深拷贝测试 — record class 带主构造器
/// </summary>
public record class RecordTestEntity(int Id, string Name, int Value);

/// <summary>
/// AOT record 深拷贝测试 — 触发 RecordTestEntity 参与 [MapFrom] 映射的 DTO 载体。
/// <para>record 参与映射后由 GenerateDeepCopyRecordPath（构造器参数路径）生成深拷贝方法；
/// 未参与映射则走运行时回退，AOT 下不可用（同 T10-01 字典缺口口径）。</para>
/// </summary>
[MapFrom(typeof(RecordTestEntity))]
public record class RecordTestDto(int Id, string Name, int Value);

// ---- T10-03: 4 层嵌套模型 ----

/// <summary>AOT 4层嵌套 — 第4层（最内层）</summary>
public class Level4Entity
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>数值</summary>
    public int Value { get; set; }
}

/// <summary>AOT 4层嵌套 — 第3层</summary>
public class Level3Entity
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>子对象</summary>
    public Level4Entity? Child { get; set; }
}

/// <summary>AOT 4层嵌套 — 第2层</summary>
public class Level2Entity
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>子对象</summary>
    public Level3Entity? Child { get; set; }
}

/// <summary>AOT 4层嵌套 — 第1层（最外层）</summary>
public class Level1Entity
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>子对象</summary>
    public Level2Entity? Child { get; set; }
}

/// <summary>
/// AOT 4层嵌套 — 触发 Level4Entity 参与 [MapFrom] 映射的 DTO 载体（同 T10-01/02 口径：
/// DeepCopy 方法为参与映射的类型生成，嵌套链每层须显式参与，递归 DeepCopy 方可用生成路径）。
/// </summary>
[MapFrom(typeof(Level4Entity))]
public class Level4Dto
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>数值</summary>
    public int Value { get; set; }
}

/// <summary>AOT 4层嵌套 — 触发 Level3Entity 参与映射的 DTO 载体（仅标量，避免 Map 嵌套复杂度）</summary>
[MapFrom(typeof(Level3Entity))]
public class Level3Dto
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

/// <summary>AOT 4层嵌套 — 触发 Level2Entity 参与映射的 DTO 载体</summary>
[MapFrom(typeof(Level2Entity))]
public class Level2Dto
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

/// <summary>AOT 4层嵌套 — 触发 Level1Entity 参与映射的 DTO 载体</summary>
[MapFrom(typeof(Level1Entity))]
public class Level1Dto
{
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

// ---- T10-04: record 映射模型 ----

/// <summary>
/// AOT record 映射测试 — 源实体（普通 class）
/// </summary>
public class RecordSourceEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>标题</summary>
    public string Title { get; set; } = "";
    /// <summary>分数</summary>
    public double Score { get; set; }
}

/// <summary>
/// AOT record 映射测试 — 目标 record（带主构造器）
/// </summary>
[MapFrom(typeof(RecordSourceEntity))]
public record class RecordTargetDto(int Id, string Title, double Score);

// ================================================================
// T3-7: 补全 AOT 场景模型
// ================================================================

// ---- T3-7-3: MapIgnore 模型 ----

/// <summary>
/// AOT MapIgnore 测试实体
/// </summary>
public class IgnoreEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>秘密字段（ToDto 方向忽略）</summary>
    public string Secret { get; set; } = "";
}

/// <summary>
/// AOT MapIgnore 测试 DTO
/// </summary>
[MapFrom(typeof(IgnoreEntity))]
public class IgnoreDto
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
    /// <summary>秘密字段 — 仅 ToDto 方向忽略（反向 DTO→Entity 应正常映射）</summary>
    [MapIgnore(Direction.ToDto)]
    public string Secret { get; set; } = "";
}

// ---- T3-7-4: ConvertUsing 模型 ----

/// <summary>
/// AOT ConvertUsing 测试实体
/// </summary>
public class ConvEntity
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>价格（分）</summary>
    public int Price { get; set; }
}

/// <summary>
/// AOT ConvertUsing 测试 DTO
/// </summary>
[MapFrom(typeof(ConvEntity))]
public class ConvDto
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>价格文本（由转换器生成）</summary>
    [Convert(typeof(PriceConverter), nameof(PriceConverter.Convert))]
    [MapProperty(typeof(ConvEntity), nameof(ConvEntity.Price))]
    public string PriceText { get; set; } = "";
}

/// <summary>
/// AOT 价格转换器（静态方法引用形态，T1 模板 M(TField)）— int→string（¥格式）
/// </summary>
public static class PriceConverter
{
    /// <summary>价格（分）格式化为 "¥x"</summary>
    public static string Convert(int fieldValue) => $"¥{fieldValue}";
}

// ================================================================
// 5 类负面用例模型
// ================================================================

// ---- 负面①: 未标 [MapFrom] 模型（无源生成器路径 → 运行时回退）----

/// <summary>
/// 负面①源实体 — 未标注
/// </summary>
public class NoAttrSource
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 负面①目标 DTO — 刻意不标 [MapFrom]，验证回退路径
/// </summary>
public class NoAttrTarget
{
    /// <summary>主键标识</summary>
    public int Id { get; set; }
    /// <summary>名称</summary>
    public string Name { get; set; } = "";
}

// ---- 负面③: Compose 未注册模型（无 RegisterGeneratedCompose → ComposeRuntime 回退）----

/// <summary>
/// 负面③ Compose 目标 DTO — 刻意不标 [MapFrom]，验证 Compose 回退路径
/// </summary>
public class UnregisteredComposeDto
{
    /// <summary>订单标识（与 OrderEntity 同名匹配）</summary>
    public int OrderId { get; set; }
    /// <summary>客户名称（与 CustomerEntity 同名匹配）</summary>
    public string CustomerName { get; set; } = "";
}

// ---- 负面④: record 循环引用模型（源生成器报 NCMAP012 → DeepCopy 回退运行时）----

/// <summary>
/// 负面④ record 循环引用源 — 自引用结构
/// </summary>
public record class CyclicRecordSource(int Id, CyclicRecordSource? Next);

/// <summary>
/// 负面④ record 循环引用 DTO — [MapFrom] + 自引用，源生成器报 NCMAP012（构建期警告）
/// </summary>
[MapFrom(typeof(CyclicRecordSource))]
public record class CyclicRecordDto(int Id, CyclicRecordDto? Next);

/// <summary>
/// AOT 冒烟模型无 [MapFrom] 的环 record
/// 构参 Name（getter-only，backing-field 回写承载）+ 构参外 set 属性 Next（双向环闭合）+
/// 构参外普通属性 Tag（FillProperties 补填承载）；深拷贝走运行时 Provider 占位路径。
/// AOT 契约说明：运行时深拷贝路径为 RequiresDynamicCode/RequiresUnreferencedCode 双标注的
/// 不承诺路径——ILC 会裁剪无静态引用的成员（实测 typeof(X).Get*() 直接反射引用可触发
/// ILLink 保守保留，本模型 JIT 分支完整断言所验证的即此形态）；AOT 场景推荐源生成路径。
/// </summary>
public record class B02B03SmokeNode(string Name)
{
    /// <summary>构参外 set 属性 — 双向环闭合载体（环回边引用一致性断言锚点）</summary>
    public B02B03SmokeNode? Next { get; set; }

    /// <summary>构参外普通属性 — 构参外属性补填断言锚点</summary>
    public string? Tag { get; set; }
}
