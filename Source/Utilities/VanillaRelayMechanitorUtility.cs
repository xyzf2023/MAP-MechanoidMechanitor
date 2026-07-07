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

            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn!);
        }
    }
}
