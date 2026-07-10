using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class DataProcessingAllocationUtility
    {
        public const float StepPercent = 0.05f;
        public const float MinReservedConsciousness = 0.50f;
        public const int MinReservedConsciousnessPercent = 50;
        public const int MinConsciousnessPercentToAddStep = 55;

        /// <summary>5%：脱离指挥范围。</summary>
        public const int CommandRangeThresholdSteps = 1;

        /// <summary>15%：完整虚拟旅行节点。</summary>
        public const int TravelNodeThresholdSteps = 3;

        /// <summary>特殊数值效果最多按 100%（20 档）计算；意识加成不受此限制。</summary>
        public const int MaxSpecialEffectSteps = 20;

        public const float WorkSpeedOffsetPerStep = 0.15f;
        public const float RangedFactorReductionPerTenPercentTier = 0.05f;
        public const float MoveSpeedOffsetPerTenPercentTier = 0.5f;
        public const float IncomingDamageReductionPerTenPercentTier = 0.05f;
        public const int MoveSpeedMinEffectSteps = 4;
        public const int IncomingDamageMinEffectSteps = 6;

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

        public static int GetSpecialEffectSteps(int steps)
        {
            return Mathf.Clamp(steps, 0, MaxSpecialEffectSteps);
        }

        public static float GetWorkSpeedOffset(int steps)
        {
            return GetSpecialEffectSteps(steps) * WorkSpeedOffsetPerStep;
        }

        public static float GetRangedActionFactor(int steps)
        {
            int effectSteps = GetSpecialEffectSteps(steps);
            return 1f - effectSteps / 2 * RangedFactorReductionPerTenPercentTier;
        }

        public static float GetMoveSpeedOffset(int steps)
        {
            int effectSteps = GetSpecialEffectSteps(steps);
            return effectSteps >= MoveSpeedMinEffectSteps
                ? effectSteps / 2 * MoveSpeedOffsetPerTenPercentTier
                : 0f;
        }

        public static float GetIncomingDamageFactor(int steps)
        {
            int effectSteps = GetSpecialEffectSteps(steps);
            return effectSteps >= IncomingDamageMinEffectSteps
                ? 1f - effectSteps / 2 * IncomingDamageReductionPerTenPercentTier
                : 1f;
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

        public static int GetCurrentConsciousnessPercent(Pawn? pawn)
        {
            return Mathf.RoundToInt(GetCurrentConsciousness(pawn) * 100f);
        }

        public static int GetAdditionalAssignableSteps(Pawn? overseer)
        {
            int availablePercent =
                GetCurrentConsciousnessPercent(overseer) - MinReservedConsciousnessPercent;
            return availablePercent >= 5 ? availablePercent / 5 : 0;
        }

        public static bool CanAddStep(Pawn? overseer)
        {
            return GetCurrentConsciousnessPercent(overseer)
                >= MinConsciousnessPercentToAddStep;
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

        public static bool HasVirtualTravelNode(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            return registry != null
                && registry.HasAtLeast(pawn, TravelNodeThresholdSteps);
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
