using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class ProductivityCoreUtility
    {
        public const int MaxLevel = 5;
        public const float DefaultWorkSpeedOffsetPercentPerLevel = 200f;
        public const float MinWorkSpeedOffsetPercentPerLevel = 0f;
        public const float MaxWorkSpeedOffsetPercentPerLevel = 10000f;

        public static int GetLevel(Pawn? pawn)
        {
            Hediff? hediff = pawn?.health?.hediffSet?.GetFirstHediffOfDef(
                MAPMechanitor_HediffDefOf.MAP_ProductivityCore);
            if (hediff is Hediff_Level levelHediff)
            {
                return Mathf.Clamp(levelHediff.level, 0, MaxLevel);
            }

            return hediff == null
                ? 0
                : Mathf.Clamp(Mathf.FloorToInt(hediff.Severity), 0, MaxLevel);
        }

        public static int GetEffectiveLevelForWorker(Pawn? worker)
        {
            if (worker == null || !worker.RaceProps.IsMechanoid)
            {
                return 0;
            }

            int level = 0;
            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(worker))
            {
                level += GetLevel(worker);
            }

            Pawn? overseer = worker.GetOverseer();
            if (overseer != null && overseer != worker)
            {
                level += GetLevel(overseer);
            }

            return level;
        }

        public static bool HasActiveEffect(Pawn? pawn)
        {
            return ImplantEffectUtility.HasHediff(
                pawn,
                MAPMechanitor_HediffDefOf.MAP_ProductivityCoreActive);
        }

        public static float WorkSpeedOffsetPerLevel
        {
            get
            {
                float percent =
                    MAPMechanitorMod.Settings?.productivityCoreWorkSpeedOffsetPercentPerLevel
                    ?? DefaultWorkSpeedOffsetPercentPerLevel;
                return Mathf.Clamp(
                    percent,
                    MinWorkSpeedOffsetPercentPerLevel,
                    MaxWorkSpeedOffsetPercentPerLevel) / 100f;
            }
        }
    }
}
