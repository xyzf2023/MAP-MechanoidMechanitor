using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class JusticeScenarioFreeColonistUtility
    {
        /// <summary>
        /// 全局资格：仅在正义专属剧本启用时有效，且仅允许唯一的剧本主角充当自由殖民者替代者。
        /// 另需为机械族机械师、玩家派系成员，并存活、未销毁、非俘虏且无 HostFaction。
        /// </summary>
        public static bool IsEligibleGlobal(Pawn? pawn)
        {
            if (!GameComponent_JusticeScenarioState.IsEnabled || pawn == null)
            {
                return false;
            }

            if (!JusticeScenarioUtility.IsScenarioProtagonist(pawn)
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer
                || pawn.Dead
                || pawn.HostFaction != null
                || pawn.IsPrisoner
                || pawn.Destroyed)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 地图资格：在全局资格基础上，检查 Pawn 与指定地图的关系。
        /// </summary>
        public static bool IsEligibleOnMap(
            Pawn? pawn,
            MapPawns mapPawns,
            bool requireSpawned)
        {
            if (!IsEligibleGlobal(pawn))
            {
                return false;
            }

            if (requireSpawned)
            {
                if (!pawn!.Spawned || pawn.Map == null)
                {
                    return false;
                }

                if (pawn.Map.mapPawns != mapPawns)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 主角是否位于地图、远行队或运输载具等 PawnsFinder 全局存活集合中。
        /// </summary>
        public static bool IsPresentInMapsCaravansAndTransporters(Pawn pawn)
        {
            return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive.Contains(pawn);
        }
    }
}
