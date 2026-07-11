using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPShuttlePilotUtility
    {
        private const float MinPilotingAbility = 0.1f;

        public static bool CanServeAsShuttlePilot(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.OdysseyActive)
            {
                return false;
            }

            if (pawn.Dead || pawn.Destroyed || pawn.Downed)
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.AllowsShuttlePilot(pawn))
            {
                return false;
            }

            if (StatDefOf.PilotingAbility.Worker.IsDisabledFor(pawn))
            {
                return false;
            }

            return pawn.GetStatValue(StatDefOf.PilotingAbility, true, -1) > MinPilotingAbility;
        }
    }
}
