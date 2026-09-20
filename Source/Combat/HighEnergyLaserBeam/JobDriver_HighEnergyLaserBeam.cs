using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    public sealed class JobDriver_HighEnergyLaserBeam : JobDriver
    {
        private int chargeTicks;
        private int firingTicks;
        private bool initialized;
        private bool firingStarted;
        private Map? castMap;
        private Vector3 impactPosition;
        private MoteDualAttached? outerBeam;
        private MoteDualAttached? coreBeam;
        private Sustainer? beamSound;
        private readonly List<Pawn> damageTargets = new List<Pawn>();
        private CompHighEnergyLaserBeam? Laser => pawn.GetComp<CompHighEnergyLaserBeam>();
        // 不从可能在读档后失效的 Thing 引用推断模式，防止追踪模式退化为地块模式。
        private bool TracksPawn => job.count == 1;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanContinue());
            AddFinishAction(_ => FinishVisuals());
            yield return Toils_General.StopDead();

            Toil charge = ToilMaker.MakeToil("HighEnergyLaserBeamCharge");
            charge.defaultCompleteMode = ToilCompleteMode.Never;
            charge.handlingFacing = true;
            charge.initAction = () =>
            {
                if (!initialized)
                {
                    castMap = pawn.Map;
                    impactPosition = job.targetB.Cell.ToVector3Shifted().Yto0();
                    initialized = true;
                }
                if (!CanContinue() || Laser?.HasEmitter(pawn) != true)
                    EndJobWith(JobCondition.InterruptForced);
            };
            charge.tickAction = () =>
            {
                if (!CanContinue())
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                pawn.rotationTracker?.Face(impactPosition);
                if (++chargeTicks >= CompHighEnergyLaserBeam.WarmupTicks) ReadyForNextToil();
            };
            charge.WithProgressBar(TargetIndex.None, () => Mathf.Clamp01((float)chargeTicks / CompHighEnergyLaserBeam.WarmupTicks));
            yield return charge;

            Toil firing = ToilMaker.MakeToil("HighEnergyLaserBeamFiring");
            firing.defaultCompleteMode = ToilCompleteMode.Never;
            firing.handlingFacing = true;
            firing.initAction = () =>
            {
                if (!CanContinue() || Laser?.HasEmitter(pawn) != true)
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                if (!firingStarted)
                {
                    firingStarted = true;
                    Laser!.NotifyFiringStarted();
                }
                MaintainVisuals();
            };
            firing.tickAction = TickFiring;
            yield return firing;
        }

        private bool CanContinue()
        {
            if (Laser?.CanOperate(pawn) != true) return false;
            if (initialized && (pawn.Map != castMap || pawn.Position != job.targetC.Cell
                || pawn.pather?.Moving == true || pawn.stances?.FullBodyBusy == true)) return false;
            if (!job.targetB.Cell.IsValid || !job.targetB.Cell.InBounds(pawn.Map)) return false;
            if (!TracksPawn) return true;
            Pawn? target = job.targetA.Thing as Pawn;
            return target != null && !target.Dead && !target.Destroyed
                && target.Spawned && target.Map == pawn.Map;
        }

        private void TickFiring()
        {
            if (!CanContinue())
            {
                EndJobWith(JobCondition.InterruptForced);
                return;
            }
            if (firingTicks >= CompHighEnergyLaserBeam.DurationTicks)
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }
            firingTicks++;
            // 两种周期共用已存档的发射 tick；先检查部件，失败时不结算本轮伤害。
            if (firingTicks % CompHighEnergyLaserBeam.EmitterCheckInterval == 0 && Laser?.HasEmitter(pawn) != true)
            {
                EndJobWith(JobCondition.InterruptForced);
                return;
            }
            // 第一帧保留选中时记录的格子，此后才移动，且始终保存浮点落点。
            if (TracksPawn && firingTicks > 1)
                impactPosition = Vector3.MoveTowards(impactPosition,
                    ((Pawn)job.targetA.Thing).DrawPos.Yto0(), CompHighEnergyLaserBeam.TrackingSpeedPerTick);
            pawn.rotationTracker?.Face(impactPosition);
            MaintainVisuals();
            if (firingTicks % CompHighEnergyLaserBeam.DamageInterval == 0) ApplyDamagePulse();

            // TakeDamage 可能杀死施法者并同步结束 Job，不能再结束其后续 Job。
            if (pawn.jobs?.curDriver != this) return;
            if (!CanContinue()) EndJobWith(JobCondition.InterruptForced);
            else if (firingTicks >= CompHighEnergyLaserBeam.DurationTicks) EndJobWith(JobCondition.Succeeded);
        }

        private void ApplyDamagePulse()
        {
            Map? map = castMap;
            CompHighEnergyLaserBeam? laser = Laser;
            if (map == null || laser == null) return;
            damageTargets.Clear();
            foreach (Pawn victim in map.mapPawns.AllPawnsSpawned)
            {
                if (!victim.Dead && !victim.Destroyed
                    && (victim.DrawPos.Yto0() - impactPosition).sqrMagnitude <= CompHighEnergyLaserBeam.Radius * CompHighEnergyLaserBeam.Radius)
                    damageTargets.Add(victim);
            }
            // 先取快照，防止死亡/离图回调修改地图 Pawn 列表；包括友方和施法者。
            foreach (Pawn victim in damageTargets)
            {
                if (victim.Dead || victim.Destroyed || !victim.Spawned || victim.Map != map) continue;
                DamageInfo damage = new DamageInfo(HighEnergyLaserBeamDefOf.MAP_HighEnergyLaserBeamHeat,
                    laser.Props.damageAmount, 0f, -1f, pawn);
                damage.SetIgnoreArmor(true);
                victim.TakeDamage(damage);
            }
            damageTargets.Clear();
        }

        private void MaintainVisuals()
        {
            if (!pawn.Spawned || pawn.Map != castMap) return;
            TargetInfo target = new TargetInfo(impactPosition.ToIntVec3(), castMap);
            Vector3 offset = impactPosition - target.Cell.ToVector3Shifted();
            Vector3 direction = (impactPosition - pawn.DrawPos).Yto0().normalized;
            MaintainBeam(ref outerBeam, HighEnergyLaserBeamDefOf.Mote_MAP_HighEnergyLaserBeamOuter, target, offset, direction);
            MaintainBeam(ref coreBeam, HighEnergyLaserBeamDefOf.Mote_MAP_HighEnergyLaserBeamCore, target, offset, direction);
            if (beamSound == null || beamSound.Ended)
                beamSound = DefDatabase<SoundDef>.GetNamedSilentFail("BeamGraser_Shooting")
                    ?.TrySpawnSustainer(SoundInfo.InMap(pawn, MaintenanceType.PerTick));
            beamSound?.Maintain();
        }

        private void MaintainBeam(ref MoteDualAttached? mote, ThingDef def, TargetInfo target,
            Vector3 offset, Vector3 direction)
        {
            if (mote == null || mote.Destroyed)
                mote = MoteMaker.MakeInteractionOverlay(def, pawn, target);
            mote?.UpdateTargets(new TargetInfo(pawn), target, direction * 0.85f, offset);
            mote?.Maintain();
        }

        private void FinishVisuals()
        {
            beamSound?.End();
            beamSound = null;
            // 与战车一致，停止维护后由 Mote 自行淡出；视觉不负责伤害。
            outerBeam = null;
            coreBeam = null;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref chargeTicks, "highEnergyLaserBeamChargeTicks");
            Scribe_Values.Look(ref firingTicks, "highEnergyLaserBeamFiringTicks");
            Scribe_Values.Look(ref initialized, "highEnergyLaserBeamInitialized");
            Scribe_Values.Look(ref firingStarted, "highEnergyLaserBeamFiringStarted");
            Scribe_Values.Look(ref impactPosition, "highEnergyLaserBeamImpactPosition");
            Scribe_References.Look(ref castMap, "highEnergyLaserBeamCastMap");
            // base 在 PostLoadInit 重建 Toil，不能在 MakeNewToils 中重置上述状态。
            base.ExposeData();
        }
    }
}
