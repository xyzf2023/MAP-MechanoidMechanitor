using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class VanillaRelayMechanitorUtility
    {
        public static bool IsVanillaRelayMechanitor(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.IsVanillaRelayNode(pawn))
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.CanControlMechs(pawn))
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.AllowsExternalOverseer(pawn))
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.TryGetNodeComp(pawn, out CompMAPMechanitorNode? comp)
                || comp?.NodeProps == null
                || comp.NodeProps.controlBackend != MAPMechanitorControlBackend.Vanilla)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            return true;
        }

        public static void EnsureVanillaRelayMechanitorState(Pawn? pawn)
        {
            if (!IsVanillaRelayMechanitor(pawn))
            {
                return;
            }

            if (pawn!.relations == null)
            {
                pawn.relations = new Pawn_RelationsTracker(pawn);
            }

            bool createdMechanitor = false;
            if (pawn.mechanitor == null)
            {
                pawn.mechanitor = new Pawn_MechanitorTracker(pawn);
                createdMechanitor = true;
            }

            if (createdMechanitor
                || pawn.mechanitor.controlGroups == null
                || pawn.mechanitor.controlGroups.Count == 0)
            {
                pawn.mechanitor.Notify_PawnSpawned(true);
            }
        }
    }
}
