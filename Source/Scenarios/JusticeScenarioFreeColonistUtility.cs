using MAP_MechanoidMechanitor;
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
            if (!GameComponent_JusticeScenarioState.IsEnabled)
            {
                return false;
            }

            if (pawn == null)
            {
                return false;
            }

            if (!CompFreeColonistEquivalentUser.IsOptedIn(pawn))
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (pawn.Dead)
            {
                return false;
            }

            if (pawn.HostFaction != null)
            {
                return false;
            }

            if (pawn.IsPrisoner)
            {
                return false;
            }

            if (pawn.Destroyed)
            {
                return false;
            }

            if (requireSpawned)
            {
                if (!pawn.Spawned)
                {
                    return false;
                }

                if (pawn.Map == null)
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
