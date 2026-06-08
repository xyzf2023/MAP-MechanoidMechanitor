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
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props))
            {
                return false;
            }

            return props.controlBackend == MAPMechanitorControlBackend.Shadow;
        }

        public static bool IsVanillaRelayNode(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props))
            {
                return false;
            }

            return props.role == MAPMechanitorNodeRole.VanillaRelayMechanitor;
        }

        public static bool IsTemporaryTestNode(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props))
            {
                return false;
            }

            return props.temporaryTestNode || props.role == MAPMechanitorNodeRole.ShadowTestNode;
        }

        public static bool AllowsExternalOverseer(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props))
            {
                return false;
            }

            return props.allowExternalOverseer;
        }

        public static bool CanControlMechs(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props))
            {
                return false;
            }

            return props.canControlMechs;
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props))
            {
                return 0;
            }

            return props.extraMechBandwidth;
        }

        public static int GetExtraMechControlGroups(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props))
            {
                return 0;
            }

            return props.extraMechControlGroups;
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
