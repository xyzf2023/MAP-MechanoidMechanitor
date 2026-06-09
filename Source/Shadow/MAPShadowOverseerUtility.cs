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
            if (!PassesShadowControlBasics(controller, target, out rejectReason))
            {
                return false;
            }

            Pawn? vanillaOverseer = target!.GetOverseer();
            if (vanillaOverseer != null)
            {
                if (vanillaOverseer == controller)
                {
                    rejectReason = "Target is already controlled by this controller.";
                    return false;
                }

                if (vanillaOverseer.Faction == null || !vanillaOverseer.Faction.IsPlayerSafe())
                {
                    rejectReason = "Target overseer is not player faction.";
                    return false;
                }

                return true;
            }

            Pawn? shadowOverseer = GetShadowOverseer(target);
            if (shadowOverseer != null)
            {
                if (shadowOverseer == controller)
                {
                    rejectReason = "Target is already shadow controlled by this controller.";
                    return false;
                }

                if (shadowOverseer.Faction == null || !shadowOverseer.Faction.IsPlayerSafe())
                {
                    rejectReason = "Target shadow overseer is not player faction.";
                    return false;
                }

                return true;
            }

            return true;
        }

        public static bool TrySetShadowOverseer(Pawn? controller, Pawn? target, out string rejectReason)
        {
            return TryAssignOrTakeOverShadowOverseer(controller, target, out rejectReason);
        }

        public static bool TryAssignOrTakeOverShadowOverseer(Pawn? controller, Pawn? target, out string rejectReason)
        {
            if (!CanAssignShadowOverseer(controller, target, out rejectReason))
            {
                return false;
            }

            if (target == null || controller == null)
            {
                rejectReason = "Target or controller is null.";
                return false;
            }

            GameComponent_MAPShadowOverseerTracker? tracker = Tracker;
            if (tracker == null)
            {
                rejectReason = "Shadow overseer tracker unavailable.";
                return false;
            }

            Pawn? oldVanillaOverseer = target.GetOverseer();
            if (oldVanillaOverseer != null && oldVanillaOverseer.relations != null)
            {
                oldVanillaOverseer.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, target);
            }

            tracker.SetShadowOverseer(target, controller);
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

        public static bool IsTakeoverScenario(Pawn? target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.GetOverseer() != null)
            {
                return true;
            }

            return HasShadowOverseer(target);
        }

        private static bool PassesShadowControlBasics(Pawn? controller, Pawn? target, out string rejectReason)
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

            if (!MAPMechanitorNodeUtility.UsesShadowControlPath(controller))
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

            if (controller.mechanitor == null)
            {
                rejectReason = "Controller has no mechanitor tracker.";
                return false;
            }

            if (!target.IsColonyMech)
            {
                rejectReason = "Target is not a colony mech.";
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

            if (target.Downed)
            {
                rejectReason = "Target is downed.";
                return false;
            }

            if (target.Dead)
            {
                rejectReason = "Target is dead.";
                return false;
            }

            if (target.IsAttacking())
            {
                rejectReason = "Target is attacking.";
                return false;
            }

            if (!MechanitorUtility.EverControllable(target))
            {
                rejectReason = "Target is never controllable.";
                return false;
            }

            int availableBandwidth = controller.mechanitor.TotalBandwidth - controller.mechanitor.UsedBandwidth;
            float bandwidthCost = target.GetStatValue(StatDefOf.BandwidthCost);
            if ((float)availableBandwidth < bandwidthCost)
            {
                rejectReason = "Not enough bandwidth.";
                return false;
            }

            return true;
        }
    }
}
