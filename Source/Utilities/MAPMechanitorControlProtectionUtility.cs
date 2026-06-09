using Verse;

namespace MAP_MechanoidMechanitor
{
    // Global protection: MAP mechanitor node pawns only (via HasNode).
    // Does not protect ordinary mechs overseen by MAP nodes; those remain takeoverable via vanilla ControlMech.
    public static class MAPMechanitorControlProtectionUtility
    {
        public static bool IsProtectedMechanitorTarget(Pawn? target)
        {
            if (target == null)
            {
                return true;
            }

            return MAPMechanitorNodeUtility.HasNode(target);
        }
    }
}
