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
        private Mote? areaIndicator;
        private Sustainer? beamSound;
        private readonly List<Pawn> damageTargets = new List<Pawn>();
        private readonly HashSet<Building> buildingDamageTargets = new HashSet<Building>();
        private CompHighEnergyLaserBeam? Laser => pawn.GetComp<CompHighEnergyLaserBeam>();
        internal bool IsFiring => firingStarted;
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
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                UpdateWarmupImpactPosition();
                MaintainAreaIndicator();
            };
            charge.tickAction = () =>
            {
                if (!CanContinue())
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                UpdateWarmupImpactPosition();
                pawn.rotationTracker?.Face(impactPosition);
                MaintainAreaIndicator();
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

        private void UpdateWarmupImpactPosition()
        {
            // 调用前已通过 CanContinue，目标必须仍存活且位于同一地图。
            if (TracksPawn && Laser?.Props.trackPawnDuringWarmup == true
                && job.targetA.Thing is Pawn target)
                impactPosition = target.DrawPos.Yto0();
        }

        private bool CanContinue()
        {
            if (Laser?.CanOperate(pawn) != true) return false;
            // 玩家仍保留原有的部件检查周期；BOSS 部件真正损毁后当 tick 停止技能。
            if (pawn.GetComp<CompSunBossState>() != null && Laser.HasEmitter(pawn) != true) return false;
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
            CompProperties_HighEnergyLaserBeam? props = Laser?.Props;
            if (props == null || !CanContinue())
            {
                EndJobWith(JobCondition.InterruptForced);
                return;
            }
            if (firingTicks >= props.durationTicks)
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
            // 第一帧沿用蓄力结束时的落点；未开启蓄力跟随时仍为选中格，此后按速度追踪。
            if (TracksPawn && firingTicks > 1)
                impactPosition = Vector3.MoveTowards(impactPosition,
                    ((Pawn)job.targetA.Thing).DrawPos.Yto0(), props.trackingSpeed / 60f);
            pawn.rotationTracker?.Face(impactPosition);
            MaintainVisuals();
            if (firingTicks % CompHighEnergyLaserBeam.DamageInterval == 0) ApplyDamagePulse();

            // TakeDamage 可能杀死施法者并同步结束 Job，不能再结束其后续 Job。
            if (pawn.jobs?.curDriver != this) return;
            if (firingTicks % CompHighEnergyLaserBeam.BuildingDamageInterval == 0) ApplyBuildingDamagePulse();
            if (pawn.jobs?.curDriver != this) return;
            if (!CanContinue()) EndJobWith(JobCondition.InterruptForced);
            else if (firingTicks >= props.durationTicks) EndJobWith(JobCondition.Succeeded);
        }

        private void ApplyDamagePulse()
        {
            Map? map = castMap;
            CompHighEnergyLaserBeam? laser = Laser;
            if (map == null || laser == null) return;
            damageTargets.Clear();
            float halfSide = CompHighEnergyLaserBeam.AreaSideLength * 0.5f;
            foreach (Pawn victim in map.mapPawns.AllPawnsSpawned)
            {
                Vector3 offset = victim.DrawPos.Yto0() - impactPosition;
                // 与范围贴图共用浮点中心，正方形沿地图坐标轴对齐，不随射线旋转。
                if (!victim.Dead && !victim.Destroyed
                    && Mathf.Abs(offset.x) <= halfSide && Mathf.Abs(offset.z) <= halfSide)
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

        private void ApplyBuildingDamagePulse()
        {
            Map? map = castMap;
            if (map == null) return;
            buildingDamageTargets.Clear();
            float halfSide = CompHighEnergyLaserBeam.AreaSideLength * 0.5f;
            // 收集与浮点正方形范围相交的占地格；多格建筑每轮只结算一次。
            int minX = Mathf.Max(0, Mathf.FloorToInt(impactPosition.x - halfSide));
            int maxX = Mathf.Min(map.Size.x - 1, Mathf.CeilToInt(impactPosition.x + halfSide) - 1);
            int minZ = Mathf.Max(0, Mathf.FloorToInt(impactPosition.z - halfSide));
            int maxZ = Mathf.Min(map.Size.z - 1, Mathf.CeilToInt(impactPosition.z + halfSide) - 1);
            for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                    foreach (Thing thing in new IntVec3(x, 0, z).GetThingList(map))
                        if (thing is Building building && !building.Destroyed)
                            buildingDamageTargets.Add(building);

            // 完成快照后再伤害，避免建筑销毁及其回调修改格内物体列表。
            foreach (Building building in buildingDamageTargets)
            {
                if (pawn.jobs?.curDriver != this) break;
                if (building.Destroyed || !building.Spawned || building.Map != map) continue;
                DamageInfo damage = new DamageInfo(HighEnergyLaserBeamDefOf.MAP_ChariotLaserSiege,
                    CompHighEnergyLaserBeam.BuildingDamageAmount, 1.5f, -1f, pawn);
                damage.SetIgnoreArmor(true);
                building.TakeDamage(damage);
            }
            buildingDamageTargets.Clear();
        }

        private void MaintainVisuals()
        {
            if (!pawn.Spawned || pawn.Map != castMap) return;
            MaintainAreaIndicator();
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

        private void MaintainAreaIndicator()
        {
            if (!pawn.Spawned || castMap == null || pawn.Map != castMap) return;
            IntVec3 cell = impactPosition.ToIntVec3();
            if (!cell.InBounds(castMap)) return;
            if (areaIndicator == null || areaIndicator.Destroyed)
            {
                areaIndicator = (Mote)ThingMaker.MakeThing(HighEnergyLaserBeamDefOf.Mote_MAP_HighEnergyLaserBeamArea);
                areaIndicator.exactPosition = impactPosition;
                areaIndicator.Scale = CompHighEnergyLaserBeam.AreaSideLength;
                GenSpawn.Spawn(areaIndicator, cell, castMap);
            }
            areaIndicator.Position = cell;
            areaIndicator.exactPosition = impactPosition;
            // 蓄力每 15 tick 明暗切换；使用已存档进度，暂停/读档不重置节奏。
            float alpha = firingStarted || chargeTicks % 30 < 15 ? 1f : 0f;
            areaIndicator.instanceColor = new Color(1f, 1f, 1f, alpha);
            areaIndicator.Maintain();
        }

        private void FinishVisuals()
        {
            if (areaIndicator != null && !areaIndicator.Destroyed) areaIndicator.Destroy();
            areaIndicator = null;
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
