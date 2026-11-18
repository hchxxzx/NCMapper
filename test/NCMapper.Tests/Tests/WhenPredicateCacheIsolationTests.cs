// P2-4 谓词缓存键程序集上下文隔离测试（20260908-001 P2-4；批次一 004 X4 备案移交）
// 修复点：PropertyMatcher.ResolveWhenPredicate 键前缀追加 contextType.Assembly.FullName（631）
// 场景：两个程序集各定义同 FQN 类型 SameFqn.Ctx + 同方法名 When（返回不同 bool）——
// 修复前键为 "FQN::方法名"/"FullName::方法名"，先解析方 MethodInfo 被后至方缓存命中（串扰）；
// 修复后双形态（特性路径 / methodRef）各自解析到本程序集 MethodInfo。
// 动态程序集名带 Guid 后缀：避免跨测试的 s_whenPredicateCache 残留条目（键含 Assembly.FullName）串扰本测试断言。
using System;
using System.Reflection;
using System.Reflection.Emit;
using NCMapper;
using Xunit;

namespace NCMapper.Tests.Tests;

public class WhenPredicateCacheIsolationTests
{
    private static (Assembly AsmA, Assembly AsmB) BuildPairAssemblies()
    {
        // 同 FQN 类型 SameFqn.Ctx：程序集名不同（+Guid 后缀防缓存残留串扰），类型 FullName 相同
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var asmA = BuildPredicateAssembly("WhenPredicateIsolationAsmA_" + suffix, returnsTrue: true);
        var asmB = BuildPredicateAssembly("WhenPredicateIsolationAsmB_" + suffix, returnsTrue: false);
        return (asmA, asmB);
    }

    private static Assembly BuildPredicateAssembly(string assemblyName, bool returnsTrue)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var module = builder.DefineDynamicModule(assemblyName);
        // public static class SameFqn.Ctx { public static bool When(int v) => returnsTrue; }
        var type = module.DefineType("SameFqn.Ctx",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
        var method = type.DefineMethod("When",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), new[] { typeof(int) });
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, returnsTrue ? 1 : 0);
        il.Emit(OpCodes.Ret);
        type.CreateTypeInfo();
        return builder;
    }

    private static (Type CtxA, Type CtxB) ResolveContextTypes((Assembly AsmA, Assembly AsmB) pair)
        => (pair.AsmA.GetType("SameFqn.Ctx")!, pair.AsmB.GetType("SameFqn.Ctx")!);

    [Fact]
    public void AttributePath_SameFqnCrossAssembly_NoCacheCrosstalk()
    {
        var (ctxA, ctxB) = ResolveContextTypes(BuildPairAssemblies());

        // 特性路径形态：methodRef=null，methodName="When"（解析域 = contextType 所在程序集）
        var resolvedA = PropertyMatcher.ResolveWhenPredicate(ctxA, methodRef: null, methodName: "When");
        var resolvedB = PropertyMatcher.ResolveWhenPredicate(ctxB, methodRef: null, methodName: "When");

        Assert.Same(ctxA, resolvedA.DeclaringType);
        Assert.Same(ctxB, resolvedB.DeclaringType);
        Assert.True((bool)resolvedA.Invoke(null, new object[] { 0 })!);
        Assert.False((bool)resolvedB.Invoke(null, new object[] { 0 })!);
    }

    [Fact]
    public void MethodRefPath_SameFqnCrossAssembly_NoCacheCrosstalk()
    {
        var (ctxA, ctxB) = ResolveContextTypes(BuildPairAssemblies());

        // methodRef 形态："FQN::方法名"（host 经 ResolveWhenHost 显示名短路 = contextType 自身）
        var resolvedA = PropertyMatcher.ResolveWhenPredicate(ctxA, methodRef: "SameFqn.Ctx::When", methodName: null);
        var resolvedB = PropertyMatcher.ResolveWhenPredicate(ctxB, methodRef: "SameFqn.Ctx::When", methodName: null);

        Assert.Same(ctxA, resolvedA.DeclaringType);
        Assert.Same(ctxB, resolvedB.DeclaringType);
        Assert.True((bool)resolvedA.Invoke(null, new object[] { 0 })!);
        Assert.False((bool)resolvedB.Invoke(null, new object[] { 0 })!);
    }
}
