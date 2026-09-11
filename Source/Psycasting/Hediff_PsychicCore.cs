using RimWorld;
using System.Text;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class Hediff_PsychicCore : Hediff_Level
    {
        private const int PsyfocusRecoverySettlementTicks = 60;
        private const int MaxPendingPsyfocusRecoveryTicks = PsyfocusRecoverySettlementTicks;

        private int pendingPsyfocusRecoveryTicks;

        public override void PostAdd(DamageInfo? dinfo)
        {
            if (pawn?.RaceProps.IsMechanoid == true)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
            }

            base.PostAdd(dinfo);
            Severity = level;

            ClearExistingDisruptorFlash();
            MechanoidMechanitorPsycastUtility.SyncPsychicReceiver(pawn);
            if (pawn?.RaceProps.IsMechanoid == true)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
            }

            PsychicCoreUtility.TryGainPsylinkLevel(pawn);
        }

        public override void ChangeLevel(int levelOffset)
        {
            int oldLevel = level;
            base.ChangeLevel(levelOffset);
            Severity = level;

            int gainedLevels = level - oldLevel;
            for (int i = 0; i < gainedLevels; i++)
            {
                PsychicCoreUtility.TryGainPsylinkLevel(pawn);
            }

            MechanoidMechanitorPsycastUtility.SyncPsychicReceiver(pawn);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);

            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead)
            {
                return;
            }

            TryRecoverMentalStateAtMaxLevel(delta);

            if (delta > 0)
            {
                pendingPsyfocusRecoveryTicks += delta;
            }

            // 累计不足60 Tick 时不执行恢复计算，避免每 Tick 重复查询。
            if (pendingPsyfocusRecoveryTicks < PsyfocusRecoverySettlementTicks)
            {
                return;
            }

            // 结算前重新校验：第三方状态结束回调可能改变角色状态与植入体状态。
            if (pawn.Destroyed
                || pawn.Dead
                || pawn.health?.hediffSet == null
                || !pawn.health.hediffSet.hediffs.Contains(this)
                || pawn.psychicEntropy == null
                || !pawn.HasPsylink)
            {
                // 不满足恢复条件时丢弃本轮累计时间，禁止补发离线期间的恢复。
                pendingPsyfocusRecoveryTicks = 0;
                return;
            }

            float totalPerHour =
                PsychicCoreUtility.GetTotalPsyfocusRecoveryPerHour(pawn, level);
            if (totalPerHour <= 0f)
            {
                pendingPsyfocusRecoveryTicks = 0;
                return;
            }

            int elapsedTicks = pendingPsyfocusRecoveryTicks;
            pendingPsyfocusRecoveryTicks = 0;
            float offset =
                totalPerHour * elapsedTicks / GenDate.TicksPerHour;
            pawn.psychicEntropy.OffsetPsyfocusDirectly(offset);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref pendingPsyfocusRecoveryTicks,
                "pendingPsyfocusRecoveryTicks",
                0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pendingPsyfocusRecoveryTicks < 0)
                {
                    pendingPsyfocusRecoveryTicks = 0;
                }
                else if (pendingPsyfocusRecoveryTicks > MaxPendingPsyfocusRecoveryTicks)
                {
                    pendingPsyfocusRecoveryTicks = MaxPendingPsyfocusRecoveryTicks;
                }
            }
        }

        private void TryRecoverMentalStateAtMaxLevel(int delta)
        {
            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead)
            {
                return;
            }

            if (MAPMechanitorMod.Settings == null
                || !MAPMechanitorMod.Settings
                    .enableMaxLevelPsychicCoreMentalStateRecovery)
            {
                return;
            }

            if (level < def.maxSeverity)
            {
                return;
            }

            if (!pawn.IsHashIntervalTick(600, delta))
            {
                return;
            }

            MentalState? mentalState =
                pawn.mindState?.mentalStateHandler?.CurState;
            if (mentalState == null)
            {
                return;
            }

            mentalState.RecoverFromState();
        }

        public override string TipStringExtra
        {
            get
            {
                StringBuilder stringBuilder = new StringBuilder();
                string baseTip = base.TipStringExtra;
                if (!baseTip.NullOrEmpty())
                {
                    stringBuilder.Append(baseTip);
                    stringBuilder.AppendLine();
                }

                float basePerHour =
                    PsychicCoreUtility.GetPassivePsyfocusRecoveryPerHour(level);
                if (basePerHour > 0f)
                {
                    stringBuilder.AppendLine(
                        " - 精神力自动恢复："
                        + PsychicCoreUtility.FormatPsyfocusPercent(basePerHour)
                        + "/小时");
                }

                if (CurStage is { blocksMentalBreaks: true })
                {
                    stringBuilder.AppendLine(" - 不再陷入精神崩溃");
                }

                return stringBuilder.ToString().TrimEnd('\n', '\r');
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            ClearPsychicActivation();
            MechanoidMechanitorPsycastUtility.SyncPsychicReceiver(pawn);
        }

        private void ClearExistingDisruptorFlash()
        {
            if (pawn?.health?.hediffSet == null || HediffDefOf.DisruptorFlash == null)
            {
                return;
            }

            Hediff? existing =
                pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DisruptorFlash);
            while (existing != null)
            {
                pawn.health.RemoveHediff(existing);
                existing =
                    pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DisruptorFlash);
            }
        }

        private void ClearPsychicActivation()
        {
            HediffDef? activationDef = PsychicCoreUtility.PsychicActivationHediffDef;
            if (pawn?.health?.hediffSet == null || activationDef == null)
            {
                return;
            }

            Hediff? active = pawn.health.hediffSet.GetFirstHediffOfDef(activationDef);
            if (active != null)
            {
                pawn.health.RemoveHediff(active);
            }
        }
    }
}
