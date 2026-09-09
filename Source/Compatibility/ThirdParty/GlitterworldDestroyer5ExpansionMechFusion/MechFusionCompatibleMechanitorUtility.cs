using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion
{
    /// <summary>
    /// 闪毁5融合兼容层统一使用的机械族机械师身份判断。
    /// 这里只判断“是否属于本兼容应接纳的机械族机械师”，不附带任何能力、健康状态或研究同步逻辑。
    /// </summary>
    internal static class MechFusionCompatibleMechanitorUtility
    {
        internal static bool IsEligibleMechanitor(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            return pawn.mechanitor != null;
        }
    }
}
