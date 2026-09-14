using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 战车建筑形态配置的唯一查询入口。
    /// 只有 Pawn ThingDef 上的 CompChariotBuildingConversion 可以提供配置与资格。
    /// </summary>
    public static class MechBuildingConversionProfileUtility
    {
        public static bool TryGetProfile(
            Pawn? pawn,
            out CompProperties_ChariotBuildingConversion? profile)
        {
            profile = null;
            if (pawn == null || pawn.Destroyed || pawn.Discarded)
            {
                return false;
            }

            CompChariotBuildingConversion? comp =
                pawn.GetComp<CompChariotBuildingConversion>();
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
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.MissingProfile".Translate();
                return false;
            }

            if (buildingDef.category != ThingCategory.Building)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.InvalidDef".Translate();
                return false;
            }

            if (!HasComp(buildingDef, typeof(CompMechFormCarrier))
                || !HasComp(buildingDef, typeof(CompMechBuildingForm)))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.MissingCarrierComp".Translate();
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
