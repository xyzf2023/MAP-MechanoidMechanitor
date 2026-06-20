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

        public static bool UsesVanillaControlPath(Pawn? pawn)
        {
            if (!TryGetProps(pawn, out CompProperties_MAPMechanitorNode? props) || props == null)
            {
                return false;
            }

            return props.controlBackend == MAPMechanitorControlBackend.Vanilla;
        }

        public static bool IsMechanitorNodeController(Pawn? pawn)
        {
            if (!PassesMechanitorNodeControllerBasics(pawn))
            {
                return false;
            }

            return UsesVanillaControlPath(pawn);
        }

        // Vanilla backend + requires external overseer (e.g. Hermit relay node).
        public static bool IsVanillaRelayMechanitorNode(Pawn? pawn)
        {
            if (!PassesMechanitorNodeControllerBasics(pawn))
            {
                return false;
            }

            return UsesVanillaControlPath(pawn) && RequiresExternalOverseer(pawn);
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

            return props.controlBackend == MAPMechanitorControlBackend.Vanilla;
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            if (!TryGetNodeComp(pawn, out CompMAPMechanitorNode? comp) || comp == null)
            {
                return 0;
            }

            return comp.CurrentIntrinsicBandwidth;
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
