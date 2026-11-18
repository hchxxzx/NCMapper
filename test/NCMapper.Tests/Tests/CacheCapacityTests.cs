using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 容量策略测试
    /// <para>方案 b（动态类型不缓存走直算，裁决 2026-08-09）实证载体：
    /// Reflection.Emit 动态类型对生成器（BL-3 复现口径，P-002 C6；
    /// 仓库内无既有载体，按新建，B 类变更登记）。
    /// 断言载体：①动态类型访问后 PropertyMatcher 缓存条目不增长（无界键空间治理核心）；
    /// ②动态类型直算映射结果与同构静态类型缓存路径结果守恒（B3 红线）。</para>
    /// </summary>
    [Collection("GlobalConfigState")]
    public class CacheCapacityTests
    {
        /// <summary>
        /// 每个测试用例执行前清空全局缓存（既有 GlobalConfigState 集合惯例）
        /// </summary>
        public CacheCapacityTests()
        {
            Mapper.ClearCache();
        }

        /// <summary>
        /// 用 Reflection.Emit 生成一个动态类型对（源 + 目标，各含 Id:int 与 Name:string 两个属性）
        /// <para>AssemblyBuilder.Run 模式 → Assembly.IsDynamic=true（BL-3 同口径：Emit 动态类型）。</para>
        /// </summary>
        /// <param name="name">类型对唯一标识</param>
        /// <returns>（源类型, 目标类型）</returns>
        private static (Type Source, Type Target) CreateDynamicPair(string name)
        {
            var assemblyName = new AssemblyName($"DynamicPair_{name}_{Guid.NewGuid():N}");
            var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("Main");

            var src = module.DefineType($"DynSrc_{name}", TypeAttributes.Public);
            DefineAutoProperty(src, "Id", typeof(int));
            DefineAutoProperty(src, "Name", typeof(string));
            var srcType = src.CreateType();

            var tgt = module.DefineType($"DynTgt_{name}", TypeAttributes.Public);
            DefineAutoProperty(tgt, "Id", typeof(int));
            DefineAutoProperty(tgt, "Name", typeof(string));
            var tgtType = tgt.CreateType();

            return (srcType, tgtType);
        }

        /// <summary>
        /// 在 TypeBuilder 上定义自动属性（私有后备字段 + get/set 方法 + Property 声明）
        /// </summary>
        /// <param name="typeBuilder">类型构建器</param>
        /// <param name="name">属性名</param>
        /// <param name="propertyType">属性类型</param>
        private static void DefineAutoProperty(TypeBuilder typeBuilder, string name, Type propertyType)
        {
            var field = typeBuilder.DefineField($"<{name}>k__BackingField", propertyType, FieldAttributes.Private);

            var getter = typeBuilder.DefineMethod($"get_{name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                propertyType, Type.EmptyTypes);
            var getIl = getter.GetILGenerator();
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, field);
            getIl.Emit(OpCodes.Ret);

            var setter = typeBuilder.DefineMethod($"set_{name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                null, new[] { propertyType });
            var setIl = setter.GetILGenerator();
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldarg_1);
            setIl.Emit(OpCodes.Stfld, field);
            setIl.Emit(OpCodes.Ret);

            var property = typeBuilder.DefineProperty(name, PropertyAttributes.None, propertyType, Type.EmptyTypes);
            property.SetGetMethod(getter);
            property.SetSetMethod(setter);
        }

        /// <summary>
        /// 通过反射调用 Mapper.Map&lt;TSource,TTarget&gt;（动态类型无法静态泛型调用）
        /// </summary>
        /// <param name="sourceType">源类型</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="source">源实例</param>
        /// <returns>映射结果实例</returns>
        private static object? MapViaReflection(Type sourceType, Type targetType, object source)
        {
            var mapMethod = typeof(Mapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Mapper.Map)
                    && m.IsGenericMethodDefinition
                    && m.GetGenericArguments().Length == 2
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[0].ParameterType == m.GetGenericArguments()[0]);
            var generic = mapMethod.MakeGenericMethod(sourceType, targetType);
            return generic.Invoke(null, new[] { source, null });
        }

        /// <summary>
        /// 容量实证（BL-3 对照口径）：N 个动态类型对访问后 PropertyMatcher 缓存条目不增长；
        /// 静态类型对照访问后条目 +1（证明检测有效、静态路径仍缓存）
        /// </summary>
        [Fact]
        public void DynamicTypes_DoNotGrow_PropertyMatcherCaches()
        {
            // 生成并访问 60 个动态类型对（源 + 目标 = 120 个动态类型）
            const int pairCount = 60;
            var before = PropertyMatcher.CacheEntryCount;

            for (var i = 0; i < pairCount; i++)
            {
                var pair = CreateDynamicPair($"Pair{i}");
                var props = PropertyMatcher.GetPropertiesDictIgnoreCase(pair.Source)!;
                Assert.True(props.ContainsKey("Id"), "动态源类型属性字典应包含 Id");
                Assert.True(props.ContainsKey("Name"), "动态源类型属性字典应包含 Name");
                _ = PropertyMatcher.GetPropertiesDictIgnoreCase(pair.Target);
                _ = PropertyMatcher.GetMapFromAttribute(pair.Source);
            }

            var after = PropertyMatcher.CacheEntryCount;
            Assert.Equal(before.Properties, after.Properties);
            Assert.Equal(before.MapFromAttr, after.MapFromAttr);

            // 静态类型对照：1 个静态类型访问后 Properties 条目 +1（证明动态检测有效、静态路径仍缓存）
            var staticType = typeof(StSrc);
            _ = PropertyMatcher.GetPropertiesDictIgnoreCase(staticType);
            var afterStatic = PropertyMatcher.CacheEntryCount;
            Assert.Equal(before.Properties + 1, afterStatic.Properties);
        }

        /// <summary>
        /// BL-3 规模实证：5000 个动态类型对（BL-3 口径全量复现）访问后缓存条目不增长，
        /// 记录条目数供与报告 BL-3 原数据（各 12400 条目/52.94MB）并列对照
        /// </summary>
        [Fact]
        public void W3E6_BL3Scale_DynamicTypes_NoCacheGrowth()
        {
            const int pairCount = 5000;
            Mapper.ClearCache();
            var before = PropertyMatcher.CacheEntryCount;

            for (var i = 0; i < pairCount; i++)
            {
                var pair = CreateDynamicPair($"BL3_{i}");
                _ = PropertyMatcher.GetPropertiesDictIgnoreCase(pair.Source);
                _ = PropertyMatcher.GetPropertiesDictIgnoreCase(pair.Target);
                _ = PropertyMatcher.GetMapFromAttribute(pair.Source);
            }

            var after = PropertyMatcher.CacheEntryCount;
            // 动态类型不缓存：5000 对（10000 个动态类型）访问后缓存条目增量 = 0（BL-3 口径下原增长 12400 条/52.94MB）
            Assert.Equal(before.Properties, after.Properties);
            Assert.Equal(before.MapFromAttr, after.MapFromAttr);
        }

        /// <summary>
        /// 行为守恒（B3 红线）：动态类型直算映射结果与同构静态类型（缓存路径）映射结果逐字段一致
        /// </summary>
        [Fact]
        public void DynamicTypes_MapResult_ConsistentWith_StaticCachePath()
        {
            // 动态类型对（直算路径）
            var pair = CreateDynamicPair("Consistency");
            var dynSource = Activator.CreateInstance(pair.Source)!;
            pair.Source.GetProperty("Id")!.SetValue(dynSource, 42);
            pair.Source.GetProperty("Name")!.SetValue(dynSource, "Dynamic");

            var dynResult = MapViaReflection(pair.Source, pair.Target, dynSource);
            Assert.NotNull(dynResult);
            Assert.Equal(42, pair.Target.GetProperty("Id")!.GetValue(dynResult));
            Assert.Equal("Dynamic", pair.Target.GetProperty("Name")!.GetValue(dynResult));

            // 静态同构类型对（缓存路径）
            var staticResult = Mapper.Map<StSrc, StTgt>(new StSrc { Id = 42, Name = "Dynamic" })!;
            Assert.Equal(42, staticResult.Id);
            Assert.Equal("Dynamic", staticResult.Name);

            // 双路径结果语义一致（Id/Name 均正确映射）
            Assert.Equal(staticResult.Id, (int)pair.Target.GetProperty("Id")!.GetValue(dynResult)!);
            Assert.Equal(staticResult.Name, (string)pair.Target.GetProperty("Name")!.GetValue(dynResult)!);
        }

        /// <summary>
        /// 静态同构源类型（缓存路径对照载体）
        /// </summary>
        private class StSrc
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string? Name { get; set; }
        }

        /// <summary>
        /// 静态同构目标类型（缓存路径对照载体）
        /// </summary>
        private class StTgt
        {
            /// <summary>
            /// 主键
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// 名称
            /// </summary>
            public string? Name { get; set; }
        }
    }
}
