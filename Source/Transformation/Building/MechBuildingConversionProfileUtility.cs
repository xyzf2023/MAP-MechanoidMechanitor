using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 按固定优先级寻找建筑形态配置：Pawn ThingDef、PawnKindDef、HediffDef。
    /// 这样能力来源和形态配置可以分别添加，也能让植入体提供独立建筑形态。
    /// </summary>
    public static class MechBuildingConversionProfileUtility
    {
        public static bool TryGetProfile(
            Pawn? pawn,
            out DefModExtension_MechBuildingConversion? profile)
        {
            profile = null;
            if (pawn == null || pawn.Destroyed || pawn.Discarded)
            {
                return false;
            }

            profile = pawn.def?.GetModExtension<DefModExtension_MechBuildingConversion>();
            if (IsUsable(profile))
            {
                return true;
            }

            profile = pawn.kindDef?.GetModExtension<DefModExtension_MechBuildingConversion>();
            if (IsUsable(profile))
            {
                return true;
            }

            List<Hediff>? hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                profile = null;
                return false;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                profile = hediffs[i]?.def?
                    .GetModExtension<DefModExtension_MechBuildingConversion>();
                if (IsUsable(profile))
                {
                    return true;
                }
            }

            profile = null;
            return false;
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

        private static bool IsUsable(
            DefModExtension_MechBuildingConversion? profile)
        {
            return profile?.buildingFormDef != null;
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
