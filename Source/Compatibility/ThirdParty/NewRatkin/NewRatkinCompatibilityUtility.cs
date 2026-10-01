using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.NewRatkin
{
    internal static class NewRatkinCompatibilityUtility
    {
        internal const string PackageId = "Solaris.RatkinRaceMod";

        // 查询正式身份，不初始化 Pawn 或修改任何注册表。
        internal static bool IsPlayerMechanitor(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction?.IsPlayer == true
                && !pawn.IsPrisoner
                && !pawn.IsSlave
                && pawn.HostFaction == null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }
    }
}
