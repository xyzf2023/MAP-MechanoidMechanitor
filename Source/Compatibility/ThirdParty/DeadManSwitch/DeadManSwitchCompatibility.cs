using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.DeadManSwitch
{
    /// <summary>
    /// 为第三方 MOD《The Dead Man's Switch / 失能机关》（真实 packageId：Aoba.DeadManSwitch.Core）
    /// 提供机械族机械师兼容。仅在确认失能机关本体与前置库 Fortified（AOBA.Framework）均已加载、
    /// 且全部精确目标均成功解析后才动态安装补丁：
    /// 1) 扩展 Fortified.CompTargetable_AddHediffOnTarget.DoEffect 的使用者身份门控，
    ///    仅对“失能机关本体提供的插件”放行合法且可操作的机械族机械师；
    /// 2) 在 Fortified.JobDriver_ApplyModification.MakeNewToils 状态机 MoveNext 中，
    ///    将“机械族机械师给自己安装失能机关插件”这一精确组合改为普通 Wait，
    ///    消除原版 WaitWith 的“otherPawn is the same as toil.actor”警告。
    /// 目标 C# 类型位于 Fortified 程序集，因此一律从 AOBA.Framework 的 ModContentPack 解析。
    /// 绝不使用 PatchAll，绝不引用 Fortified.dll / DMS.dll，绝不为普通玩家机械族放权，
    /// 也绝不自动放行其他使用 Fortified 改造框架的 MOD 物品。
    /// </summary>
    internal sealed class DeadManSwitchCompatibility :
        IThirdPartyCompatibilityModule
    {
        // 仅以真实 packageId 作为加载判断。
        private const string TargetPackageId = "Aoba.DeadManSwitch.Core";

        // 前置库 Fortified Features Framework 的真实 packageId。
        // 失能机关本体依赖它，且本兼容要改写的 C# 类型都定义在 Fortified 程序集中。
        private const string FortifiedPackageId = "AOBA.Framework";

        private const string DoEffectTypeName =
            "Fortified.CompTargetable_AddHediffOnTarget";
        private const string DoEffectMethodName = "DoEffect";

        private const string JobDriverTypeName =
            "Fortified.JobDriver_ApplyModification";
        private const string MakeNewToilsMethodName = "MakeNewToils";

        public string ModuleId => "DeadManSwitch";

        public string DisplayName => "失能机关";

        public string PackageId => TargetPackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            // 第一步：确认失能机关本体已加载。未加载则静默 Inactive，不解析也不安装任何补丁。
            ModContentPack? dmsMod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(TargetPackageId);
            if (dmsMod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId, DisplayName, PackageId);
            }

            // 第二步：失能机关已加载，但前置库 Fortified 必须同时存在。
            // 不得因为失能机关理论上依赖 Fortified 就省略这次独立检查；缺失即安全停用。
            ModContentPack? fortifiedMod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(FortifiedPackageId);
            if (fortifiedMod == null)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "失能机关本体已加载，但前置库 Fortified（AOBA.Framework）未找到，" +
                    "目标依赖结构不完整，兼容已安全跳过。");
            }

            // 必须先完整解析所有关键目标（从 Fortified 程序集解析），确认无误后才安装任何补丁，
            // 避免第一个补丁已安装、第二个目标才发现无法解析的部分状态。
            // 解析成功后会立即把运行期精确目标配置给 Transpiler（运行过程不再模糊反射）。
            if (!TryResolveAllTargets(
                    fortifiedMod,
                    out MethodInfo? doEffectMethod,
                    out MethodInfo? moveNextMethod,
                    out string resolveFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId, resolveFailure);
            }

            MethodInfo? doEffectTranspiler = typeof(DeadManSwitchModificationInstallationPatch)
                .GetMethod(
                    nameof(DeadManSwitchModificationInstallationPatch.TranspilerDoEffect),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            MethodInfo? waitTranspiler = typeof(DeadManSwitchModificationInstallationPatch)
                .GetMethod(
                    nameof(DeadManSwitchModificationInstallationPatch.TranspilerMakeNewToils),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);

            if (doEffectTranspiler == null || waitTranspiler == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的 DeadManSwitch 兼容 Transpiler。",
                    new MissingMethodException(
                        typeof(DeadManSwitchModificationInstallationPatch).FullName,
                        nameof(DeadManSwitchModificationInstallationPatch.TranspilerDoEffect)
                        + " / "
                        + nameof(DeadManSwitchModificationInstallationPatch.TranspilerMakeNewToils)));
            }

            try
            {
                harmony.Patch(
                    doEffectMethod!,
                    transpiler: new HarmonyMethod(doEffectTranspiler));
                harmony.Patch(
                    moveNextMethod!,
                    transpiler: new HarmonyMethod(waitTranspiler));
            }
            catch (Exception ex)
            {
                UnpatchQuietly(harmony, doEffectMethod!, doEffectTranspiler);
                UnpatchQuietly(harmony, moveNextMethod!, waitTranspiler);
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装 DeadManSwitch 兼容补丁时发生异常。",
                    ex,
                    doEffectMethod!,
                    moveNextMethod!);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "CompTargetable_AddHediffOnTarget.DoEffect 身份门槛扩展（仅放行失能机关本体插件）；" +
                "JobDriver_ApplyModification.MakeNewToils 枚举器中的自我安装 Wait 分支。");
        }

        private static void UnpatchQuietly(
            Harmony harmony,
            MethodInfo original,
            MethodInfo patch)
        {
            try
            {
                harmony.Unpatch(original, patch);
            }
            catch
            {
                // 回滚失败不得掩盖最初的补丁安装异常。
            }
        }

        private static bool TryResolveAllTargets(
            ModContentPack fortifiedMod,
            out MethodInfo? doEffectMethod,
            out MethodInfo? moveNextMethod,
            out string failureReason)
        {
            doEffectMethod = null;
            moveNextMethod = null;
            failureReason = string.Empty;

            // 目标一：DoEffect(Pawn)，实例方法，void，严格参数 (Pawn)。
            // 类型位于 Fortified 程序集，因此从 fortifiedMod 解析。
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    fortifiedMod,
                    DoEffectTypeName,
                    DoEffectMethodName,
                    typeof(void),
                    new[] { typeof(Pawn) },
                    out doEffectMethod,
                    out string doEffectFailure)
                || doEffectMethod == null)
            {
                failureReason =
                    $"预期目标：System.Void {DoEffectTypeName}.{DoEffectMethodName}" +
                    $"(Verse.Pawn)。{doEffectFailure}";
                return false;
            }

            // 目标二：MakeNewToils()，实例方法，无参，返回 IEnumerable<Toil>。
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    fortifiedMod,
                    JobDriverTypeName,
                    MakeNewToilsMethodName,
                    typeof(IEnumerable<Toil>),
                    Type.EmptyTypes,
                    out MethodInfo? makeNewToilsMethod,
                    out string makeNewToilsFailure)
                || makeNewToilsMethod == null)
            {
                failureReason =
                    $"预期目标：System.Collections.Generic.IEnumerable<Verse.AI.Toil> " +
                    $"{JobDriverTypeName}.{MakeNewToilsMethodName}()。" +
                    $"{makeNewToilsFailure}";
                return false;
            }

            Type? jobDriverType = makeNewToilsMethod.DeclaringType;
            if (jobDriverType == null)
            {
                failureReason = $"{JobDriverTypeName} 的声明类型无法解析。";
                return false;
            }

            // MakeNewToils 是 yield 迭代器，真正的执行代码位于编译器生成状态机的 MoveNext 中。
            MethodInfo? moveNext = AccessTools.EnumeratorMoveNext(makeNewToilsMethod);
            if (moveNext == null
                || moveNext.ReturnType != typeof(bool)
                || moveNext.IsStatic
                || moveNext.GetParameters().Length != 0
                || moveNext.DeclaringType == null
                || moveNext.DeclaringType == jobDriverType)
            {
                failureReason =
                    $"{JobDriverTypeName}.{MakeNewToilsMethodName}() 的编译器生成状态机 " +
                    $"MoveNext 无法唯一确认（应满足：返回 bool、无参、属于状态机类型）。";
                return false;
            }

            moveNextMethod = moveNext;

            // 精确解析状态机中指向 JobDriver 实例的捕获字段：
            // 必须在 MoveNext.DeclaringType 的 DeclaredOnly 实例字段中，
            // 查找 FieldType 恰好等于该第三方 JobDriver Type 的字段，必须恰好一个。
            FieldInfo? resolvedCapture = null;
            int captureCount = 0;
            FieldInfo[] fields = moveNext.DeclaringType.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType == jobDriverType)
                {
                    captureCount++;
                    resolvedCapture = fields[i];
                }
            }

            if (captureCount != 1 || resolvedCapture == null)
            {
                failureReason =
                    $"在 {JobDriverTypeName} 编译器生成状态机中按字段类型" +
                    $"({JobDriverTypeName}) 解析捕获字段，预期恰好 1 个，实际 {captureCount} 个。";
                return false;
            }

            // 精确解析原版 Toils_General.WaitWith 的完整签名（含全部可选参数的真实 CLR 参数）。
            MethodInfo? waitWith = AccessTools.Method(
                typeof(Toils_General),
                "WaitWith",
                new[]
                {
                    typeof(TargetIndex),
                    typeof(int),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(TargetIndex),
                    typeof(PathEndMode)
                });
            if (waitWith == null
                || waitWith.ReturnType != typeof(Toil)
                || !waitWith.IsStatic)
            {
                failureReason =
                    "原版目标 Toils_General.WaitWith(TargetIndex, int, bool, bool, bool, " +
                    "TargetIndex, PathEndMode) 无法解析或签名不符。";
                return false;
            }

            // 全部确认完毕，一次性把运行期精确目标配置给 Transpiler（运行过程不再模糊反射）。
            DeadManSwitchModificationInstallationPatch.Configure(
                resolvedCapture,
                waitWith);

            return true;
        }
    }
}
