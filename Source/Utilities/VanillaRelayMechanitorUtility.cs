using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class VanillaRelayMechanitorUtility
    {
        public static bool IsVanillaRelayMechanitor(Pawn? pawn)
        {
            return MAPMechanitorNodeUtility.IsVanillaRelayMechanitorNode(pawn);
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
