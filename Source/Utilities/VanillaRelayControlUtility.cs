using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class VanillaRelayControlUtility
    {
        public static bool IsVanillaRelayController(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (!VanillaRelayMechanitorUtility.IsVanillaRelayMechanitor(pawn))
            {
                return false;
            }

            return MAPMechanitorNodeUtility.CanControlMechs(pawn);
        }

        public static bool IsForbiddenRelayTarget(Pawn? target)
        {
            if (target == null)
            {
                return true;
            }

            if (!ModsConfig.BiotechActive)
            {
                return true;
            }

            if (!target.RaceProps.IsMechanoid)
            {
                return true;
            }

            if (target.Dead)
            {
                return true;
            }

            if (MAPMechanitorNodeUtility.HasNode(target))
            {
                return true;
            }

            if (target.Faction == null || !target.Faction.IsPlayerSafe())
            {
                return true;
            }

            return false;
        }

        public static bool CanRelayControlTarget(Pawn? controller, Pawn? target, out string rejectReason)
        {
            rejectReason = string.Empty;

            if (!IsVanillaRelayController(controller))
            {
                rejectReason = "Controller is not a vanilla relay mechanitor.";
                return false;
            }

            if (target == null)
            {
                rejectReason = "Target is null.";
                return false;
            }

            if (target == controller)
            {
                rejectReason = "Target is self.";
                return false;
            }

            if (target.Dead)
            {
                rejectReason = "Target is dead.";
                return false;
            }

            if (!target.RaceProps.IsMechanoid)
            {
                rejectReason = "Target is not a mechanoid.";
                return false;
            }

            if (MAPMechanitorNodeUtility.HasNode(target))
            {
                rejectReason = "Target is a mechanitor node.";
                return false;
            }

            if (target.Faction == null || !target.Faction.IsPlayerSafe())
            {
                rejectReason = "Target is not player faction.";
                return false;
            }

            Pawn overseer = target.GetOverseer();
            if (overseer != null && overseer != controller)
            {
                rejectReason = "Target already has an overseer.";
                return false;
            }

            return true;
        }
    }
}
