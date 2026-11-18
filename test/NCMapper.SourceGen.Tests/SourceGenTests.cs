using Xunit;
using NCMapper.SourceGen.Tests.Models;
using NCMapper.Generated;

namespace NCMapper.SourceGen.Tests
{
    /// <summary>
    /// 源生成器功能测试 — 验证编译时生成的映射方法与运行时 Mapper 行为一致
    /// </summary>
    public class SourceGenTests
    {
        /// <summary>
        /// 扁平映射所有属性正确
        /// </summary>
        [Fact]
        public void SgFlat_Map_All_Properties_Correct()
        {
            var entity = new SgFlatEntity
            {
                Id = 100, Name = "Alice", Age = 30, Salary = 999.99m,
                CreatedAt = new DateTime(2025, 6, 27), IsActive = true,
                Score = 99.5, UserId = 888888L, Guid = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffffffffffff")
            };

            var dto = GeneratedMapper.Map_SgFlatEntity_To_SgFlatDto(entity)!;

            Assert.Equal(100, dto.Id);
            Assert.Equal("Alice", dto.Name);
            Assert.Equal(30, dto.Age);
            Assert.Equal(999.99m, dto.Salary);
            Assert.Equal(new DateTime(2025, 6, 27), dto.CreatedAt);
            Assert.True(dto.IsActive);
            Assert.Equal(99.5, dto.Score);
            Assert.Equal(888888L, dto.UserId);
            Assert.Equal(entity.Guid, dto.Guid);
        }

        /// <summary>
        /// 源为 null 返回 null
        /// </summary>
        [Fact]
        public void SgFlat_Map_Null_Returns_Null()
        {
            var dto = GeneratedMapper.Map_SgFlatEntity_To_SgFlatDto(null!);
            Assert.Null(dto);
        }

        /// <summary>
        /// 列表映射 — null 元素保留为 null，数量和索引一致
        /// </summary>
        [Fact]
        public void SgFlat_MapList_All_Elements()
        {
            var entities = new List<SgFlatEntity>
            {
                new() { Id = 1, Name = "A" },
                new() { Id = 2, Name = "B" },
                null!,
                new() { Id = 3, Name = "C" }
            };

            var dtos = GeneratedMapper.MapList_SgFlatEntity_To_SgFlatDto(entities)!;

            Assert.Equal(4, dtos.Count);
            Assert.Equal(1, dtos[0].Id);
            Assert.Null(dtos[2]);
            Assert.Equal("C", dtos[3].Name);
        }

        /// <summary>
        /// 异名字段映射 — [MapProperty] 标注的字段正确映射
        /// </summary>
        [Fact]
        public void SgNameDiff_Map_All_Properties_Correct()
        {
            var entity = new SgFlatEntity
            {
                Id = 200, Name = "Bob", Age = 25, Salary = 123.45m,
                CreatedAt = new DateTime(2024, 1, 1)
            };

            var dto = GeneratedMapper.Map_SgFlatEntity_To_SgNameDiffDto(entity)!;

            Assert.Equal(200, dto.Id);
            Assert.Equal("Bob", dto.FullName);
            Assert.Equal(25, dto.UserAge);
            Assert.Equal(200L, dto.AccountId);
            Assert.Equal(new DateTime(2024, 1, 1), dto.CreatedAt);
            Assert.Equal(123.45m, dto.TotalAmount);
        }

        /// <summary>
        /// 源生成器结果与运行时 Mapper 一致
        /// </summary>
        [Fact]
        public void SgFlat_Map_Matches_Runtime_Mapper()
        {
            var entity = new SgFlatEntity
            {
                Id = 42, Name = "Verify", Age = 99, Salary = 1m,
                CreatedAt = DateTime.Now, IsActive = false,
                Score = 50.5, UserId = 1L, Guid = Guid.NewGuid()
            };

            var sgDto = GeneratedMapper.Map_SgFlatEntity_To_SgFlatDto(entity)!;
            var rtDto = Mapper.Map<SgFlatEntity, SgFlatDto>(entity)!;

            Assert.Equal(rtDto.Id, sgDto.Id);
            Assert.Equal(rtDto.Name, sgDto.Name);
            Assert.Equal(rtDto.Age, sgDto.Age);
            Assert.Equal(rtDto.Salary, sgDto.Salary);
            Assert.Equal(rtDto.IsActive, sgDto.IsActive);
            Assert.Equal(rtDto.Score, sgDto.Score);
            Assert.Equal(rtDto.UserId, sgDto.UserId);
            Assert.Equal(rtDto.Guid, sgDto.Guid);
        }

