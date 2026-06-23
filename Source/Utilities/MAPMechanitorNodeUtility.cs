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
            return CompMAPMechanitorNode.PawnHasNode(pawn)
                || MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn);
        }

        public static bool UsesVanillaControlPath(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.UsesVanillaControlPath(pawn);
        }

        public static bool IsMechanitorNodeController(Pawn? pawn)
        {
            if (!PassesMechanitorNodeControllerBasics(pawn))
            {
                return false;
            }

            return UsesVanillaControlPath(pawn);
        }

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
            return MechanoidMechanitorRoleUtility.RequiresExternalOverseer(pawn);
        }

        public static bool CanControlMechs(Pawn? pawn)
        {
            return PassesMechanitorNodeControllerBasics(pawn)
                && MechanoidMechanitorRoleUtility.UsesVanillaControlPath(pawn);
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.GetExtraMechBandwidth(pawn);
        }

        public static int GetExtraMechControlGroups(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.GetExtraMechControlGroups(pawn);
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

            return HasNode(pawn);
        }
    }
}
