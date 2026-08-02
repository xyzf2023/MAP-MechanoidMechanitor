using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(SchoolUtility), nameof(SchoolUtility.FindTeacher))]
    public static class Patch_SchoolUtility_FindTeacher_MechanicalChildcare
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn child, ref Pawn? __result)
        {
            // 保留原版及其他模组已经找到的教师，不改变普通人类教师的优先逻辑。
            if (__result != null || !ModsConfig.BiotechActive || child == null)
            {
                return;
            }

            Map? map = child.Map;
            if (map == null)
            {
                return;
            }

            List<Pawn> spawnedPlayerPawns =
                map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);

            for (int i = 0; i < spawnedPlayerPawns.Count; i++)
            {
                Pawn candidate = spawnedPlayerPawns[i];
                if (!IsMechanicalTeacherCandidate(child, candidate))
                {
                    continue;
                }

                // 继续使用原版的征召、倒地、保育开关、说话能力、清醒和需求等检查。
                if (!SchoolUtility.CanTeachNow(candidate))
                {
                    continue;
                }

                if (!candidate.CanReach(child, PathEndMode.Touch, Danger.Deadly))
                {
                    continue;
                }

                __result = candidate;
                return;
            }
        }

        private static bool IsMechanicalTeacherCandidate(Pawn child, Pawn? candidate)
        {
            if (candidate == null
                || ReferenceEquals(candidate, child)
                || candidate.Destroyed
                || candidate.Dead
                || !candidate.Spawned
                || candidate.Map != child.Map
                || candidate.Faction != Faction.OfPlayer
                || candidate.HostFaction != null)
            {
                return false;
            }

            if (ModsConfig.AnomalyActive && candidate.IsSubhuman)
            {
                return false;
            }

            if (!MechanicalChildcareUtility.IsAuthorized(candidate))
            {
                return false;
            }

            // CanTeachNow 会直接访问 workSettings；这里只做基础设施完整性保护，
            // 不在高频教师查找路径中主动修复或改写 Pawn 状态。
            return MechanicalChildcareUtility.ChildcareWorkType != null
                && candidate.workSettings != null;
        }
    }
}
