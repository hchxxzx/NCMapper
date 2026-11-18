using NCMapper.Attributes;
using NCMapper.Generated;
using NCMapper.Samples.Models.Dtos;
using NCMapper.Samples.Models.Entities;
using NCMapper.Samples.Helpers;
using NCMapper.Samples.Models.Enums;

namespace NCMapper.Samples.Declarative;

/// <summary>
/// 基础映射场景集 — 每个静态方法对应一个子场景，返回映射结果供调度层输出。
/// </summary>
public static class BasicScenarios
{
    // ===== 1. 单对象映射 =====

    /// <summary>
    /// 最基础的同名属性映射：UserEntity → UserDto。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>映射后的 DTO</returns>
    #region BasicMapping_SimpleNameMatch
    public static UserDto SimpleNameMatch(UserEntity src)
        => Mapper.Map<UserEntity, UserDto>(src)!;
    #endregion

    /// <summary>
    /// 列表映射：List&lt;UserEntity&gt; → List&lt;UserDto&gt;。
    /// </summary>
    /// <param name="srcs">源实体列表</param>
    /// <returns>映射后的 DTO 列表</returns>
    #region BasicMapping_ListMap
    public static List<UserDto> ListMap(List<UserEntity> srcs)
        => Mapper.MapList<UserEntity, UserDto>(srcs)!;
    #endregion

    /// <summary>
    /// MapListInto 批量就地映射 — 复用预分配目标数组，仅覆盖映射字段，顶层零分配。
    /// <para>对比 MapList（每次调用全量分配新 List + N 个 DTO）：MapListInto 复用已有目标对象，
    /// 适合 UI 高频刷新、批量同步到常驻缓冲区等场景（见 how-to/mapping/map-list-into.md）。</para>
    /// </summary>
    /// <param name="srcs">源实体列表</param>
    /// <param name="dtos">预分配并初始化每个元素的目标数组（长度 ≥ srcs.Count）</param>
    #region BasicMapping_MapListInto
    public static void ListMapInto(List<UserEntity> srcs, UserDto[] dtos)
        => Mapper.MapListInto<UserEntity, UserDto>(srcs, dtos);
    #endregion

    /// <summary>
    /// 带 Action 回调的映射，映射后立即修改目标。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>Name 带 [VIP] 前缀的 DTO</returns>
    #region BasicMapping_MapWithAction
    public static UserDto MapWithAction(UserEntity src)
        => Mapper.Map<UserEntity, UserDto>(src, (s, t) => t.Name = $"[VIP] {s.Name}")!;
    #endregion

    /// <summary>
    /// 更新已有目标对象（不创建新实例）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="target">待更新的目标对象</param>
    #region BasicMapping_UpdateExisting
    public static void UpdateExisting(UserEntity src, UserDto target)
        => Mapper.Map(src, target);
    #endregion

    // ===== 2. 异名字段与自定义转换 =====

    /// <summary>
    /// [MapProperty] 异名映射 + [Convert] 自定义转换 + PascalCase 扁平化演示。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>含异名字段与转换结果的详情 DTO</returns>
    public static UserDetailDto CustomPropertyAndConvert(UserEntity src)
        => Mapper.Map<UserEntity, UserDetailDto>(src)!;

    // ===== 3. 双向映射与方向控制 =====

    /// <summary>
    /// 正向映射 Entity → DTO。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>映射后的 DTO</returns>
    #region BasicMapping_ForwardMap
    public static CustomerDto ForwardMap(CustomerEntity src)
        => Mapper.Map<CustomerEntity, CustomerDto>(src)!;
    #endregion

    /// <summary>
    /// 反向映射 DTO → Entity，验证 [MapIgnore(Direction.ToEntity)] 生效。
    /// </summary>
    /// <param name="src">源 DTO</param>
    /// <returns>反向映射后的实体（DisplayLabel 被忽略）</returns>
    #region BasicMapping_MapIgnore
    public static CustomerEntity BackwardMap(CustomerDto src)
        => Mapper.Map<CustomerDto, CustomerEntity>(src)!;
    #endregion

    // ===== 4. 源生成器扩展方法 =====

    /// <summary>
    /// 使用源生成器扩展方法，零委托直调。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>映射后的 DTO</returns>
    #region BasicMapping_SourceGenExtension
    public static UserDto SourceGenExtension(UserEntity src)
        => src.MapToUserDto()!;
    #endregion


