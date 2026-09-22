using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.Forgenest
{
    internal static class ForgenestMechanitorSelectionPatch
    {
        public static void Postfix(ThingComp __instance, ref IEnumerable<Pawn> __result)
        {
            __result = AppendMechanitors(__instance, __result);
        }

        private static IEnumerable<Pawn> AppendMechanitors(
            ThingComp producer,
            IEnumerable<Pawn>? original)
        {
            // 保留第三方原有候选与顺序，仅为追加项去重；不修改其菜单和生产逻辑。
            HashSet<Pawn> seen = new HashSet<Pawn>();
            if (original != null)
            {
                foreach (Pawn pawn in original)
                {
                    seen.Add(pawn);
                    yield return pawn;
                }
            }

            Map? map = producer?.parent?.Map;
            if (map == null || !ModsConfig.BiotechActive)
            {
                yield break;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null
                    || pawn.Dead
                    || pawn.Destroyed
                    || pawn.Faction != Faction.OfPlayer
                    || pawn.mechanitor == null
                    || pawn.relations == null
                    || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                    || !seen.Add(pawn))
                {
                    continue;
                }

                // 身份来自公共注册表；查询不补建Tracker，也不向机械体添加原版植入体。
                yield return pawn;
            }
        }
    }
}
