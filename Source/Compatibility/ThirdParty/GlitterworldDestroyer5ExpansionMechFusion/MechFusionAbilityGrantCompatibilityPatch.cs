using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion
{
    /// <summary>
    /// 根据闪毁5融合后续科研状态，为机械族机械师补发对应 Ability。
    /// 本补丁只添加缺失能力，绝不移除能力，也不写入 GeneTracker。
    /// </summary>
    internal static class MechFusionAbilityGrantCompatibilityPatch
    {
        internal const string FusionResearchDefName = "MF_Fusion";
        internal const string SuperFusionResearchDefName = "MF_SuperFusion";
        internal const string EvolutionResearchDefName = "MF_EvolutionStone";
        internal const string ExtremizationResearchDefName = "MF_ExtremizationStone";

        internal const string FusionAbilityDefName = "MechFusion";
        internal const string SuperFusionAbilityDefName = "SuperMechFusion";
        internal const string EvolutionAbilityDefName = "Evolution";
        internal const string ExtremizationAbilityDefName = "Extremization";

        internal static void Postfix_ResearchFinish(ResearchProjectDef? proj)
        {
            if (proj == null)
            {
                return;
            }

            AbilityDef? abilityDef = ResolveAbilityForResearch(proj.defName);
            if (abilityDef == null)
            {
                return;
            }

            GrantAbilityToRegisteredMechanitorsIfMissing(abilityDef);
        }

        internal static void Postfix_LoadedGame()
        {
            GrantIfResearchFinished(FusionResearchDefName, FusionAbilityDefName);
            GrantIfResearchFinished(SuperFusionResearchDefName, SuperFusionAbilityDefName);
            GrantIfResearchFinished(EvolutionResearchDefName, EvolutionAbilityDefName);
            GrantIfResearchFinished(ExtremizationResearchDefName, ExtremizationAbilityDefName);
        }

        private static AbilityDef? ResolveAbilityForResearch(string researchDefName)
        {
            string? abilityDefName = researchDefName switch
            {
                FusionResearchDefName => FusionAbilityDefName,
                SuperFusionResearchDefName => SuperFusionAbilityDefName,
                EvolutionResearchDefName => EvolutionAbilityDefName,
                ExtremizationResearchDefName => ExtremizationAbilityDefName,
                _ => null
            };

            if (abilityDefName == null)
            {
                return null;
            }

            return DefDatabase<AbilityDef>.GetNamedSilentFail(abilityDefName);
        }

        private static void GrantIfResearchFinished(
            string researchDefName,
            string abilityDefName)
        {
            ResearchProjectDef? researchDef =
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchDefName);
            if (researchDef == null || !researchDef.IsFinished)
            {
                return;
            }

            AbilityDef? abilityDef =
                DefDatabase<AbilityDef>.GetNamedSilentFail(abilityDefName);
            if (abilityDef == null)
            {
                return;
            }

            GrantAbilityToRegisteredMechanitorsIfMissing(abilityDef);
        }

        private static void GrantAbilityToRegisteredMechanitorsIfMissing(
            AbilityDef abilityDef)
        {
            try
            {
                GrantAbilityToRegisteredMechanitorsCore(abilityDef);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] MechFusion 科研能力补发批次失败，已隔离异常："
                    + abilityDef.defName + "。\n" + ex);
            }
        }

        private static void GrantAbilityToRegisteredMechanitorsCore(AbilityDef abilityDef)
        {
            // 能力初始化可能回调身份注册表；使用快照避免批次中途改变目标集合。
            List<Pawn> mechanitors = new List<Pawn>(
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors);
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn pawn = mechanitors[i];
                try
                {
                    if (!MechFusionCompatibleMechanitorUtility.IsEligibleMechanitor(pawn))
                    {
                        continue;
                    }

                    Pawn_AbilityTracker? abilityTracker = pawn.abilities;
                    if (abilityTracker == null)
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 第三方兼容（MechFusion 科研能力补发）："
                            + $"机械族机械师 {pawn.LabelShortCap} 缺少 Pawn_AbilityTracker，"
                            + $"无法补发能力 {abilityDef.defName}。");
                        continue;
                    }

                    if (abilityTracker.GetAbility(abilityDef, includeTemporary: true) == null)
                    {
                        abilityTracker.GainAbility(abilityDef);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] MechFusion 科研能力补发失败，继续处理其他机械师："
                        + $"pawn={pawn?.ThingID}，ability={abilityDef.defName}。\n{ex}");
                }
            }
        }
    }
}