    /// <summary>
    /// 源生成器扩展方法 + Action 回调。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>Name 带 [VIP] 前缀的 DTO</returns>
    #region BasicMapping_SourceGenExtension_SourceGenWithAction
    public static UserDto SourceGenWithAction(UserEntity src)
        => src.MapToUserDto((s, t) => t.Name = $"[VIP] {s.Name}")!;
    #endregion


    /// <summary>
    /// 源生成器扩展方法：更新已有对象（void 返回）。
    /// </summary>
    /// <param name="src">源实体</param>
    /// <param name="target">待更新的目标对象</param>
    #region BasicMapping_SourceGenExtension_SourceGenUpdateExisting
    public static void SourceGenUpdateExisting(UserEntity src, UserDto target)
        => src.MapToUserDto(target);
    #endregion


    /// <summary>
    /// 源生成器扩展方法：列表零委托直调。
    /// </summary>
    /// <param name="srcs">源实体列表</param>
    /// <returns>映射后的 DTO 列表</returns>
    #region BasicMapping_SourceGenExtension_SourceGenList
    public static List<UserDto> SourceGenList(List<UserEntity> srcs)
        => srcs.MapToUserDtoList()!;
    #endregion

    // ===== 4.1 反向直调与反向自定义转换 =====

    /// <summary>
    /// 反向直调扩展：DTO → Entity 新建（dto.MapToUserEntity()，生成委托路径零反射）。
    /// </summary>
    /// <param name="src">源 DTO</param>
    /// <returns>反向映射新建的实体</returns>
    #region BasicMapping_ReverseDirectExtension
    public static UserEntity ReverseDirectExtension(UserDto src)
        => src.MapToUserEntity()!;
    #endregion

    /// <summary>
    /// 反向直调扩展：更新已有实体（编辑表单提交主场景，dto.MapToUserEntity(entity)）。
    /// </summary>
    /// <param name="src">源 DTO</param>
    /// <param name="target">待更新的实体</param>
    #region BasicMapping_ReverseDirectUpdate
    public static void ReverseDirectUpdate(UserDto src, UserEntity target)
        => src.MapToUserEntity(target);
    #endregion

    /// <summary>
    /// 反向直调扩展：列表 DTO → Entity（dtos.MapToUserEntityList()）。
    /// </summary>
    /// <param name="srcs">源 DTO 列表</param>
    /// <returns>反向映射的实体列表</returns>
    public static List<UserEntity> ReverseDirectList(List<UserDto> srcs)
        => srcs.MapToUserEntityList()!;

    /// <summary>
    /// [ConvertBack] 往返演示：entity → dto（正向格式化）→ entity（反向解析）。
    /// <para>正向 [Convert(typeof(DateTimeFormatter), nameof(DateTimeFormatter.Convert))] 将 CreatedAt 格式化为字符串；
    /// 反向 [ConvertBack(typeof(DateBackParser), nameof(DateBackParser.Convert))] 将 RegisterDate 解析回 CreatedAt——
    /// 正向转换器反向默认不消费（NCMAP033），往返闭环依赖显式反向转换器。</para>
    /// </summary>
    /// <param name="src">源实体</param>
    /// <returns>往返后的实体（CreatedAt 应等于源值）</returns>
    public static UserEntity ConvertBackRoundTrip(UserEntity src)
    {
        var dto = Mapper.Map<UserEntity, UserDetailDto>(src)!;  // 正向：CreatedAt → "yyyy-MM-dd"
        return dto.MapToUserEntity()!;                           // 反向：RegisterDate → CreatedAt（DateBackParser）
    }

    // ===== 5. JSON 字段映射 =====

    /// <summary>
    /// [MapProperty(IsFromJson = true)]：string 列反序列化为强类型对象/集合。
    /// </summary>
    /// <param name="src">含 JSON 文本列的实体</param>
    /// <returns>JSON 字段被反序列化为强类型的 DTO</returns>
    public static OrderConfigDto JsonFieldToObject(OrderConfigEntity src)
        => Mapper.Map<OrderConfigEntity, OrderConfigDto>(src)!;

    /// <summary>
    /// IsFromJson 反向：对象序列化为 JSON 字符串。
    /// </summary>
    /// <param name="src">含强类型对象的源</param>
    /// <returns>对象被序列化为 JSON 字符串的 DTO</returns>
    public static OrderConfigReverseDto JsonFieldReverse(OrderConfigSource src)
        => Mapper.Map<OrderConfigSource, OrderConfigReverseDto>(src)!;

