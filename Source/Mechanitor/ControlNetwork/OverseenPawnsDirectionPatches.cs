using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // OverseenPawns 方向过滤：原版按全部 Overseer DirectRelations 枚举下属，
    // 在 reflexive 关系下会把上级误计入。仅对 MAP Vanilla 节点按控制组方向过滤。
    [HarmonyPatch(
        typeof(Pawn_MechanitorTracker),
        nameof(Pawn_MechanitorTracker.OverseenPawns),
        MethodType.Getter)]
    public static class Patch_Pawn_MechanitorTracker_OverseenPawns_MAPDirection
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn, ref List<Pawn> __result)
        {
            if (___pawn == null || __result == null || __result.Count == 0)
            {
                return;
            }

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.HasNode(___pawn)
                || !MAPMechanitorNodeUtility.UsesVanillaControlPath(___pawn))
            {
                return;
            }

            if (!NeedsDirectionFilter(___pawn, __result))
            {
                return;
            }

            List<Pawn> filtered = new List<Pawn>(__result.Count);
            for (int i = 0; i < __result.Count; i++)
            {
                Pawn subject = __result[i];
                if (subject == null)
                {
                    continue;
                }

                if (MAPOverseerRelationDirectionUtility.IsActualOverseerOf(___pawn, subject))
                {
                    filtered.Add(subject);
                }
            }

            __result = filtered;
        }

        private static bool NeedsDirectionFilter(Pawn mechanitor, List<Pawn> overseen)
        {
            for (int i = 0; i < overseen.Count; i++)
            {
                Pawn subject = overseen[i];
                if (subject == null)
                {
                    continue;
                }

                if (!MAPOverseerRelationDirectionUtility.IsActualOverseerOf(mechanitor, subject))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
