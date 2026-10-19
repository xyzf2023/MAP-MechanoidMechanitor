using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MovementCostImmunityUtility
    {
        // Local eligibility only. The shared core combines it with optional providers.
        internal static bool HasImmunity(Pawn? pawn)
        {
            return MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn, MechanoidMechanitorCapability.MovementCostImmunity);
        }
    }
}
