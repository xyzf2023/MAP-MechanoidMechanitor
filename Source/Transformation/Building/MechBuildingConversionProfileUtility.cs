using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械体建筑形态配置的唯一查询入口。
    /// 只有 Pawn ThingDef 上的 CompMechBuildingConversion 可以提供配置与资格。
    /// </summary>
    public static class MechBuildingConversionProfileUtility
    {
        public static bool TryGetProfile(
            Pawn? pawn,
            out CompProperties_MechBuildingConversion? profile)
        {
            profile = null;
            if (pawn == null || pawn.Destroyed || pawn.Discarded)
            {
                return false;
            }

            CompMechBuildingConversion? comp =
                pawn.GetComp<CompMechBuildingConversion>();
            profile = comp?.Props;
            return profile?.buildingFormDef != null;
        }

        public static bool IsBuildingFormDefValid(
            ThingDef? buildingDef,
            out string? failureReason)
        {
            failureReason = null;
            if (buildingDef == null)
            {
                failureReason = "未配置建筑形态 Def。";
                return false;
            }

            if (buildingDef.category != ThingCategory.Building
                || !buildingDef.useHitPoints)
            {
                failureReason = "建筑形态 Def 不是可受伤的建筑。";
                return false;
            }

            if (!HasComp(buildingDef, typeof(CompMechFormCarrier))
                || !HasComp(buildingDef, typeof(CompMechBuildingForm)))
            {
                failureReason = "建筑形态 Def 缺少形态载体或建筑形态组件。";
                return false;
            }

            return true;
        }

        private static bool HasComp(ThingDef thingDef, System.Type compClass)
        {
            List<CompProperties>? comps = thingDef.comps;
            if (comps == null)
            {
                return false;
            }

            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i]?.compClass == compClass)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
