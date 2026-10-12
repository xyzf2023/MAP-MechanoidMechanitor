using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 为授权仿生伴侣补充普通类人床的分配候选与搬迁后的床主恢复资格。
    /// 实际分配、体型、奴隶身份及共享床戒律仍使用原版流程。
    /// </summary>
    public static class SyntheticCompanionBedAssignmentPatches
    {
        private static bool IsAssignableCompanion(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.Faction == Faction.OfPlayer
                && !pawn.IsPrisoner
                && pawn.RaceProps?.IsMechanoid == true
                && pawn.ownership != null
                && SyntheticCompanionStateUtility.IsSyntheticCompanion(pawn);
        }

        private static bool IsOrdinaryHumanlikeBed(CompAssignableToPawn_Bed assignable)
        {
            return assignable.parent is Building_Bed bed
                && !(assignable is CompAssignableToPawn_DeathrestCasket)
                && bed.def.building?.bed_humanlike == true
                && bed.def != ThingDefOf.DeathrestCasket
                && !bed.Medical
                && !bed.ForPrisoners;
        }

        [HarmonyPatch(typeof(CompAssignableToPawn_Bed),
            nameof(CompAssignableToPawn_Bed.AssigningCandidates), MethodType.Getter)]
        public static class Patch_AssigningCandidates
        {
            [HarmonyPostfix]
            public static void Postfix(CompAssignableToPawn_Bed __instance,
                ref IEnumerable<Pawn> __result)
            {
                if (!__instance.parent.Spawned || !IsOrdinaryHumanlikeBed(__instance))
                    return;

                Map map = __instance.parent.Map;
                List<Pawn> companions = map.mapPawns.PawnsInFaction(Faction.OfPlayer)
                    .Where(pawn => pawn.Spawned && pawn.Map == map && IsAssignableCompanion(pawn))
                    .ToList();
                if (companions.Count == 0) return;

                // 保留原版及其他补丁提供的候选；去重以兼容已被其他机制视为殖民者的机械体。
                __result = __result.Concat(companions).Distinct()
                    .OrderByDescending(pawn => __instance.CanAssignTo(pawn).Accepted
                        && !__instance.IdeoligionForbids(pawn) ? 1 : 0)
                    .ThenBy(pawn => pawn.LabelShort);
            }
        }

        [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.CanAssignTo))]
        public static class Patch_CanAssignTo
        {
            [HarmonyPostfix]
            public static void Postfix(CompAssignableToPawn_Bed __instance,
                Pawn pawn, ref AcceptanceReport __result)
            {
                if (!__result.Accepted || !IsOrdinaryHumanlikeBed(__instance)
                    || !IsAssignableCompanion(pawn) || pawn.ideo?.Ideo != null) return;

                // 共享床可能被另一床主的戒律禁止。原版窗口会用候选的 ideo 绘制图标，
                // 无意识形态的机械体改为不可分配原因，避免空引用且保留另一床主的戒律。
                if (__instance.IdeoligionForbids(pawn))
                    __result = "IdeoligionForbids".Translate();
            }
        }

        [HarmonyPatch(typeof(CompAssignableToPawn_Bed), "CanSetUninstallAssignedPawn")]
        public static class Patch_CanSetUninstallAssignedPawn
        {
            [HarmonyPostfix]
            public static void Postfix(CompAssignableToPawn_Bed __instance,
                Pawn pawn, ref bool __result)
            {
                if (__result || !IsOrdinaryHumanlikeBed(__instance)
                    || !IsAssignableCompanion(pawn)) return;

                // 原版在此仅接受殖民者或囚犯；授权机械体使用同一套床位恢复流程。
                __result = !__instance.AssignedAnything(pawn)
                    && __instance.CanAssignTo(pawn).Accepted
                    && !__instance.IdeoligionForbids(pawn);
            }
        }
    }
}
