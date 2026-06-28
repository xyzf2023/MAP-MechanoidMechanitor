using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 由机械族机械师身份模块统一授予 Warden、AnimalHandling、Mechanical Childcare 与通用背景故事。
    /// </summary>
    public static class MechanoidMechanitorWorkAuthorizationUtility
    {
        public static void GrantAndEnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            MechanoidBackstoryUtility.EnsureGenericBackstories(pawn);
            WardenWorkUtility.GrantAndEnsureInfrastructure(pawn);

            if (pawn.GetComp<CompAnimalHandlingWorkUser>() != null)
            {
                AnimalHandlingWorkUtility.EnsureInfrastructure(pawn);
            }
            else
            {
                AnimalHandlingWorkUtility.GrantAndEnsureInfrastructure(pawn, 3);
            }

            if (pawn.GetComp<CompMechanicalChildcareUser>() != null)
            {
                MechanicalChildcareUtility.EnsureInfrastructure(pawn);
            }
            else
            {
                MechanicalChildcareUtility.GrantAndEnsureInfrastructure(pawn, 3);
            }
        }
    }
}
