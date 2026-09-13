using UnityEngine;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体能源的唯一读写入口。能源只在权威合体记录中保存：
    /// 飞行与地面消耗扣同一份能源，服装与源 Pawn 的 Need 都不作为权威来源。
    /// </summary>
    public static class MechFusionEnergyUtility
    {
        internal const int GroundSettlementIntervalTicks = 60;
        internal const float GroundFractionPerDay = 0.20f;
        private const float TicksPerDay = 60000f;

        internal static readonly MechFusionFlightEnergyProvider
            FusionFlightEnergyProvider = new MechFusionFlightEnergyProvider();

        internal static void CaptureInitialEnergy(
            MechFusionSession session,
            Pawn sourcePawn)
        {
            Need_MechEnergy? energy = sourcePawn?.needs?.energy;
            float fallbackMax = sourcePawn?.RaceProps?.maxMechEnergy ?? 100f;
            float max = energy?.MaxLevel ?? fallbackMax;
            float current = energy?.CurLevel ?? max;
            session.SetEnergy(current, max);
        }

        internal static void WriteBackToSource(
            MechFusionSession session,
            Pawn? sourcePawn)
        {
            Need_MechEnergy? energy = sourcePawn?.needs?.energy;
            if (energy == null || session.MaxEnergy <= 0f)
            {
                return;
            }

            energy.CurLevel = session.CurrentEnergy;
        }

        internal static bool TryGetActiveSessionForWearer(
            Pawn? pawn,
            out MechFusionSession? session)
        {
            session = null;
            return GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out session)
                && session != null
                && session.IsActive;
        }

        internal static bool IsActiveFusionWearer(Pawn? pawn)
        {
            return TryGetActiveSessionForWearer(pawn, out _);
        }

        /// <summary>
        /// 合体地面基础消耗：20%/游戏日 × 源机械族 MechEnergyUsageFactor，
        /// 每 60 Tick 按实际经过 Tick 结算一次。
        /// </summary>
        internal static void TickSession(MechFusionSession session)
        {
            if (session == null || !session.IsActive)
            {
                return;
            }

            session.EnergyTickAccumulator += 1;
            if (session.EnergyTickAccumulator < GroundSettlementIntervalTicks)
            {
                return;
            }

            int elapsedTicks = session.EnergyTickAccumulator;
            session.EnergyTickAccumulator = 0;
            if (session.MaxEnergy <= 0f)
            {
                return;
            }

            float fractionPerTick = GroundFractionPerDay / TicksPerDay
                * GetMechEnergyUsageFactor(session);
            session.ConsumeEnergy(
                fractionPerTick * elapsedTicks * session.MaxEnergy);

            if (session.CurrentEnergy <= 0f)
            {
                MechFusionTeardownService.TryTeardown(
                    session,
                    MechFusionExitReason.EnergyDepleted,
                    force: false);
            }
        }

        internal static float GetMechEnergyUsageFactor(MechFusionSession session)
        {
            Pawn? source = session?.SourcePawn;
            StatDef? stat =
                DefDatabase<StatDef>.GetNamedSilentFail("MechEnergyUsageFactor");
            if (source == null || stat == null)
            {
                return 1f;
            }

            float value = source.GetStatValue(stat);
            if (float.IsNaN(value))
            {
                return 1f;
            }

            return Mathf.Max(0f, value);
        }

        internal static float GetGroundConsumptionPercentPerDay(
            MechFusionSession session)
        {
            return GroundFractionPerDay
                * 100f
                * GetMechEnergyUsageFactor(session);
        }
    }

    /// <summary>
    /// 合体人类的飞行能源提供器：读写权威合体记录，
    /// 不在服装 Comp 与记录之间双向覆盖。
    /// </summary>
    internal sealed class MechFusionFlightEnergyProvider
        : IMechanicalFlightEnergyProvider
    {
        public bool TryGetEnergyFraction(Pawn pawn, out float fraction)
        {
            fraction = 0f;
            if (!MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || session.MaxEnergy <= 0f)
            {
                return false;
            }

            fraction = Mathf.Clamp01(session.CurrentEnergy / session.MaxEnergy);
            return true;
        }

        public bool TryConsumeMaximumEnergyFraction(Pawn pawn, float fraction)
        {
            if (!MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null)
            {
                return false;
            }

            session.ConsumeEnergy(
                session.MaxEnergy * Mathf.Max(0f, fraction));
            return true;
        }

        public bool TrySetEnergyFraction(Pawn pawn, float fraction)
        {
            if (!MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null)
            {
                return false;
            }

            session.SetEnergy(
                session.MaxEnergy * Mathf.Clamp01(fraction),
                session.MaxEnergy);
            return true;
        }
    }
}
