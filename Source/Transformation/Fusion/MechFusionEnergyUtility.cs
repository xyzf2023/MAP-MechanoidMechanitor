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

        /// <summary>
        /// 将权威合体记录中的最终能源提交给已经恢复完成的真实源 Pawn。
        /// 如果生命周期整理暂时移除了 MechEnergy Need，会先按原版规则重新整理
        /// Needs，再写入最终值。只有实际存在可写入的能源 Need 时才返回 true；
        /// 调用方只能在返回 true 后标记 EnergyWrittenBack。
        /// </summary>
        internal static bool TryWriteBackToSource(
            MechFusionSession session,
            Pawn? sourcePawn)
        {
            if (session == null
                || sourcePawn == null
                || sourcePawn.Destroyed
                || sourcePawn.Discarded)
            {
                return false;
            }

            // 旧存档若没有有效的合体能源载荷，不阻塞整个解除事务。
            if (session.MaxEnergy <= 0f)
            {
                return true;
            }

            Pawn_NeedsTracker? needs = sourcePawn.needs;
            if (needs == null)
            {
                return false;
            }

            Need_MechEnergy? energy = needs.energy;
            if (energy == null)
            {
                // SpawnSetup、阵营/监管恢复等生命周期步骤可能重新整理 Needs。
                // 在最终提交点再按原版规则校正一次，避免新建 Need 停留在默认 50%。
                needs.AddOrRemoveNeedsAsAppropriate();
                energy = needs.energy;
            }

            if (energy == null)
            {
                return false;
            }

            energy.CurLevel = session.CurrentEnergy;
            return true;
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
        /// 飞行系统在准备进入普通机械族的低能量迫降前调用。
        /// 合体能源已经归零时，立即交给合体解除事务处理；返回 true 后，
        /// 飞行调用方必须停止本轮普通迫降逻辑，避免抢先安全着陆。
        /// </summary>
        internal static bool TryHandleDepletedFlightEnergy(Pawn? pawn)
        {
            if (!TryGetActiveSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || session.CurrentEnergy > 0f)
            {
                return false;
            }

            MechFusionTeardownService.TryTeardown(
                session,
                MechFusionExitReason.EnergyDepleted,
                force: false);
            return true;
        }

        /// <summary>
        /// 合体地面基础消耗：20%/游戏日 × 排除虚空供能状态后的源机械族耗能系数。
        /// 每 60 Tick 按实际经过 Tick 结算；机体同调的最终倍率由会话统一施加。
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
            // 日冕跟随实际穿戴者所在地图，不能读取合体前留在源 Pawn 上的效果。
            if (SunEnergyAuraUtility.Level(session?.WearerPawn) != 0) return 0f;
            Pawn? source = session?.SourcePawn;
            StatDef? stat =
                DefDatabase<StatDef>.GetNamedSilentFail("MechEnergyUsageFactor");
            if (source == null || stat == null)
            {
                return 1f;
            }

            float value = MechFusionVoidEngineUtility.GetUsageFactor(source, stat);
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
                * GetMechEnergyUsageFactor(session)
                * MechFusionVoidEngineUtility.ConsumptionFactor(session)
                * SunEnergyAuraUtility.ConsumptionFactor(session.WearerPawn);
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
