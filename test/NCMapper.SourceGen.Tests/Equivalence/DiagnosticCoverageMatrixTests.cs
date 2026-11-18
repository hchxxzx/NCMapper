using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace NCMapper.SourceGen.Tests.Equivalence;

/// <summary>
/// Check7 —— 诊断码<b>双列</b>覆盖矩阵门禁（全码台账 = 正例锚点 × 邻界反例锚点 × 豁免）。
/// <para><b>契约</b>：全库<b>引用侧</b> 115 个诊断码（数字族 48 + F 系 67）每码一册，
/// 登记「正例触发锚点 / 邻界反例锚点 / 豁免（含理由）」三态归属 <b>0 空缺</b>。
/// 锚点 = <c>file::method</c> <b>弱校验</b>（锚点文件存在 + 该类中该方法体文本含该码字符串），
/// 与 Check6 同哲学——<b>不保证断言强度</b>，只拦「锚点消失 / 改名 / 引用移除 / 漏登」。</para>
///
/// <para><b>双红类</b>：
/// <list type="bullet">
/// <item><c>DIAGCOVER_MISSING_POS</c> —— 该码无有效正例锚点（列空且未豁免，或锚点失效）；</item>
/// <item><c>DIAGCOVER_MISSING_NEG</c> —— 该码无有效邻界反例锚点（列空且未豁免，或锚点失效）。</item>
/// </list>
/// 另设域门禁（<see cref="RunDiagnosticDomainCheck"/>）：<c>UNREGISTERED</c>（引用侧有码而台账漏登）/
/// <c>STALE</c>（台账有码而引用侧已无引用）/ <c>COUNT</c>（码数 ≠ 115 = 48 + 67）/ <c>VACUOUS</c>（扫描面为空）。</para>
///
/// <para><b>检出力边界（拦 / 不拦，明写）</b>——
/// <b>拦</b>：某码正/反例锚点被删除、改名、其所在文件被移除、方法体内对该码的引用被移除；
/// 新增诊断码未登台账；退役码未清台账；台账码数与 115/48/67 口径不符。
/// <b>不拦</b>：锚点对应断言是否<b>真实行使</b>该码（弱存在性校验）；
/// 断言强度与假阳/假阴性（由防假测试与人工金标抽样承担）。</para>
///
/// <para><b>域口径边界</b>：台账域 = <b>引用侧</b>（<c>test/</c> 下出现该码字符串）——含 X6 保留/退役位；
/// 定义侧（描述符声明）与引用侧的差集 = 保留历史位（004/008/F03/F09/F10/F13）。
/// <b>退役空位 F61 不入台账</b>（<c>AnalyzerReleases.Unshipped.md</c> 登记 retired empty ID，无任何引用）。
/// 本类自身文件不计入引用侧扫描（其台账含全量码串，计入将导致域门禁恒绿——门禁自戕）。</para>
///
/// <para><b>缺口补齐（本轮，只新增不改既有断言）</b>：零正例码补正例（028 / 006 / F04）；
/// 零反例码补邻界反例（数字族与 F 系按期盘点收敛清单）；保留/退役位登记豁免。
/// 台账注释块记录「2026-10-13 初盘基线 + 收敛后实测值」。</para>
///
/// <para><b>无需 CI 挂载</b>——本类是 xUnit 用例，随 <c>local-ci-check.ps1</c> 的 <c>test</c> 腿自动执行。</para>
/// </summary>
public class DiagnosticCoverageMatrixTests
{
    // =================================================================
    // 台账数据面：引用侧 115 码（数字族 48 + F 系 67）
    // =================================================================

