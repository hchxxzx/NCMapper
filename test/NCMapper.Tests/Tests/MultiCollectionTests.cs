using NCMapper.Tests.Models;
using Xunit;

namespace NCMapper.Tests.Tests
{
    /// <summary>
    /// 多样化集合类型映射测试 — 验证 ConvertListExpression 对 HashSet/Queue/Stack/Array 等容器的支持
    /// </summary>
    public class MultiCollectionTests
    {
        /// <summary>
        /// List&lt;string&gt; → List&lt;string&gt;：走 CopyList（ToList 浅拷贝，值类型无深拷贝需求）
        /// </summary>
        [Fact]
        public void List_Of_String_Mapped()
        {
            var entity = new MultiCollectionEntity
            {
                NameList = new List<string> { "Alpha", "Beta", "Gamma" }
            };

            var dto = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(entity)!;

            Assert.NotNull(dto.NameList);
            Assert.Equal(3, dto.NameList!.Count);
            Assert.Equal("Alpha", dto.NameList[0]);
            Assert.Equal("Beta", dto.NameList[1]);
            Assert.Equal("Gamma", dto.NameList[2]);
        }

        /// <summary>
        /// HashSet&lt;string&gt; → HashSet&lt;string&gt;：走 CopyHashSet（构造器拷贝），元素不重复且无序
        /// </summary>
        [Fact]
        public void HashSet_Of_String_Mapped()
        {
            var entity = new MultiCollectionEntity
            {
                TagSet = new HashSet<string> { "csharp", "dotnet", "mapper" }
            };

            var dto = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(entity)!;

            Assert.NotNull(dto.TagSet);
            Assert.Equal(3, dto.TagSet!.Count);
            Assert.Contains("csharp", dto.TagSet);
            Assert.Contains("dotnet", dto.TagSet);
            Assert.Contains("mapper", dto.TagSet);
            // 验证是独立副本，非同一引用
            Assert.NotSame(entity.TagSet, dto.TagSet);
        }

        /// <summary>
        /// Queue&lt;int&gt; → Queue&lt;int&gt;：走 CopyQueue（构造器拷贝），保持 FIFO 顺序
        /// </summary>
        [Fact]
        public void Queue_Of_Int_Mapped()
        {
            var entity = new MultiCollectionEntity
            {
                TaskQueue = new Queue<int>(new[] { 10, 20, 30 })
            };

            var dto = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(entity)!;

            Assert.NotNull(dto.TaskQueue);
            // 验证元素和出队顺序
            Assert.Equal(10, dto.TaskQueue!.Dequeue());
            Assert.Equal(20, dto.TaskQueue.Dequeue());
            Assert.Equal(30, dto.TaskQueue.Dequeue());
        }

        /// <summary>
        /// Stack&lt;int&gt; → Stack&lt;int&gt;：走 CopyStack（构造器+Reverse），保持 LIFO 顺序一致
        /// </summary>
        [Fact]
        public void Stack_Of_Int_Mapped()
        {
            var entity = new MultiCollectionEntity
            {
                HistoryStack = new Stack<int>(new[] { 999, 888, 777 })
            };

            var dto = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(entity)!;

            Assert.NotNull(dto.HistoryStack);
            // P6 修复后：CopyStack 用构造器 + Reverse 保持 LIFO 顺序一致
            // 原栈 push 999,888,777 → 栈顶 777 → Pop 顺序 777, 888, 999
            // 修复前（JSON 方案）：顺序反转 → Pop 999, 888, 777
            Assert.Equal(777, dto.HistoryStack!.Pop());
            Assert.Equal(888, dto.HistoryStack.Pop());
            Assert.Equal(999, dto.HistoryStack.Pop());
        }

        /// <summary>
        /// int[] → int[]：走 CopyArray（Array.Clone 浅拷贝）
        /// </summary>
        [Fact]
        public void Array_Of_Int_Mapped()
        {
            var entity = new MultiCollectionEntity
            {
                Scores = new[] { 85, 92, 78 }
            };

            var dto = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(entity)!;

            Assert.NotNull(dto.Scores);
            Assert.Equal(3, dto.Scores!.Length);
            Assert.Equal(85, dto.Scores[0]);
            Assert.Equal(92, dto.Scores[1]);
            Assert.Equal(78, dto.Scores[2]);
            // 验证是独立副本
            Assert.NotSame(entity.Scores, dto.Scores);
        }

        /// <summary>
        /// 全部集合属性为 null → DTO 对应属性均为 null
        /// </summary>
        [Fact]
        public void All_Null_Collections_Return_Null()
        {
            var entity = new MultiCollectionEntity
            {
                NameList = null,
                TagSet = null,
                TaskQueue = null,
                HistoryStack = null,
                Scores = null
            };

            var dto = Mapper.Map<MultiCollectionEntity, MultiCollectionDto>(entity)!;

            Assert.Null(dto.NameList);
            Assert.Null(dto.TagSet);
            Assert.Null(dto.TaskQueue);
            Assert.Null(dto.HistoryStack);
            Assert.Null(dto.Scores);
        }
    }
}
