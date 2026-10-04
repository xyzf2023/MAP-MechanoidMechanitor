using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版及经第三方模块验证的起飞仪式入口；不按类型或名称泛化其他仪式。
    /// </summary>
    public static class GravshipLaunchRitualUtility
    {
        private static PreceptDef? additionalLaunchA;
        private static PreceptDef? additionalLaunchB;

        internal static void SetAdditionalLaunchDefs(PreceptDef? first, PreceptDef? second)
        {
            additionalLaunchA = first;
            additionalLaunchB = second;
        }

        public static bool IsSupportedLaunchDef(PreceptDef? def)
        {
            return ModsConfig.OdysseyActive && def != null
                && (def == PreceptDefOf.GravshipLaunch
                    || def == additionalLaunchA || def == additionalLaunchB);
        }

        public static bool IsSupportedLaunch(Precept_Ritual? ritual)
        {
            return ritual != null && IsSupportedLaunchDef(ritual.def)
                && ritual.behavior is RitualBehaviorWorker_GravshipLaunch;
        }

        // 解析只读取已有意识形态，不为驾驶员初始化 Tracker 或修改文化归属。
        public static Precept_Ritual? ResolveForPilot(Pawn? pawn, PreceptDef? launchDef)
        {
            if (pawn == null || launchDef == null)
                return null;
            if (pawn.Ideo != null)
                return pawn.Ideo.GetPrecept(launchDef) as Precept_Ritual;
            if (!IsSupportedLaunchDef(launchDef)
                || !CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
                return null;
            return Faction.OfPlayer?.ideos?.PrimaryIdeo?.GetPrecept(launchDef) as Precept_Ritual;
        }
    }
}
