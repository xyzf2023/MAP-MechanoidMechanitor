using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion
{
    /// <summary>
    /// 闪毁5融合科研能力补发模块：
    /// 在四项后续科研完成时即时为既有机械族机械师补发 Ability，
    /// 并在完整读档后按科研状态兜底补发。新生成机械族机械师不即时处理。
    /// </summary>
    internal sealed class MechFusionAbilityGrantCompatibility :
        IThirdPartyCompatibilityModule
    {
        public string ModuleId => "GlitterworldDestroyer5ExpansionMechFusion.AbilityGrant";

        public string DisplayName => "闪耀世界毁灭者5扩展·MechFusion 科研能力补发";

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

            if (!TryValidateUnlockDefs(out string defFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    defFailure);
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

            MethodInfo? researchPostfix =
                typeof(MechFusionAbilityGrantCompatibilityPatch).GetMethod(
                    nameof(MechFusionAbilityGrantCompatibilityPatch.Postfix_ResearchFinish),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            MethodInfo? loadedPostfix =
                typeof(MechFusionAbilityGrantCompatibilityPatch).GetMethod(
                    nameof(MechFusionAbilityGrantCompatibilityPatch.Postfix_LoadedGame),
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
                    "无法解析本项目的 MechFusion 科研能力补发 Postfix。",
                    new MissingMethodException(
                        typeof(MechFusionAbilityGrantCompatibilityPatch).FullName,
                        nameof(MechFusionAbilityGrantCompatibilityPatch.Postfix_ResearchFinish)
                        + " / "
                        + nameof(MechFusionAbilityGrantCompatibilityPatch.Postfix_LoadedGame)));
            }

            try
            {
                harmony.Patch(
                    finishProject!,
                    postfix: new HarmonyMethod(researchPostfix));
                harmony.Patch(
                    loadedGame!,
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
                    "安装 MechFusion 科研能力补发补丁时发生异常。",
                    ex,
                    finishProject!,
                    loadedGame!);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                "四项 MechFusion 后续科研完成时即时补发对应 Ability，"
                + "并在 GameComponentUtility.LoadedGame() 后按已完成科研兜底补发；"
                + "仅添加缺失能力，不执行任何移除逻辑。");
        }

        private static bool TryValidateUnlockDefs(out string failureReason)
        {
            string[] researchDefNames =
            {
                MechFusionAbilityGrantCompatibilityPatch.FusionResearchDefName,
                MechFusionAbilityGrantCompatibilityPatch.SuperFusionResearchDefName,
                MechFusionAbilityGrantCompatibilityPatch.EvolutionResearchDefName,
                MechFusionAbilityGrantCompatibilityPatch.ExtremizationResearchDefName
            };

            for (int i = 0; i < researchDefNames.Length; i++)
            {
                if (DefDatabase<ResearchProjectDef>.GetNamedSilentFail(
                        researchDefNames[i]) == null)
                {
                    failureReason =
                        $"未找到闪毁5融合研究 {researchDefNames[i]}，"
                        + "科研能力补发模块未安装。";
                    return false;
                }
            }

            string[] abilityDefNames =
            {
                MechFusionAbilityGrantCompatibilityPatch.FusionAbilityDefName,
                MechFusionAbilityGrantCompatibilityPatch.SuperFusionAbilityDefName,
                MechFusionAbilityGrantCompatibilityPatch.EvolutionAbilityDefName,
                MechFusionAbilityGrantCompatibilityPatch.ExtremizationAbilityDefName
            };

            for (int i = 0; i < abilityDefNames.Length; i++)
            {
                if (DefDatabase<AbilityDef>.GetNamedSilentFail(
                        abilityDefNames[i]) == null)
                {
                    failureReason =
                        $"未找到闪毁5融合能力 {abilityDefNames[i]}，"
                        + "科研能力补发模块未安装。";
                    return false;
                }
            }

            failureReason = string.Empty;
            return true;
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
                    "原版目标 GameComponentUtility.LoadedGame() 无法解析或签名不符。";
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
                // 回滚失败不得掩盖最初的安装异常。
            }
        }
    }
}
