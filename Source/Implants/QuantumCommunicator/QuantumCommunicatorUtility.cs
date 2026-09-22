using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class QuantumCommunicatorUtility
    {
        public static bool HasImplant(Pawn? pawn)
        {
            return ImplantEffectUtility.HasHediff(
                pawn,
                MAPMechanitor_HediffDefOf.MAP_QuantumCommunicator);
        }

        public static bool HasEffect(Pawn? pawn)
        {
            return HasImplant(pawn)
                || MechFusionMechanitorSynchronizationService
                    .GrantsQuantumCommunicatorEffect(pawn);
        }

    }
}
