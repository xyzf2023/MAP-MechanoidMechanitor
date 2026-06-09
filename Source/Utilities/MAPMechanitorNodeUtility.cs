using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPMechanitorNodeUtility
    {
        public static bool TryGetNodeComp(Pawn? pawn, out CompMAPMechanitorNode? comp)
        {
            return CompMAPMechanitorNode.TryGetNodeComp(pawn, out comp);
        }

        public static bool HasNode(Pawn? pawn)
        {
            return CompMAPMechanitorNode.PawnHasNode(pawn);
        }

        public static bool IsShadowNode(Pawn? pawn)
        {
            return UsesShadowControlPath(pawn);
        }

        public static bool UsesVanillaControlPath(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return false;
            }

            return props.controlBackend == MAPMechanitorControlBackend.Vanilla
                && props.canControlMechs;
        }

        public static bool UsesShadowControlPath(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return false;
            }

            return props.controlBackend == MAPMechanitorControlBackend.Shadow
                && props.canControlMechs;
        }

        public static bool IsMechanitorNodeController(Pawn? pawn)
        {
            if (!PassesMechanitorNodeControllerBasics(pawn))
            {
                return false;
            }

            return UsesVanillaControlPath(pawn) || UsesShadowControlPath(pawn);
        }

        public static bool IsShadowController(Pawn? pawn)
        {
            return PassesMechanitorNodeControllerBasics(pawn) && UsesShadowControlPath(pawn);
        }

        public static bool IsOverseerlessShadowNode(Pawn? pawn)
        {
            return IsShadowController(pawn) && !RequiresExternalOverseer(pawn);
        }

        public static bool IsVanillaRelayNode(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return false;
            }

            return props.role == MAPMechanitorNodeRole.VanillaRelayMechanitor;
        }

        public static bool IsTemporaryTestNode(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return false;
            }

            return props.temporaryTestNode || props.role == MAPMechanitorNodeRole.ShadowTestNode;
        }

        public static bool AllowsExternalOverseer(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return false;
            }

            return props.allowExternalOverseer;
        }

        public static bool RequiresExternalOverseer(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return false;
            }

            return props.requiresExternalOverseer;
        }

        public static bool CanControlMechs(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return false;
            }

            return props.canControlMechs;
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return 0;
            }

            return props.extraMechBandwidth;
        }

        public static int GetExtraMechControlGroups(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return 0;
            }

            return props.extraMechControlGroups;
        }

        private static bool PassesMechanitorNodeControllerBasics(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
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

            return true;
        }

        private static bool TryGetProps(Pawn? pawn, out CompProperties_MAPMechanitorNode? props)
        {
            props = null;
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!TryGetNodeComp(pawn, out CompMAPMechanitorNode? comp) || comp == null)
            {
                return false;
            }

            props = comp.NodeProps;
            return props != null;
        }
    }
}
