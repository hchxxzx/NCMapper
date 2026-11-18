// : DeepCopy 图分类边规则 —— 单一事实源
// 运行时 DeepCopyTypeClassifier（Type 反射版）与生成侧 ClassifyDeepCopyGraph（ITypeSymbol 版）
// 共用本文件（NCMapper.SourceGen.csproj 经 <Compile Include Link> 双端 include，遵循 NameCandidateResolver 先例）。
// 类型系统适配由两端各自完成；规则清单与组合判定全部收敛于此，单点维护。
using System;
using System.Linq;

namespace NCMapper.Shared
{
    /// <summary>
    /// DeepCopy 图分类边规则 —— 容器边/字典 K+V 双边/叶子类型/集合判定/virtual 属性归 Unknown
    /// <para>规则收敛：两端分类结果必须一致（分类一致性测试锁定）</para>
    /// </summary>
    internal static class DeepCopyGraphRules
    {
        /// <summary>
        /// 叶子类型清单（基类匹配：命中即叶子）—— 含精确类型与基类类型
        /// <para>String/值类型/Delegate 由 IsLeafType 组合判定（不在此清单）</para>
        /// </summary>
        public static readonly (string Ns, string Name)[] LeafBaseTypes =
        {
            ("System", "Type"),
            ("System.Reflection", "MemberInfo"),
            ("System.IO", "Stream"),
            ("System", "DBNull"),
        };

        /// <summary>
        /// 字典容器泛型定义清单（K+V 双边遍历）—— 以 (命名空间, 元数据名) 匹配
        /// <para>元数据名含 arity 后缀：Dictionary&lt;,&gt; → "Dictionary`2"</para>
        /// </summary>
        public static readonly (string Ns, string Name)[] DictionaryDefinitions =
        {
            ("System.Collections.Generic", "Dictionary`2"),
            ("System.Collections.Generic", "IDictionary`2"),
            ("System.Collections.Generic", "IReadOnlyDictionary`2"),
        };

        /// <summary>
        /// 叶子类型组合判定 —— 值类型/string/Delegate 或命中叶子基类清单
        /// </summary>
        /// <param name="isValueType">是否为值类型</param>
        /// <param name="isString">是否为 string</param>
        /// <param name="isDelegate">是否为委托类型</param>
        /// <param name="isAnyLeafBaseType">是否命中叶子基类清单</param>
        /// <returns>满足任一叶子条件时为 true</returns>
        public static bool IsLeafType(
            bool isValueType, bool isString, bool isDelegate, bool isAnyLeafBaseType)
            => isValueType || isString || isDelegate || isAnyLeafBaseType;

        /// <summary>
        /// 叶子基类清单命中判定（(命名空间, 类型名) 精确匹配；调用方负责沿继承链遍历）
        /// </summary>
        /// <param name="ns">类型命名空间</param>
        /// <param name="name">类型名称</param>
        /// <returns>命中叶子基类清单时为 true</returns>
        public static bool IsLeafBaseType(string ns, string name)
            => LeafBaseTypes.Any(t => t.Ns == ns && t.Name == name);

        /// <summary>
        /// 字典容器判定 —— (命名空间, 元数据名) 匹配字典泛型定义清单（运行时语义：泛型定义精确匹配）
        /// </summary>
        /// <param name="ns">类型命名空间</param>
        /// <param name="name">类型名称</param>
        /// <returns>命中字典泛型定义清单时为 true</returns>
        public static bool IsDictionaryDefinition(string ns, string name)
            => DictionaryDefinitions.Any(t => t.Ns == ns && t.Name == name);

        /// <summary>
        /// 字典接口名判定（生成侧语义：接口实现判定，原名 TypeHelpers.IsSystemCollectionsNamespace 逻辑收敛）
        /// </summary>
        /// <param name="name">接口类型名称</param>
        /// <returns>名称为 IDictionary 或 IReadOnlyDictionary 时为 true</returns>
        public static bool IsDictionaryInterfaceName(string name)
            => name == "IDictionary" || name == "IReadOnlyDictionary";

        /// <summary>
        /// System.Collections 系列命名空间判定（生成侧字典/集合接口归属）
        /// </summary>
        /// <param name="nsFullName">完整命名空间名（可为 null）</param>
        /// <returns>命中 System.Collections 系列命名空间时为 true</returns>
        public static bool IsSystemCollectionsNamespace(string? nsFullName)
            => nsFullName == "System.Collections.Generic"
                || nsFullName == "System.Collections.ObjectModel"
                || nsFullName == "System.Collections";

        /// <summary>
        /// 集合容器判定 —— 数组或实现 IEnumerable 且非 string
        /// </summary>
        /// <param name="isArray">是否为数组</param>
        /// <param name="implementsIEnumerable">是否实现 IEnumerable</param>
        /// <param name="isString">是否为 string</param>
        /// <returns>满足集合容器条件时为 true</returns>
        public static bool IsCollectionLike(bool isArray, bool implementsIEnumerable, bool isString)
            => isArray || (implementsIEnumerable && !isString);
    }
}