    /// <summary>
    /// 值类型目标 — IsFromJson 不支持，属性被跳过（保持默认值）。
    /// </summary>
    /// <param name="src">含 JSON 文本列的实体</param>
    /// <returns>值类型属性保持默认值的 DTO</returns>
    public static ValueTypeJsonDto JsonFieldValueTypeSkipped(ValueTypeJsonEntity src)
        => Mapper.Map<ValueTypeJsonEntity, ValueTypeJsonDto>(src)!;

    /// <summary>
    /// 畸形 JSON — 故意触发 MapException（D7 裁决：IsFromJson 反序列化异常被包装，InnerException 保留 JsonException 诊断）。
    /// </summary>
    /// <param name="src">含非法 JSON 的实体</param>
    /// <returns>异常信息字符串</returns>
    public static string? JsonFieldMalformed(OrderConfigEntity src)
    {
        try
        {
            _ = Mapper.Map<OrderConfigEntity, OrderConfigDto>(src);
            return null;
        }
        catch (NCMapper.MapException ex) when (ex.InnerException is System.Text.Json.JsonException)
        {
            // D7 裁决后异常类型为 MapException，内部 JsonException 携带 Path/LineNumber 等诊断信息
            return $"MapException: {ex.Message} | Inner JsonException: {ex.InnerException.Message}";
        }
        catch (NCMapper.MapException ex)
        {
            return $"MapException: {ex.Message}";
        }
    }

    // ===== 4.2 集合索引路径运行时映射=====

    /// <summary>
    /// 集合索引路径运行时映射 — [MapProperty(typeof(T), "Items[0].Name")]。
    /// <para>三种输入：正常（取到 Items[0]）、空集合（default）、越界（Items[1] 超出 Count → default），
    /// 均不抛异常（ElementAtOrDefault 语义，与源生成器同形态）。</para>
    /// </summary>
    /// <returns>三种输入的映射结果汇总（First/Second 索引取值）</returns>
    public static string MapIndexPathRuntime()
    {
        // 正常：Items 有 2 个元素 → FirstItemName="A"，SecondItemName="B"
        var normal = new IndexOrderEntity
        {
            Id = 1,
            Items = { new() { Name = "A" }, new() { Name = "B" } },
        };
        var normalDto = Mapper.Map<IndexOrderEntity, IndexOrderDto>(normal)!;

        // 空集合：Items 为空 → FirstItemName / SecondItemName 均为 default(null)
        var empty = new IndexOrderEntity { Id = 2, Items = new() };
        var emptyDto = Mapper.Map<IndexOrderEntity, IndexOrderDto>(empty)!;

        // 越界：Items 只有 1 个元素 → SecondItemName（索引 1）越界 → default(null)
        var outOfRange = new IndexOrderEntity
        {
            Id = 3,
            Items = { new() { Name = "A" } },
        };
        var outOfRangeDto = Mapper.Map<IndexOrderEntity, IndexOrderDto>(outOfRange)!;

        return $"normal=[{normalDto.FirstItemName},{normalDto.SecondItemName}] " +
               $"empty=[{emptyDto.FirstItemName},{emptyDto.SecondItemName}] " +
               $"outOfRange=[{outOfRangeDto.FirstItemName},{outOfRangeDto.SecondItemName}]";
    }

    // ===== 4.3 反向写回点路径=====

    /// <summary>
    /// 反向写回点路径 — [MapProperty(typeof(T), "Author.Name")]。
    /// <para>DTO 扁平属性 AuthorName 写回实体嵌套路径 Author.Name：链存在则写叶值；
    /// 源值 null 且链存在 → 清空叶。</para>
    /// </summary>
    /// <returns>写回与清空两种场景的验证结果</returns>
    public static string ReverseWriteBackPath()
    {
        // 正常写回：AuthorName="Alice" → entity.Author.Name 覆盖为 "Alice"
        var dtoWrite = new ArticleDto { Id = 1, Title = "T1", AuthorName = "Alice" };
        var entityWrite = new ArticleEntity
        {
            Id = 1,
            Title = "T1",
            Author = new AuthorEntity { Name = "OLD" },
        };
        Mapper.Map<ArticleDto, ArticleEntity>(dtoWrite, entityWrite);
        var written = entityWrite.Author!.Name;

        // null 源清空叶（D5）：AuthorName=null 且 Author 链存在 → Author.Name 清空为 null
        var dtoClear = new ArticleDto { Id = 2, Title = "T2", AuthorName = null };
        var entityClear = new ArticleEntity
        {
            Id = 2,
            Title = "T2",
            Author = new AuthorEntity { Name = "OLD" },
        };
        Mapper.Map<ArticleDto, ArticleEntity>(dtoClear, entityClear);
        var cleared = entityClear.Author!.Name ?? "null";

        return $"write=[{written}] clearOnNull=[{cleared}]";
    }

