using Verse;

namespace MAP_MechanoidMechanitor
{
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
