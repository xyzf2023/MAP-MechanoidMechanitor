using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class DataProcessingAllocationUtility
    {
        public const float StepPercent = 0.05f;
        public const float MinReservedConsciousness = 0.50f;
        public const float MinConsciousnessToAddStep = 0.55f;
        public const int CommandRangeThresholdSteps = 2;
        public const int CaravanLeadThresholdSteps = 4;

        private const string DataStreamDistributionDefName = "MAP_DataStreamDistribution";
        private const string CommandFocusDefName = "MAP_CommandFocus";

        private static HediffDef? dataStreamDistributionDef;
        private static HediffDef? commandFocusDef;

        public static HediffDef? DataStreamDistributionDef =>
            dataStreamDistributionDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(DataStreamDistributionDefName);

        public static HediffDef? CommandFocusDef =>
            commandFocusDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(CommandFocusDefName);

        public static float StepsToPercent(int steps)
        {
            return steps * StepPercent;
        }

        public static string FormatPercentDelta(int steps, bool positive)
        {
            int percent = steps * 5;
            return positive ? $"+{percent}%" : $"-{percent}%";
        }

        public static float GetCurrentConsciousness(Pawn? pawn)
        {
            if (pawn?.health?.capacities == null)
            {
                return 0f;
            }

            try
            {
                return pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            }
            catch
            {
                return 0f;
            }
        }

        public static int GetAdditionalAssignableSteps(Pawn? overseer)
        {
            float current = GetCurrentConsciousness(overseer);
            float available = current - MinReservedConsciousness;
            if (available < StepPercent)
            {
                return 0;
            }

            return Mathf.FloorToInt(available / StepPercent);
        }

        public static bool CanAddStep(Pawn? overseer)
        {
            return GetCurrentConsciousness(overseer) >= MinConsciousnessToAddStep;
        }

        public static bool HasCommandRangeBypass(Pawn? mech)
        {
            if (mech == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return false;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            return registry != null
                && registry.HasAtLeast(mech, CommandRangeThresholdSteps);
        }

        public static bool HasCaravanLeadQualification(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            return registry != null
                && registry.HasAtLeast(pawn, CaravanLeadThresholdSteps);
        }

        public static bool IsValidAllocationPair(Pawn? overseer, Pawn? target)
        {
            if (overseer == null
                || target == null
                || overseer.Dead
                || target.Dead
                || overseer.Destroyed
                || target.Destroyed)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(overseer)
                || overseer.mechanitor == null
                || !target.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (target.GetOverseer() != overseer)
            {
                return false;
            }

            if (overseer.Faction == null
                || !overseer.Faction.IsPlayerSafe()
                || target.Faction == null
                || !target.Faction.IsPlayerSafe())
            {
                return false;
            }

            return true;
        }
    }
}