    /// <summary>
    /// MapListInto 复用循环 — 预分配数组跨轮复用，每轮仅覆盖映射字段（分页/流式形态，见 how-to/mapping/map-list-into.md）。
    /// </summary>
    /// <param name="pages">多页源集合（逐轮填充同一目标数组）</param>
    /// <param name="dtos">预分配并初始化每个元素的目标数组（长度 ≥ 最大页元素数）</param>
    #region BasicMapping_MapListIntoReuse
    public static void MapListIntoReuse(List<List<UserEntity>> pages, UserDto[] dtos)
    {
        foreach (var page in pages)
        {
            Mapper.MapListInto<UserEntity, UserDto>(page, dtos);
        }
    }
    #endregion

    // ===== F3/F9 演示=====

    /// <summary>
    /// F3 — 反向映射默认不消费 [Convert]（NCMAP033 Info 提示）：
    /// 正向经转换器输出非成员名文本（转换器调用计数 +1）；反向按类型转换路径（内建 string→enum 解析）
    /// 写入——空串抛 MapException、成员名解析成功，且转换器调用计数不增长
    ///（证明反向走的是内建路径而非转换器）。
    /// </summary>
    /// <param name="src">源实体（Level=High）</param>
    /// <returns>(正向 DTO, 空串反向异常, 成员名反向实体, 转换器调用计数) 对照</returns>
    public static (F3Dto Forward, MapException? EmptyReverseEx, F3Entity MemberReverse, int CallCount) ConvertBackNotConsumed(F3Entity src)
    {
        F3Conv.CallCount = 0;
        var dto = Mapper.Map<F3Entity, F3Dto>(src)!;
        // 反向空串：内建路径抛 MapException；反向成员名：内建解析（ignoreCase）
        MapException? emptyReverseEx = null;
        try
        {
            Mapper.Map<F3Dto, F3Entity>(new F3Dto { Id = dto.Id, Level = "" });
        }
        catch (MapException ex)
        {
            emptyReverseEx = ex;
        }
        var memberReverse = Mapper.Map<F3Dto, F3Entity>(new F3Dto { Id = dto.Id, Level = "low" })!;
        return (dto, emptyReverseEx, memberReverse, F3Conv.CallCount);
    }

    /// <summary>
    /// F9 — string→enum 空串/可空语义：非成员名正常解析；空串抛 MapException；
    /// 可空枚举 null 反向保持 null（解析不依赖区域设置）。
    /// </summary>
    /// <param name="dto">string 侧 DTO（三种文本形态）</param>
    /// <returns>反向写回实体</returns>
    public static F9Entity StringEnumSemantics(F9Dto dto)
        => Mapper.Map<F9Dto, F9Entity>(dto)!;

