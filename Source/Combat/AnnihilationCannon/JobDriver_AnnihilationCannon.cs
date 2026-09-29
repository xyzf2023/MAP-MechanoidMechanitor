using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    public sealed class JobDriver_AnnihilationCannon : JobDriver
    {
        private int chargeTicks;
        private int chargeDuration;
        private bool launched;
        private bool sequenceInitialized;
        private bool openingStarted;
        private bool recoveryStarted;
        private int openingDuration;
        private int brakeDuration;
        private int alignDuration;
        private int closeDuration;
        private SunArmorPose openingPose;
        private IntVec3 chargePosition = IntVec3.Invalid;
        private Map? chargeMap;
        private Sustainer? aimingSound;
        private Mote_AnnihilationWarmup? aimMote;
        private Mote_AnnihilationWarmup? chargeMote;
        private Mote_AnnihilationWarmup? targetMote;
        private CompAnnihilationCannon? Cannon => pawn.GetComp<CompAnnihilationCannon>();
        internal float ChargeProgress => chargeDuration > 0 ? Mathf.Clamp01((float)chargeTicks / chargeDuration) : 0f;
        private bool IsOpening => openingStarted && chargeDuration <= 0 && !launched;
        private int RecoveryDuration => brakeDuration + alignDuration + closeDuration;
        internal bool HasAnimation => sequenceInitialized || chargeDuration > 0;
        internal float AnimationSpeed => launched
            ? CannonRecoverySpeed : (IsOpening ? 0f : SunSkillAnimation.CannonSpeed(ChargeProgress));
        private float CannonRecoverySpeed => recoveryStarted
            ? SunSkillAnimation.CannonMaxSpeed * (1f - SunSkillAnimation.Progress(
                SunSkillCooldown.Elapsed(pawn, job, RecoveryDuration), brakeDuration))
            : SunSkillAnimation.CannonMaxSpeed;
        internal SunArmorPose AnimationPose => launched
            ? SunSkillAnimation.CannonRecovery(recoveryStarted
                    ? SunSkillCooldown.Elapsed(pawn, job, RecoveryDuration) : 0,
                chargeDuration, brakeDuration, alignDuration, closeDuration, SunSkillAnimation.Rest(pawn))
            : IsOpening
                ? SunSkillAnimation.CannonOpening(openingPose,
                    SunSkillCooldown.Elapsed(pawn, job, openingDuration), openingDuration)
                : SunSkillAnimation.CannonCharge(chargeTicks, chargeDuration);

        // 固定展开、发射和后摇不能被普通指令跳过；正式蓄力保留原有取消方式。
        public override bool PlayerInterruptable => !IsOpening && !launched;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanContinue());
            AddFinishAction(_ =>
            {
                if (HasAnimation)
                    SunArmorPresentation.NotifySkillEnded(pawn, AnimationPose,
                        AnimationSpeed, AnimationSpeed * SunSkillAnimation.OuterSpeedFactor);
                FinishChargeVisuals();
                SunSkillCooldown.Finish(pawn, job);
            });

            // 保留旧存档的 0=停止、1=蓄力、2=发射编号；新阶段追加并显式跳转。
            Toil stop = Toils_General.StopDead();
            stop.handlingFacing = true;
            Toil opening = ToilMaker.MakeToil("AnnihilationOpening");
            Toil charge = ToilMaker.MakeToil("AnnihilationCharge");
            Toil launch = ToilMaker.MakeToil("AnnihilationLaunch");
            Toil recovery = ToilMaker.MakeToil("AnnihilationRecovery");
            stop.initAction = () =>
            {
                pawn.pather.StopDead();
                pawn.Rotation = Rot4.South;
                InitializeSequence();
                JumpToToil(opening);
            };

            opening.defaultCompleteMode = ToilCompleteMode.Never;
            opening.handlingFacing = true;
            opening.initAction = () =>
            {
                pawn.Rotation = Rot4.South;
                InitializeSequence();
                // 旧档若保存于瞬时发射步骤之后，顺序推进也必须进入后摇。
                if (launched)
                {
                    JumpToToil(recovery);
                    return;
                }
                openingStarted = true;
                SunSkillCooldown.Begin(pawn, job, openingDuration);
            };
            opening.tickAction = () =>
            {
                pawn.Rotation = Rot4.South;
                if (SunSkillCooldown.Elapsed(pawn, job, openingDuration) >= openingDuration)
                    JumpToToil(charge);
            };

            charge.defaultCompleteMode = ToilCompleteMode.Never;
            charge.handlingFacing = true;
            charge.initAction = () =>
            {
                pawn.Rotation = Rot4.South;
                InitializeSequence();
                // 固定展开不消耗蓄力；仅在正式蓄力开始时采样瞄准系数。
                if (chargeDuration <= 0) chargeDuration = Cannon!.WarmupTicksFor(pawn);
                if (pawn.GetComp<CompSunBossState>() != null)
                    CurrentGameComponentCache<GameComponent_SunBossNotifications>.Get()
                        ?.NotifyCannonCharging(pawn);
                MaintainVisuals();
            };
            charge.tickAction = () =>
            {
                InitializeSequence(); // 旧存档正在蓄力时不会重跑 initAction。
                if (!CanContinue())
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                pawn.Rotation = Rot4.South;
                if (aimingSound == null || aimingSound.Ended)
                    aimingSound = DefDatabase<SoundDef>.GetNamedSilentFail("HellsphereCannon_Aiming")
                        ?.TrySpawnSustainer(SoundInfo.InMap(pawn, MaintenanceType.PerTick));
                aimingSound?.Maintain();
                MaintainVisuals();
                chargeTicks++;
                if (chargeTicks >= chargeDuration) ReadyForNextToil();
            };
            charge.WithProgressBar(TargetIndex.None, () => ChargeProgress);
            charge.AddFinishAction(FinishChargeVisuals);
            launch.defaultCompleteMode = ToilCompleteMode.Instant;
            launch.handlingFacing = true;
            launch.initAction = () =>
            {
                pawn.Rotation = Rot4.South;
                InitializeSequence();
                if (launched)
                {
                    JumpToToil(recovery);
                    return;
                }
                // Toil 切换到发射时再验证一次，受控/位移后不能补发已经取消的攻击。
                if (!CanContinue() || Cannon == null)
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                // 状态先提交，防止读档/Job 重入导致重复发射。
                launched = true;
                AnnihilationShot shot = (AnnihilationShot)ThingMaker.MakeThing(AnnihilationCannonDefOf.MAP_AnnihilationShot);
                shot.Initialize(pawn, job.targetA.Cell, Cannon.Props);
                GenSpawn.Spawn(shot, pawn.Position, pawn.Map);
                Cannon.NotifyLaunched();
                DefDatabase<SoundDef>.GetNamedSilentFail("Shot_HellsphereCannonGun")
                    ?.PlayOneShot(new TargetInfo(pawn));
                JumpToToil(recovery);
            };

            recovery.defaultCompleteMode = ToilCompleteMode.Never;
            recovery.handlingFacing = true;
            recovery.initAction = () =>
            {
                pawn.Rotation = Rot4.South;
                if (recoveryStarted) return;
                recoveryStarted = true;
                SunSkillCooldown.Begin(pawn, job, RecoveryDuration);
            };
            recovery.tickAction = () =>
            {
                pawn.Rotation = Rot4.South;
                if (SunSkillCooldown.Elapsed(pawn, job, RecoveryDuration) >= RecoveryDuration)
                    ReadyForNextToil();
            };
            yield return stop;
            yield return charge;
            yield return launch;
            yield return opening;
            yield return recovery;
        }

        private void InitializeSequence()
        {
            if (sequenceInitialized || Cannon == null) return;
            bool legacyCharge = chargeDuration > 0 || launched;
            openingPose = SunArmorPresentation.CapturePose(pawn);
            openingDuration = legacyCharge ? 0 : Mathf.Max(1, Cannon.Props.deployTicks);
            brakeDuration = Mathf.Max(1, Cannon.Props.brakeTicks);
            alignDuration = Mathf.Max(1, Cannon.Props.alignTicks);
            closeDuration = Mathf.Max(1, Cannon.Props.closeTicks);
            if (!chargePosition.IsValid)
            {
                chargePosition = pawn.Position;
                chargeMap = pawn.Map;
            }
            sequenceInitialized = true;
        }

        private bool CanContinue()
        {
            CompAnnihilationCannon? cannon = Cannon;
            if (!pawn.Spawned || pawn.Map == null || pawn.Dead || pawn.Downed
                || pawn.InMentalState || pawn.stances?.stunner?.Stunned == true) return false;
            // 炮弹已经独立运行；后摇不再依赖目标视线、发射器或征召状态。
            if (!launched && (cannon == null || !cannon.CanOperate(pawn)
                || !cannon.ValidTarget(pawn, job.targetA.Cell))) return false;
            if (chargeDuration > 0 && !chargePosition.IsValid)
            {
                // 旧存档缺少锚点时，等 Pawn 真正回到地图后再补齐，不能在 PostLoadInit 读取 Map。
                chargePosition = pawn.Position;
                chargeMap = pawn.Map;
            }
            // StopDead 执行前允许 Pawn 仍在移动；开始蓄力后锁定地图和所在格。
            return !chargePosition.IsValid || (pawn.Map == chargeMap && pawn.Position == chargePosition
                && pawn.pather?.Moving != true
                && (pawn.stances?.FullBodyBusy != true || SunSkillCooldown.Owns(pawn, job)));
        }

        private void FinishChargeVisuals()
        {
            aimingSound?.End();
            aimingSound = null;
            // 与原版一致，停止维护后自行淡出；发射时清理，不延长到后摇结束。
            aimMote = null;
            chargeMote = null;
            targetMote = null;
        }

        public override string GetReport() => IsOpening ? "MAP_Annihilation.Report.Opening".Translate().Resolve()
            : launched ? "MAP_Annihilation.Report.Recovery".Translate().Resolve() : base.GetReport();

        private void MaintainVisuals()
        {
            MaintainMote(ref aimMote, AnnihilationCannonDefOf.Mote_MAP_AnnihilationAim, 0);
            MaintainMote(ref chargeMote, AnnihilationCannonDefOf.Mote_MAP_AnnihilationCharge, 1);
            MaintainMote(ref targetMote, AnnihilationCannonDefOf.Mote_MAP_AnnihilationTarget, 2);
        }

        private void MaintainMote(ref Mote_AnnihilationWarmup? mote, ThingDef def, int part)
        {
            if (mote == null || mote.Destroyed)
            {
                mote = (Mote_AnnihilationWarmup)ThingMaker.MakeThing(def);
                mote.UpdateCharge(pawn, job.targetA.Cell, ChargeProgress, chargeTicks / 60f, part);
                GenSpawn.Spawn(mote, pawn.Position, pawn.Map);
                // Mote 不存档；读档后按已保存的蓄力进度恢复淡入年龄。
                mote.ForceSpawnTick(Find.TickManager.TicksGame - chargeTicks);
            }
            mote.UpdateCharge(pawn, job.targetA.Cell, ChargeProgress, chargeTicks / 60f, part);
            mote.Maintain();
        }

        public override void ExposeData()
        {
            // base 在 PostLoadInit 会重建 Toil；不在重建过程中重置已保存的进度。
            Scribe_Values.Look(ref chargeTicks, "annihilationChargeTicks");
            Scribe_Values.Look(ref chargeDuration, "annihilationChargeDuration");
            Scribe_Values.Look(ref launched, "annihilationLaunched");
            Scribe_Values.Look(ref sequenceInitialized, "annihilationSequenceInitialized");
            Scribe_Values.Look(ref openingStarted, "annihilationOpeningStarted");
            Scribe_Values.Look(ref recoveryStarted, "annihilationRecoveryStarted");
            Scribe_Values.Look(ref openingDuration, "annihilationOpeningDuration");
            Scribe_Values.Look(ref brakeDuration, "annihilationBrakeDuration");
            Scribe_Values.Look(ref alignDuration, "annihilationAlignDuration");
            Scribe_Values.Look(ref closeDuration, "annihilationCloseDuration");
            openingPose.ExposeData("annihilationOpeningPose");
            Scribe_Values.Look(ref chargePosition, "annihilationChargePosition", IntVec3.Invalid);
            Scribe_References.Look(ref chargeMap, "annihilationChargeMap");
            base.ExposeData();
        }
    }
}
