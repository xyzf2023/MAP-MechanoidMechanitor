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

            return VanillaRelayMechanitorUtility.IsVanillaRelayMechanitor(pawn);
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

            if (MAPMechanitorControlProtectionUtility.IsProtectedMechanitorTarget(target))
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

            if (MAPMechanitorControlProtectionUtility.IsProtectedMechanitorTarget(target))
            {
                rejectReason = "Target is a MAP mechanitor node.";
                return false;
            }

            if (target.Faction == null || !target.Faction.IsPlayerSafe())
            {
                rejectReason = "Target is not player faction.";
                return false;
            }

            Pawn overseer = target.GetOverseer();
            if (overseer == null)
            {
                return true;
            }

            if (overseer == controller)
            {
                rejectReason = "Target is already controlled by this relay.";
                return false;
            }

            return CanRelayTakeOverFrom(controller!, target, overseer, out rejectReason);
        }

        // Hermit takeover layer: whether a relay may take a normal mech from oldOverseer.
        // Rejects MAP node targets and another hermit; MAP node overseers are allowed.
        // Overseer swap is handled by vanilla JobDriver_ControlMech, not here.
        public static bool CanRelayTakeOverFrom(
            Pawn controller,
            Pawn target,
            Pawn oldOverseer,
            out string rejectReason)
        {
            rejectReason = string.Empty;

            if (oldOverseer == null)
            {
                rejectReason = "No overseer to take over from.";
                return false;
            }

            if (oldOverseer == controller)
            {
                rejectReason = "Target is already controlled by this relay.";
                return false;
            }

            if (oldOverseer.Faction == null || !oldOverseer.Faction.IsPlayerSafe())
            {
                rejectReason = "Target overseer is not player faction.";
                return false;
            }

            if (VanillaRelayMechanitorUtility.IsVanillaRelayMechanitor(oldOverseer))
            {
                rejectReason = "Cannot take over from another vanilla relay mechanitor.";
                return false;
            }

            if (MAPMechanitorControlProtectionUtility.IsProtectedMechanitorTarget(target))
            {
                rejectReason = "Target is a MAP mechanitor node.";
                return false;
            }

            return true;
        }

        public static bool PassesVanillaControlBasics(Pawn pawn, Pawn mech, out string rejectReason)
        {
            rejectReason = string.Empty;

            if (!ModsConfig.BiotechActive)
            {
                rejectReason = "Biotech not active.";
                return false;
            }

            if (pawn.mechanitor == null)
            {
                rejectReason = "Controller has no mechanitor tracker.";
                return false;
            }

            if (!mech.IsColonyMech)
            {
                rejectReason = "Target is not a colony mech.";
                return false;
            }

            if (mech.Downed)
            {
                rejectReason = "Target is downed.";
                return false;
            }

            if (mech.Dead)
            {
                rejectReason = "Target is dead.";
                return false;
            }

            if (mech.IsAttacking())
            {
                rejectReason = "Target is attacking.";
                return false;
            }

            if (!MechanitorUtility.EverControllable(mech))
            {
                rejectReason = "Target is never controllable.";
                return false;
            }

            if (mech.GetOverseer() == pawn)
            {
                rejectReason = "Target is already controlled by this controller.";
                return false;
            }

            int availableBandwidth = pawn.mechanitor.TotalBandwidth - pawn.mechanitor.UsedBandwidth;
            float bandwidthCost = mech.GetStatValue(StatDefOf.BandwidthCost);
            if ((float)availableBandwidth < bandwidthCost)
            {
                rejectReason = "Not enough bandwidth.";
                return false;
            }

            return true;
        }
    }
}
