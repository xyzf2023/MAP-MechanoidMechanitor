using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class JobDriver_EnergyPulse : JobDriver
    {
        private int chargeTicks;
        private int chargeDuration;
        private bool released;
        private bool recovering;
        private int recoveryDuration;
        private bool poseInitialized;
        private SunArmorPose startPose;
        private IntVec3 chargePosition = IntVec3.Invalid;
        private Map? chargeMap;
        private CompEnergyPulse? Pulse => pawn.GetComp<CompEnergyPulse>();
        public bool Released => released;
        public float ChargeProgress => chargeDuration > 0 ? Mathf.Clamp01((float)chargeTicks / chargeDuration) : 0f;
        internal bool HasAnimation => chargeDuration > 0;
        internal SunArmorPose AnimationPose => released
            ? SunSkillAnimation.PulseRecovery(recovering
                    ? SunSkillCooldown.Elapsed(pawn, job, recoveryDuration) : 0,
                recovering ? recoveryDuration : Pulse?.Props.recoveryTicks ?? 0, SunSkillAnimation.Rest(pawn))
            : SunSkillAnimation.PulseCharge(poseInitialized ? startPose : SunSkillAnimation.Rest(pawn), ChargeProgress);
        public override bool PlayerInterruptable => !released;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanContinueCharge());
            AddFinishAction(_ =>
            {
                if (HasAnimation) SunArmorPresentation.NotifySkillEnded(pawn, AnimationPose);
                SunSkillCooldown.Finish(pawn, job);
            });
            yield return Toils_General.StopDead();
            Toil charge = ToilMaker.MakeToil("EnergyPulseCharge");
            charge.defaultCompleteMode = ToilCompleteMode.Never;
            charge.handlingFacing = true;
            charge.initAction = () =>
            {
                pawn.Rotation = Rot4.South;
                startPose = SunArmorPresentation.CapturePose(pawn);
                poseInitialized = true;
                chargeDuration = Pulse!.Props.warmupTicks;
                chargePosition = pawn.Position;
                chargeMap = pawn.Map;
            };
            charge.tickAction = () =>
            {
                pawn.Rotation = Rot4.South;
                if (!CanContinueCharge())
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                if (++chargeTicks >= chargeDuration) ReadyForNextToil();
            };
            charge.WithProgressBar(TargetIndex.A, () => ChargeProgress);
            yield return charge;

            Toil release = ToilMaker.MakeToil("EnergyPulseRelease");
            release.defaultCompleteMode = ToilCompleteMode.Instant;
            release.handlingFacing = true;
            release.initAction = () =>
            {
                pawn.Rotation = Rot4.South;
                if (released || !CanContinueCharge()) return;
                released = true;
                Pulse!.ReleaseAt(pawn.Map, pawn.Position);
                Pulse.NotifyReleased();
            };
            yield return release;

            // 追加在原有三个步骤之后，保持旧存档的 Toil 编号。
            Toil recovery = ToilMaker.MakeToil("EnergyPulseRecovery");
            recovery.defaultCompleteMode = ToilCompleteMode.Never;
            recovery.handlingFacing = true;
            recovery.initAction = () =>
            {
                pawn.Rotation = Rot4.South;
                if (recovering) return;
                recoveryDuration = Mathf.Max(0, Pulse?.Props.recoveryTicks ?? 0);
                recovering = true;
                SunSkillCooldown.Begin(pawn, job, recoveryDuration);
                if (recoveryDuration == 0) ReadyForNextToil();
            };
            recovery.tickAction = () =>
            {
                pawn.Rotation = Rot4.South;
                if (SunSkillCooldown.Elapsed(pawn, job, recoveryDuration) >= recoveryDuration)
                    ReadyForNextToil();
            };
            yield return recovery;
        }

        private bool CanContinueCharge() => (released
                ? pawn.Spawned && !pawn.Dead && !pawn.Downed && !pawn.InMentalState
                    && pawn.stances?.stunner?.Stunned != true
                : Pulse?.CanOperate(pawn) == true)
            && (chargeDuration <= 0 || (pawn.Map == chargeMap && pawn.Position == chargePosition
                && pawn.pather?.Moving != true
                && (pawn.stances?.FullBodyBusy != true || SunSkillCooldown.Owns(pawn, job))));

        public override string GetReport() => recovering
            ? "MAP_SunSkill.PulseRecovery".Translate().Resolve() : base.GetReport();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref chargeTicks, "energyPulseChargeTicks");
            Scribe_Values.Look(ref chargeDuration, "energyPulseChargeDuration");
            Scribe_Values.Look(ref released, "energyPulseReleased");
            Scribe_Values.Look(ref recovering, "energyPulseRecovering");
            Scribe_Values.Look(ref recoveryDuration, "energyPulseRecoveryDuration");
            Scribe_Values.Look(ref poseInitialized, "energyPulsePoseInitialized");
            startPose.ExposeData("energyPulseStartPose");
            Scribe_Values.Look(ref chargePosition, "energyPulseChargePosition", IntVec3.Invalid);
            Scribe_References.Look(ref chargeMap, "energyPulseChargeMap");
            base.ExposeData();
        }
    }
}
