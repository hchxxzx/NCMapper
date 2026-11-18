// : ORM 导航收集内核统一 —— 导航树数据结构
// 从 EfCoreNavigationTreeNode/Tree 与 FreeSqlNavigationTreeNode/Tree 提取（两库逐行同构，以 EFCore 版为基准）。
using System.Text;

namespace NCMapper.Internal
{
    /// <summary>
    /// 导航树节点 —— 降级路径自动 Include 的依赖图节点
    /// </summary>
    internal sealed class NavigationTreeNode
    {
        /// <summary>
        /// 导航属性名
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 是否为集合导航（List&lt;T&gt; 形态）
        /// </summary>
        public bool IsCollection { get; }

        /// <summary>
        /// 子导航节点列表
        /// </summary>
        public IReadOnlyList<NavigationTreeNode> Children { get; }

        /// <summary>
        /// 创建导航树节点
        /// </summary>
        /// <param name="name">导航属性名</param>
        /// <param name="isCollection">是否为集合导航</param>
        /// <param name="children">子导航节点（可为 null，缺省为空列表）</param>
        public NavigationTreeNode(string name, bool isCollection, IReadOnlyList<NavigationTreeNode>? children = null)
        {
            Name = name;
            IsCollection = isCollection;
            Children = children ?? Array.Empty<NavigationTreeNode>();
        }
    }

    /// <summary>
    /// 导航树 —— 收集 DTO 跨表依赖，供降级路径自动 Include 使用
    /// </summary>
    internal sealed class NavigationTree
    {
        /// <summary>
        /// 根导航节点列表
        /// </summary>
        public IReadOnlyList<NavigationTreeNode> Roots { get; }

        /// <summary>
        /// 树是否为空（无任何导航依赖）
        /// </summary>
        public bool IsEmpty => Roots.Count == 0;

        /// <summary>
        /// 创建导航树
        /// </summary>
        /// <param name="roots">根导航节点列表</param>
        public NavigationTree(IReadOnlyList<NavigationTreeNode> roots)
        {
            Roots = roots;
        }

        /// <summary>
        /// 空导航树单例
        /// </summary>
        public static readonly NavigationTree Empty = new(Array.Empty<NavigationTreeNode>());

        /// <summary>
        /// 展开全部根到叶路径 —— Include 链（"A.B.C" 形式；含中间路径，与提取前行为一致）
        /// </summary>
        /// <returns>全部根到叶路径集合（"A.B.C" 形式，含中间路径）</returns>
        public HashSet<string> FlattenPaths()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in Roots)
                FlattenNode(root, root.Name, paths);
            return paths;
        }

        private static void FlattenNode(NavigationTreeNode node, string prefix, HashSet<string> paths)
        {
            paths.Add(prefix);
            foreach (var child in node.Children)
                FlattenNode(child, prefix + "." + child.Name, paths);
        }

        /// <summary>
        /// 拼接导航路径摘要 —— Trace 输出用
        /// </summary>
        /// <returns>返回字符串。</returns>
        public string Summary()
        {
            if (IsEmpty) return "[]";
            var sb = new StringBuilder("[");
            var first = true;
            foreach (var root in Roots)
            {
                AppendSummary(sb, root, root.Name, ref first);
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void AppendSummary(StringBuilder sb, NavigationTreeNode node, string prefix, ref bool first)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(prefix);
            if (node.IsCollection) sb.Append("(集合)");
            foreach (var child in node.Children)
                AppendSummary(sb, child, prefix + "." + child.Name, ref first);
        }
    }
}
