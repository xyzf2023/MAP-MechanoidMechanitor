using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class FactionOutpostThreatUtility
    {
        /// <summary>
        /// 是否仍有属于前哨所有者、存活且未倒地的守军。这里故意不按“是否敌视玩家”判断，
        /// 防止战斗期间外交关系变化让尚有守军的前哨被错误结算为已摧毁。
        /// </summary>
        public static bool AnyStandingDefender(Map? map, Faction? owner)
        {
            if (map == null || owner == null || map.Disposed)
            {
                return false;
            }

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn? pawn = pawns[i];
                if (pawn == null
                    || pawn.Destroyed
                    || pawn.health == null
                    || pawn.Faction != owner)
                {
                    continue;
                }

                if (!pawn.Dead && !pawn.Downed)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
