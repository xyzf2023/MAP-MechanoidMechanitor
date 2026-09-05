using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5Expansion
{
    /// <summary>
    /// 为第三方 MOD《Glitterworld Destroyer 5 Expansion》（真实 packageId：qiuci.GD5.Expansion）
    /// 提供机械族机械师兼容。仅在确认该扩展已加载、且全部精确目标均成功解析后才动态安装补丁：
    /// 1) 扩展 CompTargetEffect_GiveHediffToPlayMech.DoEffectOn 的使用者身份门控，放行合法且可操作的机械族机械师；
    /// 2) 在 JobDriver_GiveHediffToMech.MakeNewToils 状态机 MoveNext 中，将“自我安装”分支改为普通 Wait，
    ///    消除原版 WaitWith 的“otherPawn is the same as toil.actor”警告。
    /// 绝不使用 PatchAll，绝不引用 MK3expand 类型，绝不为普通玩家机械族放权。
    /// </summary>
    internal sealed class GlitterworldDestroyer5ExpansionCompatibility :
        IThirdPartyCompatibilityModule
    {
        // 仅以真实 packageId 作为加载判断，不得使用 xyzf.GD5.Expansion，
        // 也不得将 MechFusion 的 jixuanming.mechfusion.onepointsix 误认为本次目标。
        private const string TargetPackageId = "qiuci.GD5.Expansion";

        private const string DoEffectTypeName =
            "MK3expand.CompTargetEffect_GiveHediffToPlayMech";
        private const string DoEffectMethodName = "DoEffectOn";

        private const string JobDriverTypeName =
            "MK3expand.JobDriver_GiveHediffToMech";
        private const string MakeNewToilsMethodName = "MakeNewToils";

        public string ModuleId => "GlitterworldDestroyer5Expansion";

        public string DisplayName => "闪耀世界毁灭者5扩展";

        public string PackageId => TargetPackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId, DisplayName, PackageId);
            }

            // 必须先完整解析所有关键目标，确认无误后才安装任何补丁，
            // 避免第一个补丁已安装、第二个目标才发现无法解析的部分状态。
            // 解析成功后会立即把运行期精确目标配置给 Transpiler（运行过程不再模糊反射）。
            if (!TryResolveAllTargets(
                    mod,
                    out MethodInfo? doEffectMethod,
                    out MethodInfo? moveNextMethod,
                    out string resolveFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId, resolveFailure);
            }

            MethodInfo? doEffectTranspiler = typeof(GlitterworldDestroyer5ExpansionInstallationPatch)
                .GetMethod(
                    nameof(GlitterworldDestroyer5ExpansionInstallationPatch.TranspilerDoEffectOn),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            MethodInfo? waitTranspiler = typeof(GlitterworldDestroyer5ExpansionInstallationPatch)
                .GetMethod(
                    nameof(GlitterworldDestroyer5ExpansionInstallationPatch.TranspilerMakeNewToils),
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
                    "无法解析本项目的 GlitterworldDestroyer5Expansion 兼容 Transpiler。",
                    new MissingMethodException(
                        typeof(GlitterworldDestroyer5ExpansionInstallationPatch).FullName,
                        nameof(GlitterworldDestroyer5ExpansionInstallationPatch.TranspilerDoEffectOn)
                        + " / "
                        + nameof(GlitterworldDestroyer5ExpansionInstallationPatch.TranspilerMakeNewToils)));
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
                    "安装 GlitterworldDestroyer5Expansion 兼容补丁时发生异常。",
                    ex,
                    doEffectMethod!,
                    moveNextMethod!);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "CompTargetEffect_GiveHediffToPlayMech.DoEffectOn 身份门控扩展；"
                + "JobDriver_GiveHediffToMech.MakeNewToils 枚举器中的自我安装 Wait 分支。");
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
            ModContentPack mod,
            out MethodInfo? doEffectMethod,
            out MethodInfo? moveNextMethod,
            out string failureReason)
        {
            doEffectMethod = null;
            moveNextMethod = null;
            failureReason = string.Empty;

            // 目标一：DoEffectOn(Pawn, Thing)，实例方法，void，严格参数 (Pawn, Thing)。
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
                    DoEffectTypeName,
                    DoEffectMethodName,
                    typeof(void),
                    new[] { typeof(Pawn), typeof(Thing) },
                    out doEffectMethod,
                    out string doEffectFailure)
                || doEffectMethod == null)
            {
                failureReason =
                    $"预期目标：System.Void {DoEffectTypeName}.{DoEffectMethodName}" +
                    $"(Verse.Pawn, Verse.Thing)。{doEffectFailure}";
                return false;
            }

            // 目标二：MakeNewToils()，实例方法，无参，返回 IEnumerable<Toil>。
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod,
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
            GlitterworldDestroyer5ExpansionInstallationPatch.Configure(
                resolvedCapture,
                waitWith);

            return true;
        }
    }
}