        /// <summary>
        /// 异名字段列表映射
        /// </summary>
        [Fact]
        public void SgNameDiff_MapList_All_Elements()
        {
            var entities = new List<SgFlatEntity>
            {
                new() { Id = 1, Name = "A", Salary = 10m },
                new() { Id = 2, Name = "B", Salary = 20m },
            };

            var dtos = GeneratedMapper.MapList_SgFlatEntity_To_SgNameDiffDto(entities)!;

            Assert.Equal(2, dtos.Count);
            Assert.Equal("A", dtos[0].FullName);
            Assert.Equal(10m, dtos[0].TotalAmount);
            Assert.Equal(1L, dtos[0].AccountId);
        }

        /// <summary>
        /// 异名字段 null 源返回 null
        /// </summary>
        [Fact]
        public void SgNameDiff_Map_Null_Returns_Null()
        {
            var dto = GeneratedMapper.Map_SgFlatEntity_To_SgNameDiffDto(null!);
            Assert.Null(dto);
        }

        /// <summary>
        /// 扁平 MapList null 源返回 null
        /// </summary>
        [Fact]
        public void SgFlat_MapList_Null_Returns_Null()
        {
            var dtos = GeneratedMapper.MapList_SgFlatEntity_To_SgFlatDto(null!);
            Assert.Null(dtos);
        }

        /// <summary>
        /// 空列表 MapList 返回空列表
        /// </summary>
        [Fact]
        public void SgFlat_MapList_Empty_Returns_Empty()
        {
            var dtos = GeneratedMapper.MapList_SgFlatEntity_To_SgFlatDto(new List<SgFlatEntity>())!;
            Assert.Empty(dtos);
        }

