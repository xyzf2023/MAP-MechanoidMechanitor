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
        private IntVec3 chargePosition = IntVec3.Invalid;
        private Map? chargeMap;
        private CompEnergyPulse? Pulse => pawn.GetComp<CompEnergyPulse>();
        public bool Released => released;
        public float ChargeProgress => chargeDuration > 0 ? Mathf.Clamp01((float)chargeTicks / chargeDuration) : 0f;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanContinueCharge());
            yield return Toils_General.StopDead();
            Toil charge = ToilMaker.MakeToil("EnergyPulseCharge");
            charge.defaultCompleteMode = ToilCompleteMode.Never;
            charge.initAction = () =>
            {
                chargeDuration = Pulse!.Props.warmupTicks;
                chargePosition = pawn.Position;
                chargeMap = pawn.Map;
            };
            charge.tickAction = () =>
            {
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
            release.initAction = () =>
            {
                if (released || !CanContinueCharge()) return;
                released = true;
                Pulse!.ReleaseAt(pawn.Map, pawn.Position);
                Pulse.NotifyReleased();
            };
            yield return release;
        }

        private bool CanContinueCharge() => Pulse?.CanOperate(pawn) == true
            && (chargeDuration <= 0 || (pawn.Map == chargeMap && pawn.Position == chargePosition
                && pawn.pather?.Moving != true && pawn.stances?.FullBodyBusy != true));

        public override void ExposeData()
        {
            Scribe_Values.Look(ref chargeTicks, "energyPulseChargeTicks");
            Scribe_Values.Look(ref chargeDuration, "energyPulseChargeDuration");
            Scribe_Values.Look(ref released, "energyPulseReleased");
            Scribe_Values.Look(ref chargePosition, "energyPulseChargePosition", IntVec3.Invalid);
            Scribe_References.Look(ref chargeMap, "energyPulseChargeMap");
            base.ExposeData();
        }
    }
}
