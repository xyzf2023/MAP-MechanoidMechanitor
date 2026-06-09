using Verse;

namespace MAP_MechanoidMechanitor
{
    // Global protection: MAP mechanitor node pawns only (via HasNode).
    // Does not protect ordinary mechs overseen by MAP nodes; those remain takeoverable via vanilla ControlMech.
    public static class MAPMechanitorControlProtectionUtility
    {
        public static bool IsProtectedMechanitorTarget(Pawn? target)
        {
            return IsProtectedMechanitorTarget(target, null);
        }

        public static bool IsProtectedMechanitorTarget(Pawn? target, Pawn? controller)
        {
            if (target == null)
            {
                return true;
            }

            if (!MAPMechanitorNodeUtility.HasNode(target))
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.RequiresExternalOverseer(target))
            {
                return true;
            }

            if (controller == null)
            {
                return true;
            }

            Pawn? currentOverseer = target.GetOverseer();
            if (currentOverseer == null || currentOverseer == controller)
            {
                return false;
            }

            return true;
        }
    }
}