        // ===== 场景注册入口=====
    public static void Run()
    {
    var src = new UserEntity { Id = 1, Name = "ZhangSan", Age = 30, Email = "zhangsan@example.com" };
    var list = new List<UserEntity>
    {
        new() { Id = 1, Name = "Alice", Age = 25, Email = "alice@test.com" },
        new() { Id = 2, Name = "Bob",   Age = 30, Email = "bob@test.com" },
    };

    OutputHelper.Dump("201-simple-name-match",   src, BasicScenarios.SimpleNameMatch(src));
    OutputHelper.Dump("201-list-map",             list, BasicScenarios.ListMap(list));
    OutputHelper.Dump("201-map-with-action",      src, BasicScenarios.MapWithAction(src));

    // MapListInto：预分配 + 复用循环演示（第二页仅 1 条，验证超量槽位保持原值 C3）
    var intoDtos = new UserDto[2];
    intoDtos[0] = new UserDto();
    intoDtos[1] = new UserDto();
    BasicScenarios.ListMapInto(list, intoDtos);
    OutputHelper.Dump("201-list-map-into",        list, intoDtos);
    var page2 = new List<UserEntity> { new() { Id = 3, Name = "Carol", Age = 28, Email = "carol@test.com" } };
    BasicScenarios.ListMapInto(page2, intoDtos);
    OutputHelper.Dump("201-list-map-into-reuse",  page2, intoDtos);

    // 复用循环（how-to/mapping/map-list-into.md「复用循环」节演示）：多页逐轮填充同一数组
    var reusePages = new List<List<UserEntity>> { list, page2 };
    BasicScenarios.MapListIntoReuse(reusePages, intoDtos);
    OutputHelper.Dump("201-list-map-into-reuse-loop", $"pages={reusePages.Count}", intoDtos);

    var existing = new UserDto { Id = 999, Name = "OLD" };
    BasicScenarios.UpdateExisting(src, existing);
    OutputHelper.Dump("201-update-existing",      src, existing);

    var detailSrc = new UserEntity
    {
        Id = 100,
        Name = "LiSi",
        Age = 28,
        Email = "lisi@example.com",
        Status = UserStatus.Active,
        CreatedAt = new DateTime(2025, 6, 15),
        Address = new Address { City = "Shanghai", Street = "Nanjing Road 100" },
    };
    OutputHelper.Dump("201-custom-property",      detailSrc, BasicScenarios.CustomPropertyAndConvert(detailSrc));

    var custEntity = new CustomerEntity
    {
        Name = "WangWu",
        Level = CustomerLevel.Gold,
        Phone = "13912345678",
        Region = "Beijing",
    };
    var custDto = BasicScenarios.ForwardMap(custEntity);
    OutputHelper.Dump("201-forward-map",           custEntity, custDto);
    var backEntity = BasicScenarios.BackwardMap(custDto);
    OutputHelper.Dump("201-backward-map",          custDto, backEntity);
    // 期望值：特性 [MapIgnore(Direction.ToEntity)] 移交断言——BasicMapping_MapIgnore region 契约：
    // DisplayLabel 被 ToEntity 忽略的物理形态 = CustomerEntity 侧无该属性（编译期即排除），反向映射不阻断、
    // 其余字段照常落位
    if (backEntity.Name != custDto.Name)
        OutputHelper.Error($"201-attr-map-ignore: [MapIgnore(ToEntity)] should not block reverse mapping of other fields (actual: Name={backEntity.Name})");

    OutputHelper.Dump("201-sourcegen-extension",   src, BasicScenarios.SourceGenExtension(src));
    OutputHelper.Dump("201-sourcegen-action",       src, BasicScenarios.SourceGenWithAction(src));

    var sgUpdateTarget = new UserDto { Id = 999, Name = "OLD" };
    BasicScenarios.SourceGenUpdateExisting(src, sgUpdateTarget);
    OutputHelper.Dump("201-sourcegen-update",       src, sgUpdateTarget);

    var sgList = BasicScenarios.SourceGenList(list);
    OutputHelper.Dump("201-sourcegen-list",         $"Count: {list.Count}", sgList);

    // 反向直调扩展
    var revDto = new UserDto { Id = 1, Name = "ZhangSan", Age = 30, Email = "zhangsan@example.com" };
    OutputHelper.Dump("201-reverse-direct",        revDto, BasicScenarios.ReverseDirectExtension(revDto));

    var revTarget = new UserEntity { Id = 999, Name = "OLD" };
    BasicScenarios.ReverseDirectUpdate(revDto, revTarget);
    OutputHelper.Dump("201-reverse-direct-update",  revDto, revTarget);

    var revDtos = new List<UserDto>
    {
        new() { Id = 1, Name = "Alice", Age = 25, Email = "alice@test.com" },
        new() { Id = 2, Name = "Bob",   Age = 30, Email = "bob@test.com" },
    };
    OutputHelper.Dump("201-reverse-direct-list",    $"Count: {revDtos.Count}", BasicScenarios.ReverseDirectList(revDtos));

    // [ConvertBack] 往返
    var roundTrip = BasicScenarios.ConvertBackRoundTrip(detailSrc);
    OutputHelper.Dump("201-convert-back-roundtrip", detailSrc, roundTrip);

    // JSON 字段映射
    var jsonEntity = new OrderConfigEntity
    {
        Id = 1001,
        OrderNo = "ORD-JSON",
        ShippingAddressJson = "{\"City\":\"Shanghai\",\"Street\":\"Nanjing Road 100\",\"ZipCode\":\"200001\"}",
        TagsJson = "[\"VIP\",\"Express\",\"Fragile\"]",
    };
    var jsonDto = BasicScenarios.JsonFieldToObject(jsonEntity);
    OutputHelper.Dump("201-json-to-object", jsonEntity.ShippingAddressJson, jsonDto.ShippingAddress);

    var jsonSource = new OrderConfigSource
    {
        Id = 1003,
        OrderNo = "ORD-REVERSE",
        ShippingAddress = new ShippingAddress { City = "Beijing", Street = "Chang'an Ave", ZipCode = "100000" },
    };
    var jsonReverse = BasicScenarios.JsonFieldReverse(jsonSource);
    OutputHelper.Dump("201-json-reverse", jsonSource.ShippingAddress, jsonReverse.ShippingAddressJson);

    var vtEntity = new ValueTypeJsonEntity { Id = 2001, ScoreJson = "99", DateJson = "\"2026-01-01\"" };
    var vtDto = BasicScenarios.JsonFieldValueTypeSkipped(vtEntity);
    OutputHelper.Dump("201-json-valuetype", $"Score default={vtDto.Score}, Date default={vtDto.Date:yyyy-MM-dd}", "");

    var malformed = BasicScenarios.JsonFieldMalformed(new OrderConfigEntity { Id = 999, ShippingAddressJson = "NOT JSON{{{" });
    OutputHelper.DumpExpectedFailure("201-json-malformed", "NOT JSON{{{ (malformed JSON) → MapException", malformed ?? "No exception");

    // 集合索引路径运行时映射
    OutputHelper.Dump("201-index-path-runtime", "normal / empty / out-of-range → ElementAtOrDefault semantics", BasicScenarios.MapIndexPathRuntime());

    // 反向写回点路径
    OutputHelper.Dump("201-reverse-writeback-path", "DTO flat property → entity nested path (null source clears leaf)", BasicScenarios.ReverseWriteBackPath());

    // F3：反向映射默认不消费 [Convert]
    var f3 = BasicScenarios.ConvertBackNotConsumed(new F3Entity { Id = 1, Level = F3Level.High });
    OutputHelper.DumpExpectedFailure("201-f3-convertback-not-consumed", f3.Forward.Level,
        $"empty→{(f3.EmptyReverseEx != null ? "MapException" : "no exception")}, member→{f3.MemberReverse.Level}, convCalls={f3.CallCount}");
    if (!f3.Forward.Level.Contains("LEVEL_") || f3.EmptyReverseEx == null
        || f3.MemberReverse.Level != F3Level.Low || f3.CallCount != 1)
        OutputHelper.Error("201-f3-convertback-not-consumed: semantics mismatch (forward should have converter output; reverse should not consume converter - empty string throws MapException, member name parsed by built-in path, converter call count unchanged)");

    // F9：string→enum 空串/可空语义（空串抛 MapException）
    var f9 = BasicScenarios.StringEnumSemantics(new F9Dto { Level = "High", Grade = null });
    OutputHelper.Dump("201-f9-string-enum", $"Level=High, Grade=null", new { f9.Level, f9.Grade });
    MapException? f9EmptyEx = null;
    try
    {
        BasicScenarios.StringEnumSemantics(new F9Dto { Level = "", Grade = "Low" });
    }
    catch (MapException ex)
    {
        f9EmptyEx = ex;
    }
    OutputHelper.DumpExpectedFailure("201-f9-string-enum-empty", "Level=\"\"",
        f9EmptyEx != null ? $"MapException: {f9EmptyEx.Message}" : "no exception (violates empty-string contract)");
    if (f9.Level != F9Level.High || f9.Grade != null || f9EmptyEx == null)
        OutputHelper.Error("201-f9-string-enum: empty-string/nullable semantics mismatch (normal value should parse, null should stay, empty string should throw MapException)");

        // 点路径与反向直调巡览
    var dotPathSrc = new UserEntity { Id = 1, Name = "Dave", Age = 40, Email = "dave@test.com", Address = new Address { City = "Beijing", Street = "Chang'an Ave" } };
    var dotPathDto = BasicScenarios.DotPath(dotPathSrc);
    OutputHelper.Dump("201-dot-path", dotPathSrc, dotPathDto);
    if (dotPathDto.AddressCity != "Beijing" || dotPathDto.AddressStreet != "Chang'an Ave")
        OutputHelper.Error("201-dot-path: flat properties did not pick up nested source path values (Address.City / Address.Street)");

    var reverseSrc = new UserDto { Id = 9, Name = "Eve", Age = 22, Email = "eve@test.com" };
    var reversed = BasicScenarios.ReverseExtensions(reverseSrc);
    OutputHelper.Dump("201-reverse-extensions", reverseSrc, reversed);
    // 返回的是③带回调新建形态：回调把 Name 写为 [Eve]——断言回调与反向映射同时生效
    if (reversed.Name != "[Eve]" || reversed.Email != "eve@test.com")
        OutputHelper.Error("201-reverse-extensions: reverse direct-call six-overload output fields mismatch (callback not applied or reverse mapping lost fields)");

    // ===== 移交断言（期望值来源：201 场景契约 + UserDetailDto 标注实读） =====
    // Map(src, action)：带回调重载（场景契约「回调」语义）
    var handoverSrc = new UserEntity { Id = 21, Name = "Handover", Age = 22, Email = "ho@test.com" };
    var withCallback = Mapper.Map<UserEntity, UserDto>(handoverSrc, (s, t) => t.Name = $"[{t.Name}]")!;
    if (withCallback.Name != "[Handover]")
        OutputHelper.Error($"201-map-with-action: action callback should rewrite Name (actual: {withCallback.Name})");

    // #3 Map(src, tgt)：更新已有对象重载（README 201 段「更新已有对象」）
    var updateTarget = new UserDto { Name = "keep-me" };
    Mapper.Map(handoverSrc, updateTarget);
    if (updateTarget.Name != "Handover")
        OutputHelper.Error($"201-map-update: Map(src, tgt) should overwrite target fields from source (actual: {updateTarget.Name})");

    // #5 MapList<S,T>(srcs)：列表映射（README 201 段「MapList」）
    var listSrc = new List<UserEntity> { handoverSrc, new() { Id = 22, Name = "Handover-2", Age = 23, Email = "ho2@test.com" } };
    var mappedList = Mapper.MapList<UserEntity, UserDto>(listSrc);
    if (mappedList.Count != 2 || mappedList[1].Name != "Handover-2")
        OutputHelper.Error($"201-map-list: MapList should map 2 items in order (actual: {mappedList.Count} items)");

    // #6 MapListInto<S,T>：复用预分配目标数组 + 越界槽位保持（README 201 段「MapListInto 复用」「越界槽位保持演示」；
    // 签名 Mapper.MapListInto<TSource, TTarget>(IList<TSource> sources, TTarget[] destination)）
    var destSlots = new UserDto[3];
    for (var i = 0; i < destSlots.Length; i++) destSlots[i] = new UserDto(); // 预分配每个槽位（调用方契约）
    destSlots[2].Name = "keep-slot";
    Mapper.MapListInto<UserEntity, UserDto>(listSrc, destSlots);
    if (destSlots[0].Name != "Handover" || destSlots[2].Name != "keep-slot")
        OutputHelper.Error($"201-map-list-into: should fill matching slots and keep overflow slots (actual: [0]={destSlots[0].Name}, [2]={destSlots[2].Name})");

    // #25 特性异名 [MapProperty(Name→DisplayName)]（UserDetailDto.cs:22；形态级缺口补强）
    var detailDto = Mapper.Map<UserEntity, UserDetailDto>(handoverSrc)!;
    if (detailDto.DisplayName != "Handover")
        OutputHelper.Error($"201-attr-renamed-map: [MapProperty] Name→DisplayName should map (actual: {detailDto.DisplayName})");

    // #26 特性 [Convert]（UserDetailDto.cs:46 RegisterDate ← CreatedAt 经 DateTimeFormatter；形态级缺口补强）
    if (detailDto.RegisterDate == "")
        OutputHelper.Error("201-attr-convert: [Convert] DateTimeFormatter should produce non-empty RegisterDate");

    // #39 特性 IsFromJson（JsonFieldModels.cs:54 ShippingAddressJson→ShippingAddress 反序列化；形态级缺口补强）
    var ocDto = BasicScenarios.JsonFieldToObject(new OrderConfigEntity
    {
        Id = 1,
        OrderNo = "OC-1",
        ShippingAddressJson = "{\"City\":\"JsonCity\",\"Street\":\"JsonSt\"}",
        TagsJson = "[\"t1\",\"t2\"]",
    })!;
    if (ocDto.ShippingAddress == null || ocDto.ShippingAddress.City != "JsonCity")
        OutputHelper.Error($"201-attr-is-from-json: IsFromJson should deserialize ShippingAddressJson (actual: {ocDto.ShippingAddress?.City ?? "null"})");
    if (ocDto.Tags == null || ocDto.Tags.Count != 2)
        OutputHelper.Error($"201-attr-is-from-json: IsFromJson should deserialize TagsJson to 2 tags (actual: {ocDto.Tags?.Count ?? 0})");
    }

