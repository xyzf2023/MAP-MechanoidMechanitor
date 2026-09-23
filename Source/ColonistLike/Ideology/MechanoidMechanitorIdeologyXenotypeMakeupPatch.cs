using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 空基因追踪器不代表机械身体属于人类异种人口；仅在实际排除成员时接管原版统计。
    /// </summary>
    [HarmonyPatch(typeof(ThoughtWorker_Precept_ColonyXenotypeMakeup), "ShouldHaveThought")]
    internal static class MechanoidMechanitorIdeologyXenotypeMakeupPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            ThoughtWorker_Precept_ColonyXenotypeMakeup __instance,
            Pawn p,
            ref ThoughtState __result)
        {
            if (!ModsConfig.IdeologyActive
                || !ModsConfig.BiotechActive
                || __instance.def == null
                || __instance.def != MechanoidMechanitorIdeologyBodyRequirementUtility.XenotypeMakeupThought
                || p == null
                || p.Faction == null)
            {
                return true;
            }

            Ideo? ideo = p.Ideo;
            if (ideo == null
                || (!ideo.PreferredXenotypes.Any() && !ideo.PreferredCustomXenotypes.Any()))
            {
                return true;
            }

            Map? map = p.MapHeld;
            if (map == null)
            {
                if (!MechanoidMechanitorIdeologyBodyRequirementUtility.IsExemptMechanicalMember(p))
                {
                    return true;
                }

                __result = ThoughtState.Inactive;
                return false;
            }

            List<Pawn> pawns = map.mapPawns.SpawnedPawnsInFaction(p.Faction);
            bool restrictedGroup = p.IsSlave || p.IsPrisoner;
            bool excludedMechanicalMember = false;
            int population = 0;
            int nonPreferred = 0;

            foreach (Pawn member in pawns)
            {
                if (member.genes == null
                    || restrictedGroup != (member.IsSlave || member.IsPrisoner))
                {
                    continue;
                }

                if (MechanoidMechanitorIdeologyBodyRequirementUtility.IsExemptMechanicalMember(member))
                {
                    excludedMechanicalMember = true;
                    continue;
                }

                population++;
                if (!ideo.IsPreferredXenotype(member))
                {
                    nonPreferred++;
                }
            }

            if (!excludedMechanicalMember)
            {
                return true;
            }

            // 没有生物学统计对象不等于“所有人都属于偏好异种”。
            if (population == 0)
            {
                __result = ThoughtState.Inactive;
            }
            else if (nonPreferred == 0)
            {
                __result = ThoughtState.ActiveAtStage(0);
            }
            else
            {
                // 保持原版 0.33 / 0.66 阈值，不改为数学上的三分之一 / 三分之二。
                float fraction = (float)nonPreferred / population;
                __result = ThoughtState.ActiveAtStage(fraction < 0.33f ? 1 : fraction < 0.66f ? 2 : 3);
            }

            return false;
        }
    }
}
