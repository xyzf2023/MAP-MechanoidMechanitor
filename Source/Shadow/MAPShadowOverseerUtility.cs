using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // MAP-owned shadow control records for ordinary mechs; not vanilla PawnRelationDefOf.Overseer.
    // Shadow controllers should not hold vanilla overseer; targets are tracked here instead.
    public static class MAPShadowOverseerUtility
    {
        public static GameComponent_MAPShadowOverseerTracker? Tracker
        {
            get
            {
                return GameComponent_MAPShadowOverseerTracker.EnsureInstance();
            }
        }

        public static Pawn? GetShadowOverseer(Pawn? mech)
        {
            return Tracker?.GetShadowOverseer(mech);
        }

        public static bool HasShadowOverseer(Pawn? mech)
        {
            return Tracker?.HasShadowOverseer(mech) == true;
        }

        public static bool IsShadowOverseenBy(Pawn? mech, Pawn? controller)
        {
            return Tracker?.IsShadowOverseenBy(mech, controller) == true;
        }

        public static bool CanAssignShadowOverseer(Pawn? controller, Pawn? target, out string rejectReason)
        {
            rejectReason = string.Empty;

            if (!ModsConfig.BiotechActive)
            {
                rejectReason = "Biotech not active.";
                return false;
            }

            if (controller == null)
            {
                rejectReason = "Controller is null.";
                return false;
            }

            if (target == null)
            {
                rejectReason = "Target is null.";
                return false;
            }

            if (!MAPMechanitorNodeUtility.IsShadowController(controller))
            {
                rejectReason = "Controller is not a shadow mechanitor node.";
                return false;
            }

            if (!MAPMechanitorNodeUtility.IsOverseerlessShadowNode(controller))
            {
                rejectReason = "Controller is not an overseerless shadow node.";
                return false;
            }

            if (controller.GetOverseer() != null)
            {
                rejectReason = "Controller has a vanilla overseer.";
                return false;
            }

            if (!target.RaceProps.IsMechanoid)
            {
                rejectReason = "Target is not a mechanoid.";
                return false;
            }

            if (target.Faction == null || !target.Faction.IsPlayerSafe())
            {
                rejectReason = "Target is not player faction.";
                return false;
            }

            if (target == controller)
            {
                rejectReason = "Target is self.";
                return false;
            }

            if (MAPMechanitorControlProtectionUtility.IsProtectedMechanitorTarget(target))
            {
                rejectReason = "Target is a MAP mechanitor node.";
                return false;
            }

            if (target.Dead)
            {
                rejectReason = "Target is dead.";
                return false;
            }

            if (target.GetOverseer() != null)
            {
                rejectReason = "Target already has a vanilla overseer.";
                return false;
            }

            if (HasShadowOverseer(target))
            {
                rejectReason = "Target already has a shadow overseer.";
                return false;
            }

            if (!MechanitorUtility.EverControllable(target))
            {
                rejectReason = "Target is never controllable.";
                return false;
            }

            return true;
        }

        public static bool TrySetShadowOverseer(Pawn? controller, Pawn? target, out string rejectReason)
        {
            if (!CanAssignShadowOverseer(controller, target, out rejectReason))
            {
                return false;
            }

            GameComponent_MAPShadowOverseerTracker? tracker = Tracker;
            if (tracker == null)
            {
                rejectReason = "Shadow overseer tracker unavailable.";
                return false;
            }

            tracker.SetShadowOverseer(target!, controller!);
            return true;
        }

        public static void RemoveShadowOverseer(Pawn? target)
        {
            Tracker?.RemoveShadowOverseer(target);
        }

        public static void RemoveAllForController(Pawn? controller)
        {
            Tracker?.RemoveAllForController(controller);
        }
    }
}
