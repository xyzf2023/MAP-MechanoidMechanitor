using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_GravshipPilotUser : CompProperties
    {
        public bool allowGravshipPilotConsole = true;

        public CompProperties_GravshipPilotUser()
        {
            compClass = typeof(CompGravshipPilotUser);
        }
    }

    public class CompGravshipPilotUser : ThingComp
    {
        public CompProperties_GravshipPilotUser Props =>
            (CompProperties_GravshipPilotUser)props;

        public static bool PawnCanUseGravshipPilotConsole(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return false;
            }

            if (!ModsConfig.OdysseyActive)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            CompGravshipPilotUser? comp = pawn.GetComp<CompGravshipPilotUser>();
            if (comp == null)
            {
                return false;
            }

            return comp.Props.allowGravshipPilotConsole;
        }
    }
}