        /// <summary>
        /// [MapIgnore] 属性被跳过
        /// </summary>
        [Fact]
        public void SgIgnore_Map_Skips_MapIgnore_Property()
        {
            var entity = new SgIgnoreEntity { Id = 1, Name = "Test", Computed = "should skip" };

            var dto = GeneratedMapper.Map_SgIgnoreEntity_To_SgIgnoreDto(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("Test", dto.Name);
            Assert.Null(dto.Computed); // [MapIgnore] 跳过
        }

        /// <summary>
        /// [MapIgnore(Direction.Both)] 反向映射也跳过 — DTO→Entity 时 Computed 不映射
        /// </summary>
        [Fact]
        public void SgIgnore_Reverse_Map_Skips_MapIgnore_Property()
        {
            var dto = new SgIgnoreDto { Id = 2, Name = "Reverse", Computed = "should skip too" };

            var entity = GeneratedMapper.Map_SgIgnoreDto_To_SgIgnoreEntity(dto)!;

            Assert.Equal(2, entity.Id);
            Assert.Equal("Reverse", entity.Name);
            // [MapIgnore(Direction.Both)] 反向也跳过 — Computed 保持实体默认值
            Assert.Equal("should not map", entity.Computed);
        }

        /// <summary>
        /// [MapIgnore(Direction.Both)] 往返一致 — 正反向都跳过 Computed
        /// </summary>
        [Fact]
        public void SgIgnore_Both_Direction_RoundTrip_Skips_Computed()
        {
            var original = new SgIgnoreEntity { Id = 10, Name = "RoundTrip", Computed = "original_entity" };

            // 正向：Entity→DTO，Computed 跳过
            var dto = GeneratedMapper.Map_SgIgnoreEntity_To_SgIgnoreDto(original)!;
            Assert.Equal(10, dto.Id);
            Assert.Null(dto.Computed);

            // 设置 DTO 的 Computed 值
            dto.Computed = "dto_value";

            // 反向：DTO→Entity，Computed 跳过
            var roundTrip = GeneratedMapper.Map_SgIgnoreDto_To_SgIgnoreEntity(dto)!;
            Assert.Equal(10, roundTrip.Id);
            Assert.Equal("RoundTrip", roundTrip.Name);
            // Computed 不从 DTO 映射，保持实体默认值
            Assert.Equal("should not map", roundTrip.Computed);
        }

        // ================================================================
        // MapIgnore Direction 方向控制测试
        // ================================================================

        /// <summary>
        /// [MapIgnore(Direction.ToDto)] 正向跳过，反向不跳过
        /// </summary>
        [Fact]
        public void SgDirection_ToDto_Forward_Skips_Reverse_Not()
        {
            var entity = new SgDirectionEntity { Id = 1, Name = "Test", ToDtoOnly = "skipped", ToEntityOnly = "kept" };

            // 正向 Entity→DTO：ToDtoOnly 被 [MapIgnore(Direction.ToDto)] 跳过
            var dto = GeneratedMapper.Map_SgDirectionEntity_To_SgDirectionDto(entity)!;
            Assert.Equal(1, dto.Id);
            Assert.Equal("Test", dto.Name);
            Assert.Null(dto.ToDtoOnly);     // ToDto 方向跳过
            Assert.Equal("kept", dto.ToEntityOnly); // ToEntity 方向不跳过
        }

        /// <summary>
        /// [MapIgnore(Direction.ToEntity)] 正向不跳过，反向跳过
        /// </summary>
        [Fact]
        public void SgDirection_ToEntity_Forward_Not_Reverse_Skips()
        {
            var dto = new SgDirectionDto { Id = 2, Name = "Rev", ToDtoOnly = "kept", ToEntityOnly = "should_skip" };

            // 反向 DTO→Entity：ToEntityOnly 被 [MapIgnore(Direction.ToEntity)] 跳过
            var entity = GeneratedMapper.Map_SgDirectionDto_To_SgDirectionEntity(dto)!;
            Assert.Equal(2, entity.Id);
            Assert.Equal("Rev", entity.Name);
            Assert.Equal("kept", entity.ToDtoOnly);   // ToDto 方向不跳过
            Assert.Equal("entity_skip", entity.ToEntityOnly); // ToEntity 方向跳过 → 保持实体初始值
        }

        // ================================================================
        // 嵌套对象映射测试
        // ================================================================

        /// <summary>
        /// 嵌套对象映射 — 子对象属性正确映射
        /// </summary>
        [Fact]
        public void SgNested_Map_Child_Properties_Correct()
        {
            var entity = new SgNestedEntity
            {
                Id = 1,
                Name = "Parent",
                Child = new SgInnerEntity { City = "Beijing", Street = "Chang'an Ave", ZipCode = 100000 }
            };

            var dto = GeneratedMapper.Map_SgNestedEntity_To_SgNestedDto(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("Parent", dto.Name);
            Assert.NotNull(dto.Child);
            Assert.Equal("Beijing", dto.Child!.City);
            Assert.Equal("Chang'an Ave", dto.Child.Street);
            Assert.Equal(100000, dto.Child.ZipCode);
        }

        /// <summary>
        /// 嵌套对象映射 — 子对象为 null 时 DTO 子对象也为 null
        /// </summary>
        [Fact]
        public void SgNested_Map_Null_Child_Returns_Null()
        {
            var entity = new SgNestedEntity { Id = 2, Name = "NoChild", Child = null };

            var dto = GeneratedMapper.Map_SgNestedEntity_To_SgNestedDto(entity)!;

            Assert.Equal(2, dto.Id);
            Assert.Equal("NoChild", dto.Name);
            Assert.Null(dto.Child);
        }

        /// <summary>
        /// 嵌套对象列表映射
        /// </summary>
        [Fact]
        public void SgNested_MapList_Child_Properties_Correct()
        {
            var entities = new List<SgNestedEntity>
            {
                new() { Id = 1, Name = "A", Child = new SgInnerEntity { City = "C1", Street = "S1", ZipCode = 1 } },
                new() { Id = 2, Name = "B", Child = null },
                new() { Id = 3, Name = "C", Child = new SgInnerEntity { City = "C3", Street = "S3", ZipCode = 3 } }
            };

            var dtos = GeneratedMapper.MapList_SgNestedEntity_To_SgNestedDto(entities)!;

            Assert.Equal(3, dtos.Count);
            Assert.Equal("C1", dtos[0].Child!.City);
            Assert.Null(dtos[1].Child);
            Assert.Equal(3, dtos[2].Child!.ZipCode);
        }

        // ================================================================
        // 多源合成 DTO 测试（跨表字段）
        // ================================================================

        /// <summary>
        /// 多源合成 DTO — 跨表字段跳过源生成器，运行时 Compose 处理
        /// </summary>
        [Fact]
        public void SgMultiSource_Map_Primary_Source_Fields_Correct()
        {
            // 源生成器只映射主源（SgOrderEntity）的字段，跨表字段（SgCustomerEntity）跳过
            var entity = new SgOrderEntity { Id = 1001, Amount = 299.99m };

            var dto = GeneratedMapper.Map_SgOrderEntity_To_SgMultiSourceDto(entity)!;

            Assert.Equal(1001, dto.OrderId);
            Assert.Equal(299.99m, dto.OrderAmount);
            // 跨表字段未被源生成器映射，保持默认值
            Assert.Equal("", dto.CustomerName);
            Assert.Equal(0, dto.CustomerLevel);
        }

        /// <summary>
        /// 多源合成 — 运行时 Compose 填充跨表字段
        /// </summary>
        [Fact]
        public void SgMultiSource_Compose_Fills_CrossTable_Fields()
        {
            var order = new SgOrderEntity { Id = 2002, Amount = 500m };
            var customer = new SgCustomerEntity { Name = "LiSi", Level = 5 };

            var dto = Mapper.Compose<SgMultiSourceDto>(order, customer)!;

            Assert.Equal(2002, dto.OrderId);
            Assert.Equal(500m, dto.OrderAmount);
            Assert.Equal("LiSi", dto.CustomerName);
            Assert.Equal(5, dto.CustomerLevel);
        }

        // ================================================================
        // 集合属性映射测试（DTO 属性为集合类型）
        // ================================================================

        /// <summary>
        /// 集合属性映射 — List 属性正确映射
        /// </summary>
        [Fact]
        public void SgCollection_Map_List_Property_Correct()
        {
            var entity = new SgCollectionEntity
            {
                Id = 1,
                Name = "WithItems",
                Items = new List<SgCollectionItemEntity>
                {
                    new() { Id = 10, Name = "Item10" },
                    new() { Id = 20, Name = "Item20" }
                }
            };

            var dto = GeneratedMapper.Map_SgCollectionEntity_To_SgCollectionDto(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("WithItems", dto.Name);
            Assert.NotNull(dto.Items);
            Assert.Equal(2, dto.Items!.Count);
            Assert.Equal(10, dto.Items[0].Id);
            Assert.Equal("Item20", dto.Items[1].Name);
        }

        /// <summary>
        /// 集合属性映射 — null 集合属性保持 null
        /// </summary>
        [Fact]
        public void SgCollection_Map_Null_List_Property()
        {
            var entity = new SgCollectionEntity { Id = 2, Name = "NoItems", Items = null };

            var dto = GeneratedMapper.Map_SgCollectionEntity_To_SgCollectionDto(entity)!;

            Assert.Equal(2, dto.Id);
            Assert.Equal("NoItems", dto.Name);
            Assert.Null(dto.Items);
        }

        // ================================================================
        // 双向映射测试
        // ================================================================

        /// <summary>
        /// 双向映射 — 反向 DTO→Entity 正确
        /// </summary>
        [Fact]
        public void SgFlat_Reverse_Map_Dto_To_Entity_Correct()
        {
            var dto = new SgFlatDto
            {
                Id = 999, Name = "Reverse", Age = 50, Salary = 100m,
                CreatedAt = new DateTime(2025, 1, 1), IsActive = false,
                Score = 60.5, UserId = 777L, Guid = Guid.Parse("00000000-0000-0000-0000-000000000001")
            };

            var entity = GeneratedMapper.Map_SgFlatDto_To_SgFlatEntity(dto)!;

            Assert.Equal(999, entity.Id);
            Assert.Equal("Reverse", entity.Name);
            Assert.Equal(50, entity.Age);
            Assert.Equal(100m, entity.Salary);
            Assert.Equal(new DateTime(2025, 1, 1), entity.CreatedAt);
            Assert.False(entity.IsActive);
            Assert.Equal(60.5, entity.Score);
            Assert.Equal(777L, entity.UserId);
        }

        /// <summary>
        /// 双向映射 — 往返一致
        /// </summary>
        [Fact]
        public void SgFlat_RoundTrip_Entity_Dto_Entity_Consistent()
        {
            var original = new SgFlatEntity
            {
                Id = 555, Name = "Round", Age = 33, Salary = 777.7m,
                CreatedAt = new DateTime(2024, 7, 1), IsActive = true,
                Score = 88.8, UserId = 666L, Guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            };

            var dto = GeneratedMapper.Map_SgFlatEntity_To_SgFlatDto(original)!;
            var roundTrip = GeneratedMapper.Map_SgFlatDto_To_SgFlatEntity(dto)!;

            Assert.Equal(original.Id, roundTrip.Id);
            Assert.Equal(original.Name, roundTrip.Name);
            Assert.Equal(original.Age, roundTrip.Age);
            Assert.Equal(original.Salary, roundTrip.Salary);
            Assert.Equal(original.CreatedAt, roundTrip.CreatedAt);
            Assert.Equal(original.IsActive, roundTrip.IsActive);
            Assert.Equal(original.Score, roundTrip.Score);
            Assert.Equal(original.UserId, roundTrip.UserId);
            Assert.Equal(original.Guid, roundTrip.Guid);
        }

        // ================================================================
        // Action 回调测试
        // ================================================================

        /// <summary>
        /// Action 回调 — 映射后修改目标属性
        /// </summary>
        [Fact]
        public void SgFlat_Map_With_Action_Modifies_Target()
        {
            var entity = new SgFlatEntity { Id = 1, Name = "Before" };

            var dto = GeneratedMapper.Map_SgFlatEntity_To_SgFlatDto_WithAction(entity, (s, d) =>
            {
                d.Name = "After";
                d.Age = s.Age + 10;
            })!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("After", dto.Name);
            Assert.Equal(40, dto.Age); // 30 + 10
        }

        /// <summary>
        /// Action 回调 — null action 正常工作
        /// </summary>
        [Fact]
        public void SgFlat_Map_With_Null_Action_Works()
        {
            var entity = new SgFlatEntity { Id = 1, Name = "Test" };

            var dto = GeneratedMapper.Map_SgFlatEntity_To_SgFlatDto(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("Test", dto.Name);
        }

        // ================================================================
        // 自动扁平化测试
        // ================================================================

        /// <summary>
        /// 自动扁平化 — ChildName ← Child.Name
        /// </summary>
        [Fact]
        public void SgAutoFlatten_Map_Nested_Properties_Correct()
        {
            var entity = new SgFlatParentEntity
            {
                Id = 1,
                Child = new SgFlatChildEntity { Name = "Nested", Level = 7 }
            };

            var dto = GeneratedMapper.Map_SgFlatParentEntity_To_SgFlatParentDto(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("Nested", dto.ChildName);
        }

        /// <summary>
        /// 自动扁平化 — Child 为 null 时 ChildName 为 null（?. 安全访问）
        /// </summary>
        [Fact]
        public void SgAutoFlatten_Null_Child_Keeps_Default()
        {
            var entity = new SgFlatParentEntity { Id = 2, Child = null };

            var dto = GeneratedMapper.Map_SgFlatParentEntity_To_SgFlatParentDto(entity)!;

            Assert.Equal(2, dto.Id);
            Assert.Null(dto.ChildName);  // src.Child?.Name 返回 null
        }

        // ================================================================
        // 枚举编译时映射测试
        // ================================================================

        /// <summary>
        /// 枚举→字符串映射
        /// </summary>
        [Fact]
        public void SgEnum_ToString_Map_Correct()
        {
            var entity = new SgEnumEntity { Id = 1, Status = SgStatus.Pending };

            var dto = GeneratedMapper.Map_SgEnumEntity_To_SgEnumToStringDto(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Equal("Pending", dto.Status);
        }

        /// <summary>
        /// 字符串→枚举映射
        /// </summary>
        [Fact]
        public void SgString_ToEnum_Map_Correct()
        {
            var entity = new SgEnumEntity { Id = 2, Status = SgStatus.Active };

            var dto = GeneratedMapper.Map_SgEnumEntity_To_SgStringToEnumDto(entity)!;

            Assert.Equal(2, dto.Id);
            Assert.Equal(SgStatus.Active, dto.Status);
        }

        /// <summary>
        /// 枚举→字符串 — 所有枚举值正确
        /// </summary>
        [Fact]
        public void SgEnum_ToString_All_Values_Correct()
        {
            foreach (var status in Enum.GetValues<SgStatus>())
            {
                var entity = new SgEnumEntity { Id = 0, Status = status };
                var dto = GeneratedMapper.Map_SgEnumEntity_To_SgEnumToStringDto(entity)!;
                Assert.Equal(status.ToString(), dto.Status);
            }
        }

        // ================================================================
        // record 构造器映射测试
        // ================================================================

        /// <summary>
        /// record 类型映射 — 构造器参数正确匹配
        /// </summary>
        /// <summary>
        /// 回归（H-05）：别名枚举 enum→string 退化为 ToString()——
        /// 生成代码不再产生不可达 switch arm（CS8510 编译失败），输出与 Enum.ToString() 一致
        /// （取第一个成员名 "A"），同值别名成员输出一致。
        /// </summary>
        [Fact]
        public void SgAliasedEnum_ToString_Degrades_ConsistentWithToString()
        {
            var dtoA = GeneratedMapper.Map_SgAliasedEnumEntity_To_SgAliasedEnumToStringDto(
                new SgAliasedEnumEntity { Id = 1, Status = SgAliasedEnum.A })!;
            var dtoB = GeneratedMapper.Map_SgAliasedEnumEntity_To_SgAliasedEnumToStringDto(
                new SgAliasedEnumEntity { Id = 2, Status = SgAliasedEnum.B })!;

            Assert.Equal(SgAliasedEnum.A.ToString(), dtoA.Status);
            Assert.Equal(SgAliasedEnum.B.ToString(), dtoB.Status);
            Assert.Equal(dtoA.Status, dtoB.Status); // 同值别名成员输出一致（均为 ToString 的首名 "A"）
        }

        [Fact]
        public void SgRecord_Map_Constructor_Params_Correct()
        {
            var entity = new SgRecordEntity(42, "Record", 25);

            var dto = Mapper.Map<SgRecordEntity, SgRecordDto>(entity)!;

            Assert.Equal(42, dto.Id);
            Assert.Equal("Record", dto.Name);
            Assert.Equal(25, dto.Age);
        }

        // ================================================================
        // null 守卫测试
        // ================================================================

        /// <summary>
        /// null 守卫 — 嵌套属性为 null 时不抛异常，子属性保持默认
        /// </summary>
        [Fact]
        public void SgNullGuard_Null_Child_No_Exception()
        {
            var entity = new SgNullGuardEntity { Id = 1, Child = null };

            var dto = Mapper.Map<SgNullGuardEntity, SgNullGuardDto>(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.Null(dto.ChildName);
        }

        /// <summary>
        /// null 守卫 — 嵌套属性非 null 时正确映射
        /// </summary>
        [Fact]
        public void SgNullGuard_NonNull_Child_Maps_Correct()
        {
            var entity = new SgNullGuardEntity
            {
                Id = 2,
                Child = new SgFlatChildEntity { Name = "Guard", Level = 9 }
            };

            var dto = Mapper.Map<SgNullGuardEntity, SgNullGuardDto>(entity)!;

            Assert.Equal(2, dto.Id);
            Assert.Equal("Guard", dto.ChildName);
        }

        // ================================================================
        // 接口类型属性映射测试
        // ================================================================

        /// <summary>
        /// IReadOnlyList<T> 接口属性正确映射
        /// </summary>
        [Fact]
        public void SgInterface_IReadOnlyList_Maps_Correct()
        {
            var entity = new SgInterfaceEntity { Id = 1, Items = new List<SgCollectionItemEntity> { new() { Id = 10, Name = "A" } } };

            var dto = Mapper.Map<SgInterfaceEntity, SgInterfaceDto>(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.NotNull(dto.Items);
            Assert.Single(dto.Items!);
            Assert.Equal(10, dto.Items[0].Id);
            Assert.Equal("A", dto.Items[0].Name);
        }

        /// <summary>
        /// IReadOnlyList<T> — null 属性保持 null
        /// </summary>
        [Fact]
        public void SgInterface_Null_List_Keeps_Null()
        {
            var entity = new SgInterfaceEntity { Id = 2, Items = null! };

            var dto = Mapper.Map<SgInterfaceEntity, SgInterfaceDto>(entity)!;

            Assert.Equal(2, dto.Id);
            Assert.Null(dto.Items);
        }

        // ================================================================
        // Dictionary 属性映射测试
        // ================================================================

        /// <summary>
        /// Dictionary&lt;string,int&gt; → Dictionary&lt;string,int&gt; 同类型 KV 拷贝
        /// </summary>
        [Fact]
        public void SgDict_SameKVType_Copied()
        {
            var entity = new SgDictEntity
            {
                Id = 1,
                TagCounts = new Dictionary<string, int>
                {
                    ["csharp"] = 42,
                    ["dotnet"] = 35,
                    ["aspnet"] = 28
                }
            };

            var dto = GeneratedMapper.Map_SgDictEntity_To_SgDictDto(entity)!;

            Assert.Equal(1, dto.Id);
            Assert.NotNull(dto.TagCounts);
            Assert.Equal(3, dto.TagCounts!.Count);
            Assert.Equal(42, dto.TagCounts["csharp"]);
            Assert.Equal(35, dto.TagCounts["dotnet"]);
            Assert.Equal(28, dto.TagCounts["aspnet"]);
        }

        /// <summary>
        /// Dictionary&lt;string,Post&gt; → Dictionary&lt;string,PostDto&gt; 值类型跨类型映射
        /// </summary>
        [Fact]
        public void SgDict_CrossTypeValue_Mapped()
        {
            var entity = new SgDictCrossTypeEntity
            {
                Id = 2,
                PostsByCategory = new Dictionary<string, SgCollectionItemEntity>
                {
                    ["tech"] = new SgCollectionItemEntity { Id = 10, Name = "AI Breakthroughs" },
                    ["life"] = new SgCollectionItemEntity { Id = 20, Name = "Morning Routine" }
                }
            };

            var dto = GeneratedMapper.Map_SgDictCrossTypeEntity_To_SgDictCrossTypeDto(entity)!;

            Assert.Equal(2, dto.Id);
            Assert.NotNull(dto.PostsByCategory);
            Assert.Equal(2, dto.PostsByCategory!.Count);
            Assert.Equal(10, dto.PostsByCategory["tech"].Id);
            Assert.Equal("AI Breakthroughs", dto.PostsByCategory["tech"].Name);
            Assert.Equal(20, dto.PostsByCategory["life"].Id);
            Assert.Equal("Morning Routine", dto.PostsByCategory["life"].Name);
        }

        /// <summary>
        /// null Dictionary 属性不抛异常，目标保持 null
        /// </summary>
        [Fact]
        public void SgDict_Null_Dictionary_No_Exception()
        {
            var entity = new SgDictEntity { Id = 3, TagCounts = null };

            var dto = GeneratedMapper.Map_SgDictEntity_To_SgDictDto(entity)!;

            Assert.Equal(3, dto.Id);
            Assert.Null(dto.TagCounts);
        }

        /// <summary>
        /// 空 Dictionary 属性生成空 Dictionary
        /// </summary>
        [Fact]
        public void SgDict_Empty_Dictionary_Returns_Empty()
        {
            var entity = new SgDictEntity
            {
                Id = 4,
                TagCounts = new Dictionary<string, int>()
            };

            var dto = GeneratedMapper.Map_SgDictEntity_To_SgDictDto(entity)!;

            Assert.Equal(4, dto.Id);
            Assert.NotNull(dto.TagCounts);
            Assert.Empty(dto.TagCounts!);
        }

        /// <summary>
        /// Dictionary → IDictionary 接口目标属性正确映射
        /// </summary>
        [Fact]
        public void SgDict_Interface_Target_Maps_Correct()
        {
            var entity = new SgDictEntity
            {
                Id = 5,
                TagCounts = new Dictionary<string, int>
                {
                    ["a"] = 1,
                    ["b"] = 2
                }
            };

            var dto = GeneratedMapper.Map_SgDictEntity_To_SgDictInterfaceDto(entity)!;

            Assert.Equal(5, dto.Id);
            Assert.NotNull(dto.TagCounts);
            Assert.Equal(2, dto.TagCounts!.Count);
            Assert.Equal(1, dto.TagCounts["a"]);
            Assert.Equal(2, dto.TagCounts["b"]);
        }

        #region ConvertUsing 转换器运行时测试

        /// <summary>
        /// 源生成器路径 — ConvertUsing 转换器全部属性正确映射
        /// </summary>
        [Fact]
        public void SgConverter_Map_All_Properties_Correct()
        {
            var entity = new SgConverterEntity
            {
                Id = 42,
                CreateTime = new DateTime(2025, 3, 15),
                Code = 7,
                FirstName = "张",
                LastName = "三",
                Price = 29.9m,
                Quantity = 3
            };

            var dto = GeneratedMapper.Map_SgConverterEntity_To_SgConverterDto(entity)!;

            Assert.Equal(42, dto.Id);
            Assert.Equal("2025-03-15", dto.CreateTime);
            Assert.Equal("000007", dto.Code);
            Assert.Equal("张 三", dto.FullName);
            Assert.Equal(89.7m, dto.TotalPrice);
        }

        /// <summary>
        /// 源生成器路径 — ConvertUsing null 源返回 null
        /// </summary>
        [Fact]
        public void SgConverter_Map_Null_Returns_Null()
        {
            var dto = GeneratedMapper.Map_SgConverterEntity_To_SgConverterDto(null!);
            Assert.Null(dto);
        }

        /// <summary>
        /// 源生成器路径 — ConvertUsing MapList 全部元素正确转换
        /// </summary>
        [Fact]
        public void SgConverter_MapList_All_Elements_Converted()
        {
            var list = new List<SgConverterEntity>
            {
                new() { Id = 1, CreateTime = new DateTime(2025, 1, 1), Code = 10, FirstName = "A", LastName = "B", Price = 10m, Quantity = 2 },
                new() { Id = 2, CreateTime = new DateTime(2025, 6, 30), Code = 999, FirstName = "C", LastName = "D", Price = 5.5m, Quantity = 4 },
                new() { Id = 3, CreateTime = new DateTime(2026, 12, 25), Code = 0, FirstName = "E", LastName = "F", Price = 100m, Quantity = 1 }
            };

            var result = GeneratedMapper.MapList_SgConverterEntity_To_SgConverterDto(list)!;

            Assert.Equal(3, result.Count);

            Assert.Equal(1, result[0].Id);
            Assert.Equal("2025-01-01", result[0].CreateTime);
            Assert.Equal("000010", result[0].Code);
            Assert.Equal("A B", result[0].FullName);
            Assert.Equal(20m, result[0].TotalPrice);

            Assert.Equal(2, result[1].Id);
            Assert.Equal("2025-06-30", result[1].CreateTime);
            Assert.Equal("000999", result[1].Code);
            Assert.Equal("C D", result[1].FullName);
            Assert.Equal(22m, result[1].TotalPrice);

            Assert.Equal(3, result[2].Id);
            Assert.Equal("2026-12-25", result[2].CreateTime);
            Assert.Equal("000000", result[2].Code);
            Assert.Equal("E F", result[2].FullName);
            Assert.Equal(100m, result[2].TotalPrice);
        }

        /// <summary>
        /// 源生成器路径 — ConvertUsing MapList null 返回 null
        /// </summary>
        [Fact]
        public void SgConverter_MapList_Null_Returns_Null()
        {
            var result = GeneratedMapper.MapList_SgConverterEntity_To_SgConverterDto(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// 源生成器 vs 运行时 — ConvertUsing 映射结果一致性验证
        /// </summary>
        [Fact]
        public void SgConverter_Map_Matches_Runtime_Mapper()
        {
            var entity = new SgConverterEntity
            {
                Id = 99,
                CreateTime = new DateTime(2026, 7, 16),
                Code = 12345,
                FirstName = "测",
                LastName = "试",
                Price = 77.77m,
                Quantity = 5
            };

            var sgDto = GeneratedMapper.Map_SgConverterEntity_To_SgConverterDto(entity)!;
            var rtDto = Mapper.Map<SgConverterEntity, SgConverterDto>(entity)!;

            Assert.Equal(rtDto.Id, sgDto.Id);
            Assert.Equal(rtDto.CreateTime, sgDto.CreateTime);
            Assert.Equal(rtDto.Code, sgDto.Code);
            Assert.Equal(rtDto.FullName, sgDto.FullName);
            Assert.Equal(rtDto.TotalPrice, sgDto.TotalPrice);
        }

        #endregion
    }
}