    // ===== 6. 点路径与反向扩展巡览=====

    /// <summary>
    /// 点路径与扁平化：嵌套源路径取值——AddressCity ← Address.City（PascalCase 扁平化）。
    /// </summary>
    /// <param name="src">含嵌套 Address 的源实体</param>
    /// <returns>扁平化 DTO</returns>
    #region BasicMapping_DotPath
    public static UserDotPathDto DotPath(UserEntity src)
        => Mapper.Map<UserEntity, UserDotPathDto>(src)!;
    #endregion

    /// <summary>
    /// 反向直调六重载巡览：源生成器为每个 [MapFrom] DTO 生成的 dto.MapToXxxEntity 族
    /// （①新建 ②更新已有 ③带回调新建 ④带回调更新 ⑤列表新建 ⑥带回调列表）。
    /// </summary>
    /// <param name="src">源 DTO</param>
    /// <returns>反向映射产物（带回调新建形态）</returns>
    #region BasicMapping_ReverseExtensions
    public static UserEntity ReverseExtensions(UserDto src)
    {
        var created = src.MapToUserEntity();                                      // (1) create a new entity
        src.MapToUserEntity(created);                                             // (2) update an existing entity
        var withAction = src.MapToUserEntity((s, t) => t.Name = $"[{s.Name}]");   // (3) create with callback
        src.MapToUserEntity(created, (s, t) => { });                              // (4) update with callback
        _ = new List<UserDto> { src }.MapToUserEntityList();                      // (5) list create
        _ = new List<UserDto> { src }.MapToUserEntityList((s, t) => { });         // (6) list create with callback
        return withAction!;
    }
    #endregion

