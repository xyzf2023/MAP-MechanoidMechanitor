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

        public static bool IsProtectedOverseer(Pawn? overseer)
        {
            if (overseer == null)
            {
                return false;
            }

            return MAPMechanitorNodeUtility.HasNode(overseer);
        }
    }
}
