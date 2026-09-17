using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 将合体源机械体的心灵中枢等级映射给目标人类。
    /// Severity 直接表示合体瞬间冻结的等级；Def 层属性独立生效，
    /// C# 层效果与真实心灵中枢共享统一的“取较高等级、只结算一次”入口。
    /// </summary>
    public sealed class Hediff_PsychicSynchronization : HediffWithComps
    {
        private int pendingPsyfocusRecoveryTicks;

        public int MappedLevel => Mathf.Clamp(
            Mathf.RoundToInt(Severity),
            1,
            PsychicCoreUtility.MaxPsychicCoreLevel);

        public override string Label =>
            def.label + " (" + "LevelNum".Translate(MappedLevel).ToString() + ")";

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            Severity = MappedLevel;
            PsychicCoreUtility.ClearExistingDisruptorFlash(pawn);
            PsychicCoreUtility.SyncPsychicActivationAbility(pawn);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            PsychicCoreUtility.TickRuntimeEffects(
                this,
                ref pendingPsyfocusRecoveryTicks,
                delta);
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
                pendingPsyfocusRecoveryTicks =
                    pendingPsyfocusRecoveryTicks < 0
                        ? 0
                        : System.Math.Min(
                            pendingPsyfocusRecoveryTicks,
                            PsychicCoreUtility.PsyfocusRecoverySettlementTicks);
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            PsychicCoreUtility.SyncPsychicActivationAbility(pawn);
        }
    }
}
