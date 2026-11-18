using System.Reflection;
namespace NCMapper;
/// <summary>
/// 通用类型转换辅助类，供表达式树Lambda在运行时调用
/// 覆盖 string↔基元类型、值类型互转、枚举互转、可空类型互转等场景
/// </summary>
public static class TypeConvertHelper
{
    /// <summary>
    /// 尝试将源类型的值转换为目标类型
    /// 支持：string↔基元/DateTime/Guid/bool、值类型互转、枚举↔数字/字符串、可空类型
    /// <para>IConvertible 主路径恒按 <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
    /// 解析（"1.5" 任何 CurrentCulture 下均得 1.5m）；依赖本地文化解析（如 de-DE "1,5"）的调用方须自行预转换
    ///（迁移指引见 CHANGELOG L-03）。枚举路径（源枚举→数字）无字符串文化解析，不受影响。</para>
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TTarget">目标类型</typeparam>
    /// <param name="source">源对象值</param>
    /// <returns>转换后的目标值；source 为 null 时返回 default</returns>
    /// <exception cref="MapException">所有转换路径均失败时抛出，包含源/目标类型信息</exception>
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("ConvertValue uses Convert.ChangeType/Enum.Parse/Enum.ToObject which rely on TypeDescriptor and runtime type conversion. Not supported in AOT.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("ConvertValue resolves types and enum members via runtime type conversion. Some members may be trimmed.")]
    public static TTarget? ConvertValue<TSource, TTarget>(TSource source)
    {
        if (source == null) return default;

        // 枚举/形态判定源改运行时类型——编译期泛型声明
        //（typeof(TSource)）对装箱场景（object 装枚举/数值、Enum 基类声明）误判：
        // typeof(object).IsEnum=false → 枚举路径不可达 → 兜底抛 ConversionFailedUnsupported
        //（非泛型字典跨类型转枚举集合的消费方 CollectionCopier.ConvertDictEntry 经
        // ConvertValue<object,T> 中转即受此影响）；运行时类型精确反映实际值形态。
        var sourceType = source.GetType();
        var targetType = typeof(TTarget);
        var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // 如果源值已经是目标类型或其子类，直接返回
        if (source is TTarget directTarget) return directTarget;

        // 异常作控制流收窄——易抛形态预分发到 TryParse
        // 无异常主路。现状 string→Guid / string→数值基元先经 Convert.ChangeType 必抛
        // （InvalidCastException/FormatException/OverflowException）再被 吞掉续走后
        // 续路径——每次转换一次异常抛接（热路径成本 + 脏串语义漂移源）。预分发后 TryParse
        // 成功即返回；TryParse 失败仍落原链（含 ChangeType 兜底与最终 MapException），行为
        // 域不变：NumberStyles 对齐 ChangeType 接受域（整型 Integer/浮点 Float+AllowThousands/
        // decimal Number，均 InvariantCulture），预分发成功 ⇔ ChangeType 会成功，零语义漂移。
        // X2 契约面零变化：数字→枚举仍经 ConvertNumericToCheckedEnum（:117 区段不动）、
        // 空串→枚举仍走 Enum.Parse ArgumentException→MapException、异常消息常量一字不改。
        // 预分发排除枚举目标（含 Nullable<enum>）：GetTypeCode(枚举) 返回底层 Int32 会抢跑
        // 成 int 装箱 → (TTarget) 强转 Nullable<enum> 抛 InvalidCastException——枚举全形态
        // 维持原链（:112 Enum.Parse / checked 枚举）。
        if (source is string preStr && underlyingTarget != typeof(string) && underlyingTarget != typeof(DateTime)
            && !underlyingTarget.IsEnum)
        {
            if (underlyingTarget == typeof(Guid))
            {
                if (Guid.TryParse(preStr, out var preGuid))
                    return (TTarget)(object)preGuid;
            }
            else
            {
                switch (System.Type.GetTypeCode(underlyingTarget))
                {
                    case System.TypeCode.Byte:
                        if (byte.TryParse(preStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var preByte))
                            return (TTarget)(object)preByte;
                        break;
                    case System.TypeCode.SByte:
                        if (sbyte.TryParse(preStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var preSByte))
                            return (TTarget)(object)preSByte;
                        break;
                    case System.TypeCode.Int16:
                        if (short.TryParse(preStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var preShort))
                            return (TTarget)(object)preShort;
                        break;
                    case System.TypeCode.UInt16:
                        if (ushort.TryParse(preStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var preUShort))
                            return (TTarget)(object)preUShort;
                        break;
                    case System.TypeCode.Int32:
                        if (int.TryParse(preStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var preInt))
                            return (TTarget)(object)preInt;
                        break;
                    case System.TypeCode.UInt32:
                        if (uint.TryParse(preStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var preUInt))
                            return (TTarget)(object)preUInt;
                        break;
                    case System.TypeCode.Int64:
                        if (long.TryParse(preStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var preLong))
                            return (TTarget)(object)preLong;
                        break;
                    case System.TypeCode.UInt64:
                        if (ulong.TryParse(preStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var preULong))
                            return (TTarget)(object)preULong;
                        break;
                    case System.TypeCode.Single:
                        if (float.TryParse(preStr, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var preFloat))
                            return (TTarget)(object)preFloat;
                        break;
                    case System.TypeCode.Double:
                        if (double.TryParse(preStr, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var preDouble))
                            return (TTarget)(object)preDouble;
                        break;
                    case System.TypeCode.Decimal:
                        if (decimal.TryParse(preStr, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var preDecimal))
                            return (TTarget)(object)preDecimal;
                        break;
                    case System.TypeCode.Boolean:
                        if (bool.TryParse(preStr, out var preBool))
                            return (TTarget)(object)preBool;
                        break;
                    case System.TypeCode.Char:
                        if (char.TryParse(preStr, out var preChar))
                            return (TTarget)(object)preChar;
                        break;
                }
            }
            // TryParse 失败（脏串/溢出）→ 落原链：ChangeType 异常被吞后走枚举/Guid/兜底路径，
            // 与现状行为域一致（枚举分支 Enum.Parse、Guid 分支 TryParse、兜底 MapException）
        }

        // 尝试通过 IConvertible 通用转换（覆盖 string→基元、值类型互转等）
        System.Exception? overflowInner = null;
        if (source is IConvertible)
        {
            try
            {
                // 补 InvariantCulture —— 双参重载按 CurrentCulture 解析，
                // de-DE 下 "1.5" 被按分组分隔符剥离静默得 15；恒 Invariant 后跨文化一致（L-03 行为变更）
                return (TTarget)System.Convert.ChangeType(source, underlyingTarget, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (InvalidCastException)
            {
                // IConvertible 转换失败，继续尝试其他路径
            }
            catch (FormatException)
            {
                // 字符串格式不匹配，继续尝试其他路径
            }
            catch (OverflowException ex)
            {
                // 数值溢出，继续尝试其他路径
                // 记录溢出原因——兜底 MapException 携带 InnerException（现状
                // 裸吞丢原因链；对照枚举→数字 已保留，本处补齐对称）
                overflowInner = ex;
            }
            catch (ArgumentException)
            {
                // 参数无效（如 Nullable 传递 null），继续尝试其他路径
            }
        }

        // 源是枚举类型
        if (sourceType.IsEnum)
        {
            // 枚举 → 字符串
            if (targetType == typeof(string))
                return (TTarget)(object)source.ToString()!;

            // 枚举 → 数字（底层值转换）
            if (underlyingTarget.IsValueType)
            {
                // 补 InvariantCulture —— 双参重载按 CurrentCulture 解析，
                // de-DE 下 "1.5" 被按分组分隔符剥离静默得 15；恒 Invariant 后跨文化一致（L-03 行为变更）
                // 枚举底层值溢出目标域（如 int.MaxValue 枚举 → byte）
                // 原 OverflowException 裸抛，统一为 MapException（含源/目标类型上下文，异常契约）。
                try
                {
                    return (TTarget)System.Convert.ChangeType(source, underlyingTarget, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    throw new MapException(
                        ErrorMessages.ConversionFailedUnsupported(sourceType.Name, targetType.Name),
                        sourceType, targetType);
                }
                catch (InvalidCastException ex)
                {
                    // 枚举→bool/DateTime/char 等非数值目标
                    // 裸 InvalidCastException 逃逸——统一为 MapException（异常契约同口径，
                    // InnerException 保留原始异常诊断）
                    throw new MapException(
                        ErrorMessages.ConversionFailedUnsupported(sourceType.Name, targetType.Name),
                        sourceType, targetType, ex);
                }
            }
        }

        // 目标是枚举类型
        if (underlyingTarget.IsEnum)
        {
            // B6 修复：enum 路径包 try-catch 返回 default，与 IConvertible 路径异常处理统一
            try
            {
                // 字符串 → 枚举
                // 空串不再静默 return default
                // 落入 Enum.Parse 抛 ArgumentException → 既有 catch 统一转 MapException
                //（:117-130 显式抛口径对齐；null 源保持前置 default 语义不变）
                if (source is string strValue)
                    return (TTarget)Enum.Parse(underlyingTarget, strValue, ignoreCase: true);

                // 数字 → 枚举（排除浮点类型，float/double→enum 需先转整数再转枚举）
                if (sourceType.IsValueType && sourceType != typeof(float) && sourceType != typeof(double))
                    return (TTarget)ConvertNumericToCheckedEnum(underlyingTarget, source, sourceType, targetType);

                // 浮点 → 枚举：先转 long 再转枚举
                if (sourceType == typeof(float) || sourceType == typeof(double))
                    return (TTarget)ConvertNumericToCheckedEnum(underlyingTarget, System.Convert.ToInt64(source), typeof(long), targetType);
            }
            catch (InvalidCastException)
            {
                // 枚举转换失败（非法字符串/不兼容数字类型）→ 抛出异常
                throw new MapException(
                    ErrorMessages.EnumConversionFailedInvalidString(sourceType.Name, targetType.Name),
                    sourceType, targetType);
            }
            catch (ArgumentException)
            {
                // 枚举值不匹配 → 抛出异常
                throw new MapException(
                    ErrorMessages.EnumConversionFailedNoMatch(sourceType.Name, targetType.Name),
                    sourceType, targetType);
            }
        }

        // 值是普通对象，目标是字符串（调用 ToString）
        if (targetType == typeof(string))
            return (TTarget)(object)source.ToString()!;

        // GUID 特殊处理
        if (underlyingTarget == typeof(Guid))
        {
            if (source is string guidStr && Guid.TryParse(guidStr, out var guid))
                return (TTarget)(object)guid;

            if (source is Guid sourceGuid)
                return (TTarget)(object)sourceGuid;
        }

        // 所有转换路径都失败 — 抛出异常，避免数据默默丢失
        // overflowInner 非 null 时携带溢出原因链（现状裸吞丢 InnerException，对照
        // 枚举→数字 已保留，本处补齐对称；消息常量一字不改——X2）
        throw new MapException(
            ErrorMessages.ConversionFailedUnsupported(sourceType.Name, targetType.Name),
            sourceType, targetType, overflowInner!);
    }

    /// <summary>
    /// 数值 → 枚举：Enum.ToObject 不查值域（99 → 未定义值
    /// 静默产出）——补 <see cref="Enum.IsDefined"/> 校验，未定义值抛 MapException
    ///（EnumConversionFailedNoMatch，:117-130 显式抛口径）；<see cref="FlagsAttribute"/>
    /// 枚举组合值白名单豁免（未逐一定义的按位组合合法）。
    /// </summary>
    /// <param name="enumType">目标枚举类型（非 Nullable 包装）</param>
    /// <param name="value">数值源（ChangeType 到枚举底层类型）</param>
    /// <param name="sourceType">源类型名（错误消息用）</param>
    /// <param name="targetType">目标类型名（错误消息用）</param>
    /// <returns>装箱的枚举值</returns>
    private static object ConvertNumericToCheckedEnum(Type enumType, object value, Type sourceType, Type targetType)
    {
        var raw = System.Convert.ChangeType(value, Enum.GetUnderlyingType(enumType));
        if (!enumType.IsDefined(typeof(FlagsAttribute), false) && !Enum.IsDefined(enumType, raw))
            throw new MapException(
                ErrorMessages.EnumConversionFailedNoMatch(sourceType.Name, targetType.Name),
                sourceType, targetType);
        return Enum.ToObject(enumType, raw);
    }
}