    /// <summary>源生成器入口四形态巡览：零委托直调 / 带 Action 回调 / 列表 / 更新已有（JIT 可内联，热路径最优）。</summary>
    #region Core_Generated_EntryExtensions
    public static void GeneratedEntryTour(UserEntity src, UserDto existing, List<UserEntity> srcs)
    {
        // Zero-delegate direct call (JIT-inlinable, hottest path)
        _ = src.MapToUserDto();
        // With Action callback
        _ = src.MapToUserDto((s, t) => t.Name = t.Name.ToUpper());
        // List mapping
        _ = srcs.MapToUserDtoList();
        // Update an existing target (void, zero allocation)
        src.MapToUserDto(existing);
    }
    #endregion

    /// <summary>正反双向更新巡览：正向更新已有（零堆分配）+ 反向新建 / 反向更新 / 带回调更新（CRUD 写回场景）。</summary>
    #region Declarative_ReverseMap_Pair
    public static void ReverseMapPair(UserEntity entity, UserDto dto, UserDto existingDto, UserEntity existingUser)
    {
        // Forward update in place (zero heap allocation, CRUD friendly)
        entity.MapToUserDto(existingDto);
        // Reverse create (DTO -> Entity)
        _ = dto.MapToUserEntity();
        // Reverse update an existing entity (edit-form submit, the primary scenario)
        dto.MapToUserEntity(existingUser);
        // Forward update with callback
        entity.MapToUserDto(existingDto, (src, tgt) => tgt.Name = $"[Updated] {src.Name}");
    }
    #endregion

    /// <summary>反向直调三形态：新建实体 / 就地更新已有实体（保留跟踪信息）/ 带回调更新（回写后补时间戳）。</summary>
    #region Declarative_ReverseMap_New
    public static void ReverseMapTrio(UserDto dto, UserEntity entity)
    {
        // Create: a fresh entity from the submitted DTO
        _ = dto.MapToUserEntity();
        // Update in place: the entity loaded by the edit page keeps its tracking info
        dto.MapToUserEntity(entity);
        // Update with callback: touch a timestamp after write-back
        dto.MapToUserEntity(entity, (s, t) => t.CreatedAt = DateTime.Now);
    }
    #endregion
}

/// <summary>点路径演示 DTO（扁平属性 ← 嵌套源路径，特性通道域内模型）。</summary>
[MapFrom(typeof(UserEntity))]
public class UserDotPathDto
{
    /// <summary>扁平属性：嵌套源路径 Address.City。</summary>
    public string? AddressCity { get; set; }

    /// <summary>扁平属性：嵌套源路径 Address.Street。</summary>
    public string? AddressStreet { get; set; }
}
