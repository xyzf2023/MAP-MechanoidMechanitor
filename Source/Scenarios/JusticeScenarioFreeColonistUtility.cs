using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class JusticeScenarioFreeColonistUtility
    {
        public static bool IsEligible(
            Pawn? pawn,
            MapPawns mapPawns,
            bool requireSpawned)
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

            if (requireSpawned)
            {
                if (!pawn.Spawned || pawn.Map == null)
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
    }
}
