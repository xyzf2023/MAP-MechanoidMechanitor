using UnityEngine;
using Verse;
using RimWorld;

namespace MAP_MechanoidMechanitor
{
    public interface IMechanicalFlightEnergyProvider
    {
        bool TryGetEnergyFraction(Pawn pawn, out float fraction);
        bool TryConsumeMaximumEnergyFraction(Pawn pawn, float fraction);
        bool TrySetEnergyFraction(Pawn pawn, float fraction);
    }

    internal sealed class PawnMechEnergyProvider : IMechanicalFlightEnergyProvider
    {
        public bool TryGetEnergyFraction(Pawn pawn, out float fraction)
        {
            fraction = 0f;
            Need_MechEnergy? energy = pawn?.needs?.energy;
            if (energy == null)
            {
                return false;
            }

            fraction = energy.CurLevelPercentage;
            return true;
        }

        public bool TryConsumeMaximumEnergyFraction(Pawn pawn, float fraction)
        {
            Need_MechEnergy? energy = pawn?.needs?.energy;
            if (energy == null)
            {
                return false;
            }

            energy.CurLevelPercentage = Mathf.Max(0f,
                energy.CurLevelPercentage - Mathf.Max(0f, fraction)
                    * SunEnergyAuraUtility.ConsumptionFactor(pawn));
            return true;
        }

        public bool TrySetEnergyFraction(Pawn pawn, float fraction)
        {
            Need_MechEnergy? energy = pawn?.needs?.energy;
            if (energy == null)
            {
                return false;
            }

            energy.CurLevelPercentage = Mathf.Clamp01(fraction);
            return true;
        }
    }

    /// <summary>
    /// 飞行能源的统一入口。普通机械族读取自身 Need_MechEnergy；
    /// 正在合体的人类读取权威合体记录；调用者不再各自判断是否合体。
    /// </summary>
    public static class MechanicalFlightEnergyUtility
    {
        private static readonly IMechanicalFlightEnergyProvider DefaultProvider =
            new PawnMechEnergyProvider();

        public static bool TryGetEnergyFraction(Pawn? pawn, out float fraction)
        {
            fraction = 0f;
            return pawn != null
                && GetProvider(pawn).TryGetEnergyFraction(pawn, out fraction);
        }

        /// <summary>
        /// 起飞与持续飞行共用的耗能结算：基础比例乘重量级倍率及先天飞行 Comp 倍率，
        /// 不叠加 MechEnergyUsageFactor，也不改变能源阈值和其它系统的扣能。
        /// </summary>
        public static bool TryConsumeFlightEnergy(
            Pawn? pawn,
            MechanicalFlightProfileDef profile)
        {
            if (GroupFlightUtility.IsManaged(pawn) && !GroupFlightUtility.IsProviding(pawn))
                return false;
            return TryConsumeMaximumEnergyFraction(pawn,
                GetFlightEnergyDrainFraction(pawn, profile));
        }

        private static float GetFlightEnergyDrainFraction(
            Pawn? pawn,
            MechanicalFlightProfileDef profile)
        {
            return profile.energyDrainFraction * (GetFlightEnergyMultiplier(pawn, profile)
                + 0.5f * GroupFlightUtility.PassengerCount(pawn));
        }

        /// <summary>按实际扣能倍率和间隔估算每秒消耗的最大能量比例，仅用于显示。</summary>
        internal static float GetFlightEnergyFractionPerSecond(Pawn? pawn)
        {
            if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record?.Profile == null
                || (GroupFlightUtility.IsManaged(pawn) && !GroupFlightUtility.IsProviding(pawn)))
            {
                return 0f;
            }

            MechanicalFlightProfileDef profile = record.Profile;
            return GetFinalConsumptionFraction(pawn, GetFlightEnergyDrainFraction(pawn, profile))
                * 60f / Mathf.Max(1, profile.energyDrainIntervalTicks);
        }

        private static float GetFlightEnergyMultiplier(
            Pawn? pawn,
            MechanicalFlightProfileDef profile)
        {
            // 与能源提供器使用相同的有效合体判定；重量级和先天飞行倍率均读取源机械族。
            Pawn? source = MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                pawn, out MechFusionSession? session) ? session?.SourcePawn : pawn;
            float innateMultiplier = source?.GetComp<CompMechanicalFlightInnate>()
                ?.Props.energyDrainMultiplier ?? 1f;
            return innateMultiplier * GetWeightClassEnergyMultiplier(
                source?.RaceProps?.mechWeightClass, profile);
        }

        private static float GetWeightClassEnergyMultiplier(
            MechWeightClassDef? weightClass,
            MechanicalFlightProfileDef profile)
        {
            if (weightClass == null)
            {
                return 1f;
            }

            if (weightClass == MechWeightClassDefOf.Light)
            {
                return profile.lightEnergyDrainMultiplier;
            }
            if (weightClass == MechWeightClassDefOf.Medium)
            {
                return profile.mediumEnergyDrainMultiplier;
            }
            if (weightClass == MechWeightClassDefOf.Heavy)
            {
                return profile.heavyEnergyDrainMultiplier;
            }
            if (weightClass == MechWeightClassDefOf.UltraHeavy)
            {
                return profile.ultraHeavyEnergyDrainMultiplier;
            }

            return 1f;
        }

        public static bool TryConsumeMaximumEnergyFraction(Pawn? pawn, float fraction)
        {
            return pawn != null
                && GetProvider(pawn).TryConsumeMaximumEnergyFraction(pawn, fraction);
        }

        /// <summary>仅预估实际费用供支付检查使用；扣款仍传入原始费用，避免重复减耗。</summary>
        public static float GetFinalConsumptionFraction(Pawn? pawn, float fraction)
        {
            return Mathf.Max(0f, fraction)
                * SunEnergyAuraUtility.ConsumptionFactor(pawn)
                * (MechFusionEnergyUtility.TryGetActiveSessionForWearer(pawn, out MechFusionSession? session)
                    && session != null ? MechFusionVoidEngineUtility.ConsumptionFactor(session) : 1f);
        }

        public static bool TrySetEnergyFraction(Pawn? pawn, float fraction)
        {
            return pawn != null
                && GetProvider(pawn).TrySetEnergyFraction(pawn, fraction);
        }

        private static IMechanicalFlightEnergyProvider GetProvider(Pawn pawn)
        {
            return MechFusionEnergyUtility.IsActiveFusionWearer(pawn)
                ? MechFusionEnergyUtility.FusionFlightEnergyProvider
                : DefaultProvider;
        }
    }
}
