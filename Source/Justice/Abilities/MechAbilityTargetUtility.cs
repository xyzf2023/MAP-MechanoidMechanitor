using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MechAbilityTargetUtility
    {
        /// <summary>正义与太阳 BOSS 永久禁止骇入、再编码及重构，不受通用 BOSS 限制设置影响。</summary>
        internal static bool IsProtectedBoss(Pawn? pawn)
        {
            return JusticePawnUtility.IsBossJustice(pawn)
                || pawn?.GetComp<CompSunBossState>() != null;
        }
    }
}