    /// <summary>
    /// 诊断码双列覆盖台账行：<c>(码, 正例锚点, 邻界反例锚点, 豁免理由)</c>。
    /// <para>锚点 = <c>file::method</c>（弱校验：文件存在 + 方法体含码串）；空串表示该列缺项。</para>
    /// <para>豁免理由非空 = 该行标注豁免，缺列不再判红（保留/退役位与「无稳定正例触发」码如实登记）。</para>
    /// </summary>
    /// <remarks>
    /// <b>基线（2026-10-13 收敛后实测）</b>：115 行 = 48 数字族 + 67 F 系；豁免 7 行
    /// （004 / 008 / 030 / F03 / F09 / F10 / F13）。剩余 108 码正反双列齐备。
    /// 锚点选取 = 混合式（初盘扫描产候选 + 人工金标定案，见探索项 E1/E2）。
    /// </remarks>
    internal static readonly (string Code, string PositiveAnchor, string NegativeAnchor, string Exemption)[] DiagnosticCoverageRows =
    {
        ("NCMAP001", "test/NCMapper.SourceGen.Tests/dotpathgoldentripletests.cs::S2_MissingSegment_NCMAP001_WithSegmentNameAndType", "test/NCMapper.SourceGen.Tests/DiagnosticOptionsTests.cs::Default_NoAttribute_NCMAP001_NotReported", ""),
        ("NCMAP002", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP002_TypeIncompatible_Warning", "test/NCMapper.SourceGen.Tests/TypeCompatConvergenceTests.cs::TGC_GuidToNonString_NoNCMAP002_SkippedWithNCMAP027", ""),
        ("NCMAP003", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP003_NoPropertiesMapped_Warning", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::ValidMapping_NoDiagnostics", ""),
        ("NCMAP004", "", "", "退役：语义并入 NCMAP002（M-05 移除重复发射），无发射路径"),
        ("NCMAP005", "test/NCMapper.SourceGen.Tests/dotpathgoldentripletests.cs::S2_ComposeMissingSegment_NCMAP005_WithSegmentName", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP005_ComposeFieldFound_NoWarning", ""),
        ("NCMAP006", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP006_NonPublicSetter_SameNamedSource_Warning", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP006_NonPublicSetterNoSameNamedSource_NoWarning", ""),
        ("NCMAP007", "test/NCMapper.SourceGen.Tests/AuditDiagnosticAcceptanceTests.cs::M03_Unmapped_NonNavigation_Still_NCMAP007", "test/NCMapper.SourceGen.Tests/Equivalence/EquivalenceTests.NoAutoMatch.cs::A25_NCMAP007_SuppressedOnBothChannels_WithControl", ""),
        ("NCMAP008", "", "", "退役：IValueConverter 泛型校验退役，由 NCMAP035/036/037 承接；无发射路径"),
        ("NCMAP009", "test/NCMapper.SourceGen.Tests/collectionindexgenerationtests.cs::S11i2_SameIndex_StillReportsNCMAP009", "test/NCMapper.SourceGen.Tests/collectionindexgenerationtests.cs::S11i_DistinctIndexes_NoNCMAP009", ""),
        ("NCMAP010", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP010_EnumNoOverlap_Warning", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP010_EnumWithOverlap_NoWarning", ""),
        ("NCMAP011", "test/NCMapper.SourceGen.Tests/AuditDiagnosticAcceptanceTests.cs::H03_InternalSource_PublicTarget_NCMAP011", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP011_PublicType_NotSkipped", ""),
        ("NCMAP012", "test/NCMapper.SourceGen.Tests/AuditDiagnosticAcceptanceTests.cs::M07_SelfRefRecord_NCMAP012_RuntimeFallback", "test/NCMapper.SourceGen.Tests/AuditDiagnosticAcceptanceTests.cs::M07_NonCircularRecord_NoDiagnostic_ConstructorPath", ""),
        ("NCMAP013", "test/NCMapper.SourceGen.Tests/ConvertBackUsingGenerationTests.cs::SetterGate_GetOnly_SkipsWithNCMAP013", "test/NCMapper.SourceGen.Tests/DiagnosticAndCacheTests.cs::NCMAP013_SimpleBidirectional_NoDiagnostic", ""),
        ("NCMAP014", "test/NCMapper.SourceGen.Tests/MaskGenerationTests.cs::InvalidMaskType_EmitsNCMAP014Diagnostic", "test/NCMapper.SourceGen.Tests/MaskGenerationTests.cs::ValidMaskTypeAndStringTarget_NoMaskDiagnostics", ""),
        ("NCMAP015", "test/NCMapper.SourceGen.Tests/MaskGenerationTests.cs::MaskOnNonStringProperty_EmitsNCMAP015Diagnostic", "test/NCMapper.SourceGen.Tests/MaskGenerationTests.cs::ValidMaskTypeAndStringTarget_NoMaskDiagnostics", ""),
        ("NCMAP016", "test/NCMapper.SourceGen.Tests/DeepCopyGraphClassifierTests.cs::NCMAP016_VirtualReferenceProperty_InfoDiagnostic", "test/NCMapper.SourceGen.Tests/DeepCopyGraphClassifierTests.cs::Cyclic_SelfRefClass_NoNcmap016_RegistrationRetained", ""),
        ("NCMAP017", "test/NCMapper.SourceGen.Tests/DiagnosticAndCacheTests.cs::NCMAP017_MapNoAutoInclude_OnReferenceProperty_Info", "test/NCMapper.SourceGen.Tests/DiagnosticAndCacheTests.cs::NCMAP017_MapNoAutoInclude_OnCollection_NoDiagnostic", ""),
        ("NCMAP018", "test/NCMapper.SourceGen.Tests/ArrayContainsInExpressionAnalyzerTests.cs::NCMAP018_ExpressionTree_ArrayContains_Reported_OnNet10", "test/NCMapper.SourceGen.Tests/ArrayContainsInExpressionAnalyzerTests.cs::NCMAP018_Imperative_ArrayContains_NotReported", ""),
        ("NCMAP019", "test/NCMapper.SourceGen.Tests/ConvertBackUsingGenerationTests.cs::NoMapFrom_WithConvertBackUsing_EmitsNCMAP019", "test/NCMapper.SourceGen.Tests/NCMAP019GovernanceMatrixTests.cs::FluentDeclaredDtoNaming_Exempt_042_Not019", ""),
        ("NCMAP020", "test/NCMapper.SourceGen.Tests/DiagnosticEmissionDefenseTests.cs::B3p21_Ncmap020_EmittedOnEmptyMappings", "test/NCMapper.SourceGen.Tests/GlobalConfigReaderTests.cs::EndToEnd_MSBuildProperty_SnapshotEmitted", ""),
        ("NCMAP021", "test/NCMapper.SourceGen.Tests/GlobalConfigMatchingTests.cs::NCMAP021_ExplicitMappingHitGlobalIgnore_Reported", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackTests.cs::GlobalIgnore_HitsAnchor_NCMAP021_And_ReverseEntrySuppressed", ""),
        ("NCMAP022", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_OverrideIgnoreWithMapIgnore_EmitsNCMAP022", "test/NCMapper.SourceGen.Tests/GlobalConfigMatchingTests.cs::Quadrant4_TargetHitWithExemption_Emitted", ""),
        ("NCMAP023", "test/NCMapper.Tests/Tests/SourceGenEmissionShapeTests.cs::T2_CaseCollisionEnum_Emits_DiagnosticNCMAP023", "test/NCMapper.Tests/Tests/SourceGenEmissionShapeTests.cs::T2_CleanEnum_NoCollisionDiagnostics", ""),
        ("NCMAP024", "test/NCMapper.Tests/Tests/SourceGenEmissionShapeTests.cs::T2_ValueCollisionEnum_Emits_DiagnosticNCMAP024", "test/NCMapper.Tests/Tests/SourceGenEmissionShapeTests.cs::T2_CleanEnum_NoCollisionDiagnostics", ""),
        ("NCMAP025", "test/NCMapper.SourceGen.Tests/QuadrantSemanticsGenerationTests.cs::C32_DoubleTrue_Mutual_EmitsNCMAP025_AndNotGenerated", "test/NCMapper.SourceGen.Tests/NCMAP025SyntheticSnapshotTests.cs::SyntheticPairAlone_NoMutual_No025", ""),
        ("NCMAP026", "test/NCMapper.SourceGen.Tests/NCMAP026Tests.cs::MaskOnSourceSide_Triggers", "test/NCMapper.SourceGen.Tests/NCMAP026Tests.cs::MutualPair_DoesNotTriggerNCMAP026", ""),
        ("NCMAP027", "test/NCMapper.SourceGen.Tests/IncompatibleMemberSkipTests.cs::NCMAP027_IncompatibleAutoMatch_EmitsInfoDiagnostic", "test/NCMapper.SourceGen.Tests/TypeCompatConvergenceTests.cs::TGC_GuidToNonString_NoNCMAP002_SkippedWithNCMAP027", ""),
        ("NCMAP028", "test/NCMapper.SourceGen.Tests/IncompatibleMemberSkipTests.cs::NCMAP028_InaccessibleSourceProperty_SkippedWithInfoDiagnostic", "test/NCMapper.SourceGen.Tests/IncompatibleMemberSkipTests.cs::NCMAP027_IncompatibleAutoMatch_EmitsInfoDiagnostic", ""),
        ("NCMAP029", "test/NCMapper.SourceGen.Tests/NestedCycleDegradationTests.cs::MutualCycleDiagnostic_FromIdResolves_NCMAP029_InfoSeverity", "test/NCMapper.SourceGen.Tests/NestedCycleDegradationTests.cs::NoCycle_ZeroNCMAP029_FromIdGuard", ""),
        ("NCMAP030", "", "test/NCMapper.SourceGen.Tests/dotpathgoldentripletests.cs::S6_ComposeMask_NoNCMAP030_NotConsumed", "保留狭窄场景（显式点路径 × [Convert] 无匹配源属性）为可观测诊断；消费路径实现后主路径不再发射，无稳定正例触发"),
        ("NCMAP031", "test/NCMapper.SourceGen.Tests/dotpathdiagnosticstests.cs::NCMAP031_ReverseWriteBack_InterfaceIntermediate_ErrorDiagnostic", "test/NCMapper.SourceGen.Tests/ReverseDirectCallGenTests.cs::ConvertUsingDirectReverse_NCMAP033_TypeConversionPath", ""),
        ("NCMAP032", "test/NCMapper.SourceGen.Tests/ReverseDirectCallGenTests.cs::InitOnlyUpdateSkip_NCMAP032_SingleEmit", "test/NCMapper.SourceGen.Tests/ReverseDirectCallGenTests.cs::WritableProperties_NoInitOnlyOrMask_NoReverseSkipDiagnostics", ""),
        ("NCMAP033", "test/NCMapper.SourceGen.Tests/ConvertBackUsingGenerationTests.cs::WriteBackLeaf_WithForwardConvertUsing_ReportsNCMAP033", "test/NCMapper.SourceGen.Tests/ConvertBackUsingGenerationTests.cs::DirectProperty_ReverseConsumesConverter_ForwardDoesNot", ""),
        ("NCMAP034", "test/NCMapper.SourceGen.Tests/ReverseDirectCallGenTests.cs::MaskReverseWriteBack_NCMAP034", "test/NCMapper.SourceGen.Tests/ReverseDirectCallGenTests.cs::WritableProperties_NoInitOnlyOrMask_NoReverseSkipDiagnostics", ""),
        ("NCMAP035", "test/NCMapper.SourceGen.Tests/ConvertAttributeRenameProbeTests.cs::NCMAP035_Message_Contains_NewDisplayName", "test/NCMapper.SourceGen.Tests/ConvertAttributeRenameProbeTests.cs::ConvertAttribute_Compiles_NoCS1614_NoCS0616", ""),
        ("NCMAP036", "test/NCMapper.SourceGen.Tests/ConverterMethodResolutionTests.cs::R6_OverloadAmbiguity_NCMAP036", "test/NCMapper.SourceGen.Tests/DiagnosticTests.cs::NCMAP037_ConverterSignatureMatch_NoDiagnostic", ""),
        ("NCMAP037", "test/NCMapper.SourceGen.Tests/ConvertBackUsingGenerationTests.cs::Path_DtoSideConvertBack_SignatureMismatch_EmitsNCMAP037", "test/NCMapper.SourceGen.Tests/ConvertBackUsingGenerationTests.cs::Path_LeafSideConvertBack_NotValidated_NoNCMAP037", ""),
        ("NCMAP038", "test/NCMapper.SourceGen.Tests/LifecycleHookAttributeSpikeTests.cs::NCMAP038_MethodNotOnDto_Diagnosed", "test/NCMapper.SourceGen.Tests/LifecycleHookAttributeSpikeTests.cs::DerivedDto_FluentPair_DoesNotInheritBaseHook", ""),
        ("NCMAP039", "test/NCMapper.SourceGen.Tests/LifecycleHookAttributeSpikeTests.cs::NCMAP039_SignatureMismatch_DiagnosedWithCandidates", "test/NCMapper.SourceGen.Tests/LifecycleHookAttributeSpikeTests.cs::DerivedDto_FluentPair_DoesNotInheritBaseHook", ""),
        ("NCMAP040", "test/NCMapper.SourceGen.Tests/LifecycleHookAttributeSpikeTests.cs::NCMAP040_AmbiguousCompatibleOverloads_Diagnosed", "test/NCMapper.SourceGen.Tests/LifecycleHookAttributeSpikeTests.cs::DerivedDto_FluentPair_DoesNotInheritBaseHook", ""),
        ("NCMAP041", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::Ncmap041_MethodNotFound", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::MapWhen_ValidStaticPredicate_NoDiagnostic", ""),
        ("NCMAP042", "test/NCMapper.SourceGen.Tests/NCMAP042SplitProbeTests.cs::DtoNaming_NoAnchor_Reports042_Not019", "test/NCMapper.SourceGen.Tests/NCMAP019GovernanceMatrixTests.cs::FluentDeclaredDtoNaming_Exempt_042_Not019", ""),
        ("NCMAP043", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_NonStringTarget_EmitsNCMAP043", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_ValidForm_StringTargetFormattableSource_NoFormatDiagnostics", ""),
        ("NCMAP044", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_StringSource_EmitsNCMAP044", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_ValidForm_StringTargetFormattableSource_NoFormatDiagnostics", ""),
        ("NCMAP045", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_WithConvert_SameProperty_EmitsNCMAP045", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_ValidForm_StringTargetFormattableSource_NoFormatDiagnostics", ""),
        ("NCMAP046", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_ReverseDerivation_EmitsNCMAP046", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::MapFormat_WithConvertBack_SameProperty_NoReverseDiagnostic", ""),
        ("NCMAP047", "test/NCMapper.SourceGen.Tests/Diagnostics/NoAutoMatchDiagnosticsTests.cs::NCMAP047_TransformAttributeUnderWhitelist_ProducedAndPropertyNotMapped", "test/NCMapper.SourceGen.Tests/Diagnostics/NoAutoMatchDiagnosticsTests.cs::NCMAP047_NotProduced_WhenSamePropertyHasMapProperty", ""),
        ("NCMAP099", "test/NCMapper.SourceGen.Tests/AuditDiagnosticAcceptanceTests.cs::L08M_NCMAP099_Descriptor_Exists", "test/NCMapper.SourceGen.Tests/AuditDiagnosticAcceptanceTests.cs::L08M_BadType_NormalTypes_StillGenerated", ""),
        ("NCMAP_F02", "test/NCMapper.SourceGen.Tests/DiagnosticEmissionDefenseTests.cs::B2p07_F02Message_ContainsReceiverTypeName", "test/NCMapper.SourceGen.Tests/Fluent/DiagnosticFluentTests.cs::FluentConfig_ValidForms_NoDiagnostics", ""),
        ("NCMAP_F03", "", "", "保留位不启用：ReservedIds_F03_F09_F10_NotImplemented 锁定无实现"),
        ("NCMAP_F04", "test/NCMapper.SourceGen.Tests/Fluent/FluentBoundaryRejectionTests.cs::LambdaRootNotParameter_NCMAP_F04_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentBoundaryRejectionTests.cs::LambdaRootIsParameter_NoF04", ""),
        ("NCMAP_F05", "test/NCMapper.SourceGen.Tests/Fluent/FluentBoundaryRejectionTests.cs::ConditionalAccessInLambda_NCMAP_F05_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentInvalidAnchorGuardMatrixTests.cs::NullConditional_MainChain_Mask_F05Single_NoF69", ""),
        ("NCMAP_F06", "test/NCMapper.SourceGen.Tests/LambdaIndexSegmentTests.cs::VariableIndex_StillRejected_F06", "test/NCMapper.SourceGen.Tests/Fluent/FluentInvalidAnchorGuardMatrixTests.cs::IndexerDest_MainChain_SameNameSource_Mask_F06Single_NoF69_NoMaskField", ""),
        ("NCMAP_F07", "test/NCMapper.SourceGen.Tests/Fluent/FluentBoundaryRejectionTests.cs::MethodInvocationInLambda_NCMAP_F07_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentInvalidAnchorGuardMatrixTests.cs::MethodCallHead_MainChain_Format_F07Single_NoF69", ""),
        ("NCMAP_F08", "test/NCMapper.SourceGen.Tests/Fluent/DiagnosticFluentTests.cs::F08_DynamicConfig_IfStatement_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSemanticsDiagnosticsTests.cs::F57_TernaryInLambda_DoesNotTriggerF08", ""),
        ("NCMAP_F09", "", "", "保留位不启用：ReservedIds_F03_F09_F10_NotImplemented 锁定无实现"),
        ("NCMAP_F10", "", "", "保留位不启用：ReservedIds_F03_F09_F10_NotImplemented 锁定无实现"),
        ("NCMAP_F11", "test/NCMapper.SourceGen.Tests/Diagnostics/NoAutoMatchDiagnosticsTests.cs::F11_DualDriverConflict_UnchangedByWhitelistAttribute", "test/NCMapper.SourceGen.Tests/Fluent/DualDriverArbiterTests.cs::SamePair_DualDeclaration_F11_BothSides_Suppressed", ""),
        ("NCMAP_F12", "test/NCMapper.SourceGen.Tests/Fluent/DiagnosticFluentTests.cs::F12_AfterMapping_PrivateTarget_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSemanticsDiagnosticsTests.cs::B205_PublicAndInternalHooks_NoFalsePositive", ""),
        ("NCMAP_F13", "", "", "保留位不启用：F13 已退役（路径式 Convert 并入 Map）"),
        ("NCMAP_F14", "test/NCMapper.SourceGen.Tests/Fluent/DiagnosticFluentTests.cs::F14_AfterMapping_Lambda_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/DiagnosticFluentTests.cs::FluentConfig_ValidForms_NoDiagnostics", ""),
        ("NCMAP_F15", "test/NCMapper.SourceGen.Tests/Fluent/DiagnosticFluentTests.cs::F15_MapperConfig_NoMatchingMethod_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/DiagnosticFluentTests.cs::FluentConfig_ValidForms_NoDiagnostics", ""),
        ("NCMAP_F16", "test/NCMapper.SourceGen.Tests/Fluent/DualDriverArbiterTests.cs::FluentTripleRepeat_SamePair_ThreeF16_MessageListsAll", "test/NCMapper.SourceGen.Tests/Fluent/DualDriverArbiterTests.cs::FluentSelfRepeat_SamePair_NoF11_TwoF16_Suppressed", ""),
        ("NCMAP_F17", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F17_IgnorePlusConvertBack_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F17_IgnoreOnly_NoFalsePositive", ""),
        ("NCMAP_F18", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F18_EntitySideMissing_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F18_EntitySidePresent_NoFalsePositive", ""),
        ("NCMAP_F19", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F19_PrivateConverter_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F19_InternalConverter_NoFalsePositive", ""),
        ("NCMAP_F20", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F21_RefParamConverter_FallsToCompileErrorPath_F20", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F20_LambdaConverter_Transcribed", ""),
        ("NCMAP_F21", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F21_GenericMethodConverter_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F21_ValidSignature_NoFalsePositive", ""),
        ("NCMAP_F22", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F22_GetOnlyAnchor_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F22_PublicSetterAnchor_NoFalsePositive", ""),
        ("NCMAP_F23", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F23_IgnorePlusConvert_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F23_IgnoreOnly_NoFalsePositive", ""),
        ("NCMAP_F24", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F24_SourceUnreachable_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F24_SameNameReachable_NoFalsePositive", ""),
        ("NCMAP_F25", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F25_PrivateConverter_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F25_PublicConverter_NoFalsePositive", ""),
        ("NCMAP_F26", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::MultiOverloadConvert_NoCrash_F26Emitted_NoCode", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F26_LambdaConverter_Transcribed", ""),
        ("NCMAP_F27", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F27_ShapeMismatch_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F27_T1ShapeMatch_NoFalsePositive", ""),
        ("NCMAP_F28", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F28_GetOnlyAnchor_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F28_SettableAnchor_NoFalsePositive", ""),
        ("NCMAP_F29", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::F29_MainChainMap_X_Compose_SameDest_Error", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::DifferentSubSource_SameDest_Allowed", ""),
        ("NCMAP_F30", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::F30_BlockConvertBack_Error", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::Block_LegalMethods_NoFalsePositive", ""),
        ("NCMAP_F31", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::F31_BlockNoAutoInclude_Error", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::Block_LegalMethods_NoFalsePositive", ""),
        ("NCMAP_F32", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::F32_BlockAfterMapping_Error", "test/NCMapper.SourceGen.Tests/Fluent/FluentComposeBlockDiagnosticsTests.cs::Block_LegalMethods_NoFalsePositive", ""),
        ("NCMAP_F35", "test/NCMapper.SourceGen.Tests/LifecycleHooksMatrixTests.cs::F35_FluentSide_PrivateTargetMethod_Diagnosed", "test/NCMapper.SourceGen.Tests/LifecycleHooksMatrixTests.cs::CrossPairPropagation_InapplicablePair_NoEmission_NoDiagnostic", ""),
        ("NCMAP_F36", "test/NCMapper.SourceGen.Tests/LifecycleHooksMatrixTests.cs::F36_FluentSide_LambdaTarget_Diagnosed", "test/NCMapper.SourceGen.Tests/LifecycleHooksMatrixTests.cs::CrossPairPropagation_InapplicablePair_NoEmission_NoDiagnostic", ""),
        ("NCMAP_F37", "test/NCMapper.SourceGen.Tests/LifecycleHookEmissionSpikeTests.cs::F37_InitOnlyInitializerForm_Diagnosed", "test/NCMapper.SourceGen.Tests/LifecycleHookEmissionSpikeTests.cs::PlainTarget_WithHook_NoF37", ""),
        ("NCMAP_F38", "test/NCMapper.SourceGen.Tests/LifecycleHooksMatrixTests.cs::F38_FluentSide_FromSourceBlock_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentBeforeMappingSpikeTests.cs::MainChain_BeforeMapping_NoF38", ""),
        ("NCMAP_F39", "test/NCMapper.SourceGen.Tests/HookDualChannelConflictSpikeTests.cs::F39_BeforeBefore_DualDeclaration_DiagnosedAtBothPositions", "test/NCMapper.SourceGen.Tests/HookDualChannelConflictSpikeTests.cs::F39_CrossCombo_AttrBeforeFluentAfter_NotDiagnosed", ""),
        ("NCMAP_F40", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::F40_InstanceMethodGroup_Rejected", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::MapWhen_ValidStaticPredicate_NoDiagnostic", ""),
        ("NCMAP_F41", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F20_LambdaCapture_Rejected_F41", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertBackDiagnosticsTests.cs::F20_LambdaConverter_Transcribed", ""),
        ("NCMAP_F42", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F26_LambdaStatementBody_Rejected_F42", "test/NCMapper.SourceGen.Tests/Fluent/FluentConvertDiagnosticsTests.cs::F26_LambdaConverter_Transcribed", ""),
        ("NCMAP_F43", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::F43_AnchorNoMapping_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::MapWhen_ValidStaticPredicate_NoDiagnostic", ""),
        ("NCMAP_F44", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::F44_DuplicateWhen_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::MapWhen_ValidStaticPredicate_NoDiagnostic", ""),
        ("NCMAP_F45", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::F45_MainChainComposeOnlyAnchor_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentMapWhenExtractionTests.cs::MapWhen_ValidStaticPredicate_NoDiagnostic", ""),
        ("NCMAP_F46", "test/NCMapper.SourceGen.Tests/MapSubclass/MapSubclassAttributeSideTests.cs::InheritanceViolation_F46_WithPriorAnchor", "test/NCMapper.SourceGen.Tests/NCMAP025SyntheticSnapshotTests.cs::SyntheticPair_MutualWithExplicitMapFrom_Reports025", ""),
        ("NCMAP_F47", "test/NCMapper.SourceGen.Tests/MapSubclass/MapSubclassAttributeSideTests.cs::AttributeSide_F47DuplicateAndSelf", "test/NCMapper.SourceGen.Tests/MapSubclass/MapSubclassDeclarationTests.cs::DistinctSubclassPairs_NoF47", ""),
        ("NCMAP_F48", "test/NCMapper.SourceGen.Tests/MapSubclass/MapSubclassDiagnosticsTests.cs::EmptyTargetSurface_F48_WithPriorAnchor", "test/NCMapper.SourceGen.Tests/MapSubclass/MapSubclassDiagnosticsTests.cs::NonEmptyTargetSurface_NoF48", ""),
        ("NCMAP_F49", "test/NCMapper.SourceGen.Tests/NCMAP042SplitProbeTests.cs::MapSubclass_NoAnchor_ReportsF49_Not019", "test/NCMapper.SourceGen.Tests/NCMAP025SyntheticSnapshotTests.cs::SyntheticPair_MutualWithExplicitMapFrom_Reports025", ""),
        ("NCMAP_F50", "test/NCMapper.SourceGen.Tests/MapSubclass/MapSubclassDiagnosticsTests.cs::RegistrationEmits_F50Advisory", "test/NCMapper.SourceGen.Tests/MapSubclass/MapSubclassDiagnosticsTests.cs::NoRegistration_ZeroF50", ""),
        ("NCMAP_F51", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::Row6_F51_SourceNotFound_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::RowOk_CtorParamSingleValidBinding_NoF51F53", ""),
        ("NCMAP_F52", "test/NCMapper.SourceGen.Tests/IncrementalCtorParamTests.cs::ParamRenameWithStaleBinding_Run2RerunsValidation_F52", "test/NCMapper.SourceGen.Tests/IncrementalCtorParamTests.cs::ParamRenameWithStaleBinding_Run2RerunsValidation_F52", ""),
        ("NCMAP_F53", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::Row7_F53_DuplicateBinding_Diagnosed", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::RowOk_CtorParamSingleValidBinding_NoF51F53", ""),
        ("NCMAP_F54", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::F54_MessageFormatted_TiedCountPresent_NoRawPlaceholder", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::Row10b_BindingResolvesAmbiguity_NoAdvisory", ""),
        ("NCMAP_F55", "test/NCMapper.SourceGen.Tests/Fluent/FluentCtorParamTests.cs::Row3b_Shape1_MultiSegment_RejectedByF55", "test/NCMapper.SourceGen.Tests/Fluent/FluentInvalidAnchorGuardMatrixTests.cs::MultiSegmentDest_WithMask_F55Single_NoF69", ""),
        ("NCMAP_F56", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSemanticsDiagnosticsTests.cs::F56_MapDelegateVariableArg_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSemanticsDiagnosticsTests.cs::F55_MapMultiSegmentDest_Emitted", ""),
        ("NCMAP_F57", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSemanticsDiagnosticsTests.cs::F57_TernaryInSourceSelector_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSemanticsDiagnosticsTests.cs::F57_MapWhenInlineTernaryPredicate_NoFalsePositive", ""),
        ("NCMAP_F58", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSemanticsDiagnosticsTests.cs::F58_MapPositionalVariableArg_Emitted_AndDeclarationStillGenerated", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSemanticsDiagnosticsTests.cs::F58_SemanticConstants_NoFalsePositive", ""),
        ("NCMAP_F59", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSiteDiagnosticsTests.cs::F59_CrossKindOverride_AddPathDeclaration_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSiteDiagnosticsTests.cs::F59_SameKindRedeclare_NoDiagnostic", ""),
        ("NCMAP_F60", "test/NCMapper.SourceGen.Tests/Fluent/FluentBoundaryRejectionTests.cs::UnknownChainMethod_F60Emitted_ChainDropped", "test/NCMapper.SourceGen.Tests/Fluent/FluentSilentSiteDiagnosticsTests.cs::KnownChainTerminal_NoF60", ""),
        ("NCMAP_F62", "test/NCMapper.SourceGen.Tests/SymbolResolutionDefenseTests.cs::F62_DeepSourceLambda_Rejected", "test/NCMapper.SourceGen.Tests/SymbolResolutionDefenseTests.cs::F62_SixteenLevelChain_LegalBoundary", ""),
        ("NCMAP_F63", "test/NCMapper.SourceGen.Tests/SymbolResolutionDefenseTests.cs::F63_DenseStarGraph_BudgetExceeded_StartUndetermined", "test/NCMapper.SourceGen.Tests/SymbolResolutionDefenseTests.cs::F63_SmallGraph_NoBudgetDiagnostic", ""),
        ("NCMAP_F64", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::FluentFormat_NonStringTarget_EmitsF64", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::FluentFormat_ValidForm_NoFormatDiagnostics", ""),
        ("NCMAP_F65", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::FluentFormat_NonFormattableSource_EmitsF65", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::FluentFormat_ValidForm_NoFormatDiagnostics", ""),
        ("NCMAP_F66", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::FluentFormat_WithConvert_SameAnchor_EmitsF66", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::FluentFormat_ValidForm_NoFormatDiagnostics", ""),
        ("NCMAP_F67", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::FluentFormat_ReverseDerivation_EmitsF67", "test/NCMapper.SourceGen.Tests/MapFormatGenerationTests.cs::FluentFormat_WithConvertBack_NoReverseDiagnostic", ""),
        ("NCMAP_F68", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberFoolProofTests.cs::F68_BareMemberNoFollow_WarningEmitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberFoolProofTests.cs::F68_MemberWithDeclarationFollow_NotEmitted", ""),
        ("NCMAP_F69", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberFoolProofTests.cs::F69_OmittedSourceNoSameNameMember_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentInvalidAnchorGuardMatrixTests.cs::NullConditional_MainChain_Mask_F05Single_NoF69", ""),
        ("NCMAP_F70", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberFoolProofTests.cs::F70_CrossSegmentSourceDuplicate_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberFoolProofTests.cs::F70_NoAutoIncludeRepeat_IdempotentNotEmitted", ""),
        ("NCMAP_F71", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberFoolProofTests.cs::F71_MapWhenTypeArgMismatch_Emitted", "test/NCMapper.SourceGen.Tests/Fluent/FluentMemberFoolProofTests.cs::F71_MapWhenLegalForms_NotEmitted", ""),
    };

    // 计数守门基准（非自证派生——护栏自身不得成为下一个「自证恒绿」载体）。
    private const int ExpectedTotalCodes = 115;
    private const int ExpectedNumericCodes = 48;
    private const int ExpectedFluentCodes = 67;

    /// <summary>台账声明标识串——引用侧扫描据此排除「持有全量台账的文件」（抗改名，见 <see cref="EnumerateReferencedCodes"/>）。</summary>
    private const string LedgerSentinel = "DiagnosticCoverageRows";

    // =================================================================
    // 真实核查
    // =================================================================

    /// <summary>双红类核查：全码三态归属 0 空缺（锚点存在 + 方法体含码串）或登记豁免。</summary>
    [Fact]
    public void DiagnosticCoverage_AllRows_ResolvesNoFailure()
    {
        var failures = RunDiagnosticCoverageCheck(DiagnosticCoverageRows);

        Assert.True(failures.Count == 0,
            "Check7 诊断码双列覆盖门禁 FAILED:\n" + string.Join("\n", failures));
    }

    /// <summary>域门禁：台账码集 ↔ 引用侧扫描码集 双射；码数 115 = 48 + 67。</summary>
    [Fact]
    public void DiagnosticCoverage_CodeDomain_MatchesReferenceScan()
    {
        var referenced = EnumerateReferencedCodes(FindRepoRoot());
        var failures = RunDiagnosticDomainCheck(DiagnosticCoverageRows, referenced);

        Assert.True(failures.Count == 0,
            "Check7 诊断码域门禁 FAILED:\n" + string.Join("\n", failures));
    }

    /// <summary>台账豁免约束：豁免行必须给出理由（非空），且豁免行数有界——防「豁免滥用」使缺列静默通过。</summary>
    [Fact]
    public void DiagnosticCoverage_Exemptions_MustCarryReason()
    {
        var bad = DiagnosticCoverageRows
            .Where(r => (string.IsNullOrWhiteSpace(r.PositiveAnchor) || string.IsNullOrWhiteSpace(r.NegativeAnchor))
                        && string.IsNullOrWhiteSpace(r.Exemption))
            .Select(r => r.Code)
            .ToList();

        Assert.True(bad.Count == 0, "缺列但未登记豁免理由的行: " + string.Join(", ", bad));
    }

    // =================================================================
    // 自证（双态：变异 → 必红；复原 → 绿）
    // =================================================================

    /// <summary>自证 ①（双态）：正例锚点方法改名 → DIAGCOVER_MISSING_POS；复原 → 绿。</summary>
    [Fact]
    public void SelfProof1_MissingPositiveAnchor_RedThenRestoredGreen()
    {
        var mutated = DiagnosticCoverageRows
            .Select(r => r.Code == "NCMAP010"
                ? (r.Code, r.PositiveAnchor + "_NoSuchSuffix", r.NegativeAnchor, r.Exemption)
                : r)
            .ToArray();

        var red = RunDiagnosticCoverageCheck(mutated);
        Assert.Contains(red, f => f.Contains("DIAGCOVER_MISSING_POS") && f.Contains("NCMAP010"));

        var green = RunDiagnosticCoverageCheck(DiagnosticCoverageRows);
        Assert.DoesNotContain(green, f => f.Contains("DIAGCOVER_MISSING_POS"));
    }

    /// <summary>自证 ②（双态）：反例锚点方法改名 → DIAGCOVER_MISSING_NEG；复原 → 绿。</summary>
    [Fact]
    public void SelfProof2_MissingNegativeAnchor_RedThenRestoredGreen()
    {
        var mutated = DiagnosticCoverageRows
            .Select(r => r.Code == "NCMAP010"
                ? (r.Code, r.PositiveAnchor, r.NegativeAnchor + "_NoSuchSuffix", r.Exemption)
                : r)
            .ToArray();

        var red = RunDiagnosticCoverageCheck(mutated);
        Assert.Contains(red, f => f.Contains("DIAGCOVER_MISSING_NEG") && f.Contains("NCMAP010"));

        var green = RunDiagnosticCoverageCheck(DiagnosticCoverageRows);
        Assert.DoesNotContain(green, f => f.Contains("DIAGCOVER_MISSING_NEG"));
    }

    /// <summary>自证 ③（双态）：缺列且无豁免 → 红；补豁免理由 → 同缺列不红（豁免语义）。</summary>
    [Fact]
    public void SelfProof3_EmptyColumnWithoutExemption_RedThenExemptGreen()
    {
        var noExempt = DiagnosticCoverageRows
            .Select(r => r.Code == "NCMAP011" ? (r.Code, "", "", "") : r)
            .ToArray();
        var red = RunDiagnosticCoverageCheck(noExempt);
        Assert.Contains(red, f => f.Contains("DIAGCOVER_MISSING_POS") && f.Contains("NCMAP011"));
        Assert.Contains(red, f => f.Contains("DIAGCOVER_MISSING_NEG") && f.Contains("NCMAP011"));

        var exempted = DiagnosticCoverageRows
            .Select(r => r.Code == "NCMAP011" ? (r.Code, "", "", "自证豁免理由") : r)
            .ToArray();
        var green = RunDiagnosticCoverageCheck(exempted);
        Assert.DoesNotContain(green, f => f.Contains("NCMAP011"));
    }

    /// <summary>自证 ④：台账清空 → VACUOUS 红（防台账面失效致恒绿）。</summary>
    [Fact]
    public void SelfProof4_EmptyLedger_DetectedAsVacuous()
    {
        var failures = RunDiagnosticCoverageCheck(Array.Empty<(string, string, string, string)>());

        Assert.Contains(failures, f => f.Contains("VACUOUS"));
    }

    /// <summary>自证 ⑤（双态）：删一行 → 域门禁 UNREGISTERED；复原 → 绿。</summary>
    [Fact]
    public void SelfProof5_RemovedLedgerRow_DomainUnregisteredThenRestoredGreen()
    {
        var referenced = EnumerateReferencedCodes(FindRepoRoot());
        var mutated = DiagnosticCoverageRows.Where(r => r.Code != "NCMAP010").ToArray();

        var red = RunDiagnosticDomainCheck(mutated, referenced);
        Assert.Contains(red, f => f.Contains("UNREGISTERED") && f.Contains("NCMAP010"));

        var green = RunDiagnosticDomainCheck(DiagnosticCoverageRows, referenced);
        Assert.DoesNotContain(green, f => f.Contains("UNREGISTERED"));
    }

    /// <summary>自证 ⑥（双态）：混入幽灵码行 → 域门禁 STALE；复原 → 绿。</summary>
    [Fact]
    public void SelfProof6_GhostLedgerRow_DomainStaleThenRestoredGreen()
    {
        var referenced = EnumerateReferencedCodes(FindRepoRoot());
        var mutated = DiagnosticCoverageRows
            .Append(("NCMAP900", "x::y", "x::z", ""))
            .ToArray();

        var red = RunDiagnosticDomainCheck(mutated, referenced);
        Assert.Contains(red, f => f.Contains("STALE") && f.Contains("NCMAP900"));

        var green = RunDiagnosticDomainCheck(DiagnosticCoverageRows, referenced);
        Assert.DoesNotContain(green, f => f.Contains("STALE"));
    }

    /// <summary>自证 ⑦：引用侧扫描面清空 → 域门禁 VACUOUS 红（防扫描面路径变更致恒绿）。</summary>
    [Fact]
    public void SelfProof7_EmptyReferenceScan_DomainVacuous()
    {
        var failures = RunDiagnosticDomainCheck(DiagnosticCoverageRows, Array.Empty<string>());

        Assert.Contains(failures, f => f.Contains("VACUOUS"));
    }

    /// <summary>自证 ⑧：码数口径违例（删 1 F 系行）→ 域门禁 COUNT 红（115/67 口径守门）。</summary>
    [Fact]
    public void SelfProof8_RemovedFluentRow_CountGateRed()
    {
        var referenced = EnumerateReferencedCodes(FindRepoRoot());
        var mutated = DiagnosticCoverageRows.Where(r => r.Code != "NCMAP_F68").ToArray();

        var red = RunDiagnosticDomainCheck(mutated, referenced);

        Assert.Contains(red, f => f.Contains("COUNT"));
        Assert.Contains(red, f => f.Contains("UNREGISTERED") && f.Contains("NCMAP_F68"));
    }

    // =================================================================
    // 纯核查逻辑（真实核查与自证共用——防假测试纪律）
    // =================================================================

    /// <summary>
    /// 双红类纯核查（<paramref name="rows"/> 为可变副本，自证传入变异体）。
    /// <list type="bullet">
    /// <item><c>DIAGCOVER_MISSING_POS</c>：正例列空且未豁免，或正例锚点失效；</item>
    /// <item><c>DIAGCOVER_MISSING_NEG</c>：反例列空且未豁免，或反例锚点失效；</item>
    /// <item><c>VACUOUS</c>：台账为空。</item>
    /// </list>
    /// </summary>
    internal static List<string> RunDiagnosticCoverageCheck(
        IEnumerable<(string Code, string PositiveAnchor, string NegativeAnchor, string Exemption)> rows,
        string? repoRoot = null)
    {
        var root = repoRoot ?? FindRepoRoot();
        var list = rows.ToList();
        var failures = new List<string>();

        if (list.Count == 0)
        {
            failures.Add("VACUOUS: 台账为空——诊断码双列门禁恒绿，须立即排查");
            return failures;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var rowNo = i + 1;
            var exempt = !string.IsNullOrWhiteSpace(r.Exemption);

            if (string.IsNullOrWhiteSpace(r.PositiveAnchor))
            {
                if (!exempt)
                    failures.Add($"DIAGCOVER_MISSING_POS: 行 {rowNo} 码 {r.Code} 无正例锚点且未登记豁免——须补正例或如实登记豁免理由");
            }
            else if (!AnchorValid(root, r.PositiveAnchor, r.Code, out var posWhy))
            {
                failures.Add($"DIAGCOVER_MISSING_POS: 行 {rowNo} 码 {r.Code} 正例锚点失效（{posWhy}）→ {r.PositiveAnchor}");
            }

            if (string.IsNullOrWhiteSpace(r.NegativeAnchor))
            {
                if (!exempt)
                    failures.Add($"DIAGCOVER_MISSING_NEG: 行 {rowNo} 码 {r.Code} 无反例锚点且未登记豁免——须补邻界反例或如实登记豁免理由");
            }
            else if (!AnchorValid(root, r.NegativeAnchor, r.Code, out var negWhy))
            {
                failures.Add($"DIAGCOVER_MISSING_NEG: 行 {rowNo} 码 {r.Code} 反例锚点失效（{negWhy}）→ {r.NegativeAnchor}");
            }
        }

        return failures;
    }

    /// <summary>域门禁纯核查：台账码集 ↔ 引用侧码集双射 + 码数口径（115 = 48 + 67）+ vacuous 护栏。</summary>
    internal static List<string> RunDiagnosticDomainCheck(
        IEnumerable<(string Code, string PositiveAnchor, string NegativeAnchor, string Exemption)> rows,
        IReadOnlyCollection<string> referencedCodes)
    {
        var failures = new List<string>();
        var ledgerCodes = new HashSet<string>(rows.Select(r => r.Code), StringComparer.Ordinal);

        if (referencedCodes.Count == 0)
            failures.Add("VACUOUS: 引用侧码扫描为 0 条——扫描面路径失效将使本门禁恒绿，须立即排查");
        if (ledgerCodes.Count == 0)
            failures.Add("VACUOUS: 台账码集为空");

        foreach (var miss in referencedCodes.Except(ledgerCodes, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            failures.Add($"UNREGISTERED: 引用侧存在码 {miss} 但台账未登记——新增诊断码须同批登记（否则游离于核验视野）");
        foreach (var extra in ledgerCodes.Except(referencedCodes, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            failures.Add($"STALE: 台账存在码 {extra} 但引用侧已无任何引用——码已退役或引用被移除");

        var numeric = ledgerCodes.Count(c => !c.StartsWith("NCMAP_F", StringComparison.Ordinal));
        var fluent = ledgerCodes.Count - numeric;
        if (ledgerCodes.Count != ExpectedTotalCodes)
            failures.Add($"COUNT: 台账码数 {ledgerCodes.Count} ≠ {ExpectedTotalCodes}");
        if (numeric != ExpectedNumericCodes)
            failures.Add($"COUNT: 数字族码数 {numeric} ≠ {ExpectedNumericCodes}");
        if (fluent != ExpectedFluentCodes)
            failures.Add($"COUNT: F 系码数 {fluent} ≠ {ExpectedFluentCodes}");

        return failures;
    }

    /// <summary>锚点弱校验：<c>file::method</c> 格式 + 文件存在 + 该方法体内含码串。</summary>
    private static bool AnchorValid(string root, string anchor, string code, out string why)
    {
        var parts = anchor.Split(new[] { "::" }, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            why = "锚点格式非法（应为 file::method）";
            return false;
        }

        var path = Path.Combine(root, parts[0].Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            why = "锚点文件不存在";
            return false;
        }
        if (parts[1].Length == 0)
        {
            why = "方法名为空（弱校验要求指向具名方法）";
            return false;
        }
        if (!MethodBodyContainsCode(path, parts[1], code))
        {
            why = $"方法 {parts[1]} 体内未出现码 {code}";
            return false;
        }

        why = "";
        return true;
    }

    /// <summary>在指定文件中查找同名方法并判定其方法体文本是否含码串（Roslyn 语法树界定方法体）。</summary>
    private static bool MethodBodyContainsCode(string path, string methodName, string code)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }

        var tree = CSharpSyntaxTree.ParseText(text);
        foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!string.Equals(method.Identifier.ValueText, methodName, StringComparison.Ordinal))
                continue;
            var body = method.Body?.ToFullString()
                       ?? method.ExpressionBody?.ToFullString()
                       ?? string.Empty;
            if (body.Contains(code, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 引用侧码集扫描：遍历 <c>test/</c> 下全部 <c>*.cs</c>（排除 <c>bin</c>/<c>obj</c>），
    /// 收集所有 <c>NCMAP\d{3}</c> / <c>NCMAP_F\d{2}</c> 形码串。
    /// <b>排除「持有全量台账的文件」</b>（按 <see cref="LedgerSentinel"/> 标识串识别，抗改名）——
    /// 其台账含全量码串，计入将使域门禁恒绿（门禁自戕）。
    /// </summary>
    internal static HashSet<string> EnumerateReferencedCodes(string repoRoot)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var testRoot = Path.Combine(repoRoot, "test");
        if (!Directory.Exists(testRoot))
            return result;

        var rx = new Regex(@"NCMAP(?:_F\d{2}|\d{3})", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            var norm = file.Replace('\\', '/');
            if (norm.Contains("/bin/", StringComparison.Ordinal)
                || norm.Contains("/obj/", StringComparison.Ordinal))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            // 台账持有文件（含全量码串）不入引用侧扫描面——按声明标识串识别，防改名致域门禁恒绿
            if (text.Contains(LedgerSentinel, StringComparison.Ordinal))
                continue;

            foreach (Match m in rx.Matches(text))
                result.Add(m.Value);
        }

        return result;
    }

    /// <summary>仓库根定位（向上找 <c>NCMapper.slnx</c>）。</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NCMapper.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
