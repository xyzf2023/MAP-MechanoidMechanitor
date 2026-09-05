using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion
{
    /// <summary>
    /// 为第三方 MOD《Glitterworld Destroyer 5 Expansion ：MechFusion etc》
    /// （packageId：jixuanming.mechfusion.onepointsix）提供严格兼容。
    /// 仅在确认该扩展已加载后，才通过传入的 Harmony 实例动态安装两个原版补丁；
    /// 绝不使用 PatchAll，绝不 Patch 扩展 MOD 的私有方法，绝不自行计算或移除研究状态。
    /// </summary>
    internal sealed class GlitterworldDestroyer5ExpansionMechFusionCompatibility :
        IThirdPartyCompatibilityModule
    {
        public string ModuleId => "GlitterworldDestroyer5ExpansionMechFusion";

        public string DisplayName =>
            "闪耀世界毁灭者5扩展·MechFusion（jixuanming.mechfusion.onepointsix）";

        public string PackageId => "jixuanming.mechfusion.onepointsix";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId,
                    DisplayName,
                    PackageId);
            }

            if (!TryResolveVanillaTargets(
                    out MethodInfo? finishProject,
                    out MethodInfo? loadedGame,
                    out string targetFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    targetFailure);
            }

            MethodInfo? researchPostfix = typeof(MechFusionMechBondCompatibilityPatch)
                .GetMethod(
                    nameof(MechFusionMechBondCompatibilityPatch.Postfix_ResearchFinish),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            MethodInfo? loadedPostfix = typeof(MechFusionMechBondCompatibilityPatch)
                .GetMethod(
                    nameof(MechFusionMechBondCompatibilityPatch.Postfix_LoadedGame),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);

            if (researchPostfix == null || loadedPostfix == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的 MechFusion 兼容 Postfix 方法。",
                    new MissingMethodException(
                        typeof(MechFusionMechBondCompatibilityPatch).FullName,
                        nameof(MechFusionMechBondCompatibilityPatch.Postfix_ResearchFinish)
                        + " / "
                        + nameof(MechFusionMechBondCompatibilityPatch.Postfix_LoadedGame)));
            }

            try
            {
                harmony.Patch(
                    finishProject,
                    postfix: new HarmonyMethod(researchPostfix));
                harmony.Patch(
                    loadedGame,
                    postfix: new HarmonyMethod(loadedPostfix));
            }
            catch (Exception ex)
            {
                UnpatchQuietly(harmony, finishProject!, researchPostfix);
                UnpatchQuietly(harmony, loadedGame!, loadedPostfix);
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "安装 MechFusion 兼容补丁时发生异常。",
                    ex,
                    finishProject!,
                    loadedGame!);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "ResearchManager.FinishProject(MF_Theory) 与 GameComponentUtility.LoadedGame()"
                + " 两个入口已动态安装，仅在缺失状态时补发 MechBond。");
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

        private static bool TryResolveVanillaTargets(
            out MethodInfo? finishProject,
            out MethodInfo? loadedGame,
            out string failureReason)
        {
            finishProject = null;
            loadedGame = null;
            failureReason = string.Empty;

            Type[] finishProjectParams =
            {
                typeof(ResearchProjectDef),
                typeof(bool),
                typeof(Pawn),
                typeof(bool)
            };

            finishProject = AccessTools.Method(
                typeof(ResearchManager),
                "FinishProject",
                finishProjectParams);
            if (finishProject == null
                || finishProject.ReturnType != typeof(void)
                || finishProject.IsStatic
                || !ParametersMatch(finishProject, finishProjectParams))
            {
                failureReason =
                    "原版目标 ResearchManager.FinishProject"
                    + "(ResearchProjectDef, bool, Pawn, bool) 无法解析或签名不符。";
                return false;
            }

            loadedGame = AccessTools.Method(
                typeof(GameComponentUtility),
                "LoadedGame",
                Type.EmptyTypes);
            if (loadedGame == null
                || loadedGame.ReturnType != typeof(void)
                || !loadedGame.IsStatic
                || !ParametersMatch(loadedGame, Type.EmptyTypes))
            {
                failureReason =
                    "原版目标 GameComponentUtility.LoadedGame()"
                    + " 无法解析或签名不符。";
                return false;
            }

            return true;
        }

        private static bool ParametersMatch(MethodInfo method, Type[] expected)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (parameters[i].ParameterType != expected[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
