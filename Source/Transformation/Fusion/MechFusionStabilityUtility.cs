using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 结构稳定值的唯一结算入口。数值只保存在权威合体记录中；
    /// 伤害由 AddHediff 路由提供已经过全部护甲的最终伤害，
    /// 解除合体时通过通用部位耐久工具结算源机械族伤势。
    /// </summary>
    internal static class MechFusionStabilityUtility
    {
        internal const float BaseStability = 500f;

        internal const float PassiveRepairFractionPerHour = 0.02f;
        internal const float ProtocolRepairFractionPerHour = 0.10f;

        internal static void TickRepair(MechFusionSession session)
        {
            if (session.State != MechFusionSessionState.Active
                || session.CurrentStability <= 0f || session.MaxStability <= 0f
                || session.WearerPawn == null || session.WearerPawn.Dead
                || session.WearerPawn.Destroyed) return;

            bool rapid = MechFusionHealthEffectManager.HasActiveTimedEffect(
                session, MechFusionRepairBeaconUtility.StructuralRepairRuleId);
            float fraction = rapid ? ProtocolRepairFractionPerHour
                : session.RepairBeaconAuthorized ? PassiveRepairFractionPerHour : 0f;

            // 每 tick 按当时档位累计，每 60 tick 结算，避免大稳定值下的浮点小量损失。
            // 满值不蓄积修复额度；两种速度互斥。
            if (session.CurrentStability >= session.MaxStability)
            {
                session.PendingStabilityRepair = 0f;
                session.StabilityRepairTicks = 0;
                return;
            }
            session.PendingStabilityRepair += session.MaxStability * fraction / 2500f;
            if (++session.StabilityRepairTicks < 60) return;
            session.SetStability(
                session.CurrentStability + session.PendingStabilityRepair,
                session.MaxStability);
            session.PendingStabilityRepair = 0f;
            session.StabilityRepairTicks = 0;
        }

        internal static void CaptureInitialStability(
            MechFusionSession session,
            Pawn source)
        {
            float max = BaseStability
                * GetWeightMultiplier(source)
                * (source?.RaceProps?.baseHealthScale ?? 1f);
            session.SetStability(max, max);
        }

        internal static float GetWeightMultiplier(Pawn? source)
        {
            MechWeightClassDef? weightClass = source?.RaceProps?.mechWeightClass;
            if (weightClass == null)
            {
                return 1f;
            }

            if (weightClass == MechWeightClassDefOf.Light)
            {
                return 0.5f;
            }

            if (weightClass == MechWeightClassDefOf.Medium)
            {
                return 1f;
            }

            if (weightClass == MechWeightClassDefOf.Heavy)
            {
                return 2f;
            }

            if (weightClass == MechWeightClassDefOf.UltraHeavy)
            {
                return 4f;
            }

            return 1f;
        }

        internal static void OnDamageContextClosed(
            MechFusionDamageContext? context)
        {
            if (context?.Session == null)
            {
                return;
            }

            MechFusionSession session = context.Session;
            MechFusionExitReason? reason = context.PendingExitReason;
            if (session.CurrentStability <= 0f
                && (reason == null
                    || MechFusionExitReason.StabilityDepleted.GetPriority()
                        < reason.Value.GetPriority()))
            {
                reason = MechFusionExitReason.StabilityDepleted;
            }

            if (reason == null)
            {
                return;
            }

            MechFusionTeardownService.TryTeardown(
                session,
                reason.Value,
                force: false);
        }

        /// <summary>
        /// 解除合体时按剩余稳定值比例计算源机械族的结构总损失，再由通用
        /// 部位耐久工具把损失分配为受控的局部伤势。安全容器检查、
        /// 结算顺序和死亡处理与建筑形态共用同一实现。
        /// </summary>
        internal static void SettleSourcePartDurability(
            MechFusionSession session,
            Pawn? source)
        {
            if (session == null || source == null || session.StabilitySettled)
            {
                return;
            }

            float ratio = session.MaxStability > 0f
                ? Mathf.Clamp01(session.CurrentStability / session.MaxStability)
                : 0f;
            int settlementSeed =
                MechPartDurabilityUtility.CreateSettlementSeed(
                    source,
                    session.SessionId);
            MechPartDurabilityUtility.SettleCurrentPartDurability(
                source,
                ratio,
                settlementSeed);
        }
    }
}
