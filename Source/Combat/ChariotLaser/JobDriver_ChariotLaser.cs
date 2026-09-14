using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    public abstract class JobDriver_ChariotLaserBase : JobDriver
    {
        private int firingTicks;
        private int energyTickAccumulator;
        private int damageTickAccumulator;
        private bool firingStarted;
        private Pawn? laserSourcePawn;
        private List<int>? ignitionAttemptedThingIds;

        private MoteDualAttached? beamMote;
        private Sustainer? beamSustainer;

        protected CompChariotLaserSystem? LaserComp
        {
            get
            {
                ChariotLaserContextUtility.TryGetSourceComp(
                    pawn,
                    out CompChariotLaserSystem? comp);
                return comp;
            }
        }

        protected CompProperties_ChariotLaserSystem? LaserProps =>
            LaserComp?.Props;

        protected int FiringTicks => firingTicks;

        protected abstract int MaximumFiringTicks(
            CompProperties_ChariotLaserSystem props);

        protected abstract bool IsModeTargetValid(
            CompProperties_ChariotLaserSystem props);

        protected abstract TargetInfo CurrentBeamTarget(
            CompProperties_ChariotLaserSystem props);

        protected virtual Vector3 CurrentBeamTargetOffset(
            CompProperties_ChariotLaserSystem props,
            TargetInfo target)
        {
            return Vector3.zero;
        }

        protected abstract void ApplyEnergyInterval(
            CompProperties_ChariotLaserSystem props);

        protected abstract void ApplyDamageInterval(
            CompProperties_ChariotLaserSystem props);

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() =>
            {
                CompProperties_ChariotLaserSystem? props = LaserProps;
                return props == null
                    || !CanContinueCommon()
                    || !IsModeTargetValid(props);
            });
            AddFinishAction(_ => FinishLaser());

            yield return Toils_General.StopDead();

            CompProperties_ChariotLaserSystem? initialProps = LaserProps;
            if (initialProps == null)
            {
                yield break;
            }

            Toil warmup = Toils_General.Wait(initialProps.warmupTicks);
            warmup.handlingFacing = true;
            warmup.tickAction = delegate
            {
                FaceBeamTarget();
            };
            warmup.WithProgressBarToilDelay(TargetIndex.None);
            yield return warmup;

            Toil firing = ToilMaker.MakeToil("ChariotLaserFiring");
            firing.defaultCompleteMode = ToilCompleteMode.Never;
            firing.handlingFacing = true;
            firing.initAction = delegate
            {
                firingStarted = true;
                laserSourcePawn = LaserComp?.parent as Pawn;
                FaceBeamTarget();
            };
            firing.tickAction = TickFiring;
            yield return firing;
        }

        private void TickFiring()
        {
            CompProperties_ChariotLaserSystem? props = LaserProps;
            if (props == null
                || !CanContinueCommon()
                || !IsModeTargetValid(props))
            {
                EndJobWith(JobCondition.InterruptForced);
                return;
            }

            firingTicks++;
            FaceBeamTarget();
            MaintainBeam(props);

            energyTickAccumulator++;
            if (energyTickAccumulator >= props.energyIntervalTicks)
            {
                energyTickAccumulator = 0;
                if (!ChariotLaserContextUtility.TryConsumeEnergy(pawn, props))
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }

                ApplyEnergyInterval(props);
            }

            damageTickAccumulator++;
            if (damageTickAccumulator >= props.pawnDamageIntervalTicks)
            {
                damageTickAccumulator = 0;
                ApplyDamageInterval(props);
            }

            if (firingTicks >= MaximumFiringTicks(props))
            {
                EndJobWith(JobCondition.Succeeded);
            }
        }

        private bool CanContinueCommon()
        {
            return pawn.Spawned
                && pawn.Map != null
                && pawn.jobs != null
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Drafted;
        }

        private void FaceBeamTarget()
        {
            CompProperties_ChariotLaserSystem? props = LaserProps;
            if (props != null && pawn.rotationTracker != null)
            {
                TargetInfo target = CurrentBeamTarget(props);
                pawn.rotationTracker.Face(
                    target.CenterVector3
                    + CurrentBeamTargetOffset(props, target));
            }
        }

        private void MaintainBeam(
            CompProperties_ChariotLaserSystem props)
        {
            if (props.beamMoteDef == null)
            {
                return;
            }

            TargetInfo target = CurrentBeamTarget(props);
            Vector3 targetOffset =
                CurrentBeamTargetOffset(props, target);
            Vector3 direction =
                (target.CenterVector3 + targetOffset - pawn.DrawPos)
                    .Yto0()
                    .normalized;
            if (beamMote == null || beamMote.Destroyed)
            {
                beamMote = MoteMaker.MakeInteractionOverlay(
                    props.beamMoteDef,
                    pawn,
                    target);
            }

            beamMote?.UpdateTargets(
                new TargetInfo(pawn),
                target,
                direction * 0.85f,
                targetOffset);
            beamMote?.Maintain();

            if (props.muzzleFlashScale > 0.01f
                && props.muzzleFlashIntervalTicks > 0
                && (firingTicks == 1
                    || firingTicks % props.muzzleFlashIntervalTicks == 0))
            {
                FleckMaker.Static(
                    pawn.DrawPos + direction * 0.85f,
                    pawn.Map,
                    FleckDefOf.ShotFlash,
                    props.muzzleFlashScale);
            }

            if (beamSustainer == null || beamSustainer.Ended)
            {
                beamSustainer = props.beamSoundDef?.TrySpawnSustainer(
                    SoundInfo.InMap(pawn, MaintenanceType.PerTick));
            }

            beamSustainer?.Maintain();
        }

        private void FinishLaser()
        {
            beamSustainer?.End();
            beamSustainer = null;
            beamMote = null;

            if (!firingStarted)
            {
                return;
            }

            CompChariotLaserSystem? comp =
                laserSourcePawn?.GetComp<CompChariotLaserSystem>();
            if (comp == null)
            {
                ChariotLaserContextUtility.TryGetSourceComp(pawn, out comp);
            }

            comp?.StartCooldown();
        }

        protected void TryIgniteOnce(
            Thing thing,
            float chance)
        {
            ignitionAttemptedThingIds ??= new List<int>();
            if (ignitionAttemptedThingIds.Contains(thing.thingIDNumber))
            {
                return;
            }

            ignitionAttemptedThingIds.Add(thing.thingIDNumber);
            if (Rand.Chance(chance))
            {
                thing.TryAttachFire(0.15f, pawn);
            }
        }

        protected void ApplyDamage(
            Thing thing,
            DamageDef? damageDef,
            float amount,
            float armorPenetration,
            float ignitionChance)
        {
            if (damageDef == null || thing.Destroyed || amount <= 0f)
            {
                return;
            }

            thing.TakeDamage(new DamageInfo(
                damageDef,
                amount,
                armorPenetration,
                -1f,
                pawn));
            if (!thing.Destroyed)
            {
                TryIgniteOnce(thing, ignitionChance);
            }
        }

        protected IEnumerable<Thing> ThingsInRadius(
            IntVec3 center,
            float radius)
        {
            Map? map = pawn.Map;
            if (map == null)
            {
                yield break;
            }

            HashSet<int> yielded = new HashSet<int>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(
                         center,
                         radius,
                         useCenter: true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing != pawn
                        && !thing.Destroyed
                        && yielded.Add(thing.thingIDNumber))
                    {
                        yield return thing;
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref firingTicks, "firingTicks");
            Scribe_Values.Look(
                ref energyTickAccumulator,
                "energyTickAccumulator");
            Scribe_Values.Look(
                ref damageTickAccumulator,
                "damageTickAccumulator");
            Scribe_Values.Look(ref firingStarted, "firingStarted");
            Scribe_References.Look(ref laserSourcePawn, "laserSourcePawn");
            Scribe_Collections.Look(
                ref ignitionAttemptedThingIds,
                "ignitionAttemptedThingIds",
                LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ignitionAttemptedThingIds ??= new List<int>();
            }
        }
    }

    public sealed class JobDriver_ChariotTrackingLaser
        : JobDriver_ChariotLaserBase
    {
        private Thing? TrackedThing => job.GetTarget(TargetIndex.A).Thing;

        protected override int MaximumFiringTicks(
            CompProperties_ChariotLaserSystem props)
        {
            return props.trackingDurationTicks;
        }

        protected override bool IsModeTargetValid(
            CompProperties_ChariotLaserSystem props)
        {
            Thing? target = TrackedThing;
            return target != null
                && ChariotLaserCommandUtility.IsValidTrackingTarget(
                    pawn,
                    props,
                    target);
        }

        protected override TargetInfo CurrentBeamTarget(
            CompProperties_ChariotLaserSystem props)
        {
            return new TargetInfo(TrackedThing!);
        }

        protected override void ApplyEnergyInterval(
            CompProperties_ChariotLaserSystem props)
        {
            if (TrackedThing is Building building)
            {
                ApplyDamage(
                    building,
                    props.buildingDamageDef ?? props.heatDamageDef,
                    props.buildingDamage,
                    props.armorPenetration,
                    props.trackingIgnitionChance);
            }
        }

        protected override void ApplyDamageInterval(
            CompProperties_ChariotLaserSystem props)
        {
            Thing? target = TrackedThing;
            if (target == null || pawn.Map == null)
            {
                return;
            }

            if (target is Pawn)
            {
                ApplyDamage(
                    target,
                    props.heatDamageDef,
                    props.trackingPrimaryDamage,
                    props.armorPenetration,
                    props.trackingIgnitionChance);
            }

            foreach (Thing thing in ThingsInRadius(
                         target.Position,
                         props.effectRadius))
            {
                if (thing is Pawn && thing != target)
                {
                    ApplyDamage(
                        thing,
                        props.heatDamageDef,
                        props.trackingSplashDamage,
                        props.armorPenetration,
                        props.splashIgnitionChance);
                }
            }
        }
    }

    public sealed class JobDriver_ChariotSweepLaser
        : JobDriver_ChariotLaserBase
    {
        protected override int MaximumFiringTicks(
            CompProperties_ChariotLaserSystem props)
        {
            return props.sweepDurationTicks;
        }

        protected override bool IsModeTargetValid(
            CompProperties_ChariotLaserSystem props)
        {
            return ChariotLaserCommandUtility.IsValidSweepStart(
                    pawn,
                    props,
                    job.GetTarget(TargetIndex.A).Cell)
                && ChariotLaserCommandUtility.IsValidSweepEnd(
                    pawn,
                    props,
                    job.GetTarget(TargetIndex.A).Cell,
                    job.GetTarget(TargetIndex.B).Cell);
        }

        protected override TargetInfo CurrentBeamTarget(
            CompProperties_ChariotLaserSystem props)
        {
            return new TargetInfo(CurrentImpactCell(props), pawn.Map);
        }

        protected override Vector3 CurrentBeamTargetOffset(
            CompProperties_ChariotLaserSystem props,
            TargetInfo target)
        {
            return CurrentImpactPosition(props)
                - target.Cell.ToVector3Shifted();
        }

        protected override void ApplyEnergyInterval(
            CompProperties_ChariotLaserSystem props)
        {
        }

        protected override void ApplyDamageInterval(
            CompProperties_ChariotLaserSystem props)
        {
            IntVec3 impact = CurrentImpactCell(props);
            foreach (Thing thing in ThingsInRadius(
                         impact,
                         props.effectRadius))
            {
                if (thing is not Pawn && thing is not Building)
                {
                    continue;
                }

                bool center = thing.Position == impact;
                ApplyDamage(
                    thing,
                    props.heatDamageDef,
                    center
                        ? props.sweepCenterDamage
                        : props.sweepSplashDamage,
                    props.armorPenetration,
                    props.sweepIgnitionChance);
            }
        }

        private IntVec3 CurrentImpactCell(
            CompProperties_ChariotLaserSystem props)
        {
            return CurrentImpactPosition(props).ToIntVec3();
        }

        private Vector3 CurrentImpactPosition(
            CompProperties_ChariotLaserSystem props)
        {
            IntVec3 start = job.GetTarget(TargetIndex.A).Cell;
            IntVec3 end = job.GetTarget(TargetIndex.B).Cell;
            float duration = Mathf.Max(1f, props.sweepDurationTicks - 1f);
            float progress = Mathf.Clamp01((FiringTicks - 1f) / duration);
            Vector3 interpolated = Vector3.Lerp(
                start.ToVector3Shifted(),
                end.ToVector3Shifted(),
                progress);
            Map? map = pawn.Map;
            if (map == null)
            {
                return interpolated;
            }

            IntVec3 desired = interpolated.ToIntVec3();
            Vector3 source = pawn.Position.ToVector3Shifted();
            Vector3 sourceToTarget = (interpolated - source).Yto0();
            float visibleDistance = sourceToTarget.magnitude;
            Vector3 direction = sourceToTarget.normalized;
            IntVec3 visible = GenSight.LastPointOnLineOfSight(
                pawn.Position,
                desired,
                cell => cell.InBounds(map)
                    && cell.CanBeSeenOverFast(map),
                skipFirstCell: true);
            if (visible.IsValid)
            {
                visibleDistance -= (desired - visible).LengthHorizontal;
                return source
                    + direction * Mathf.Max(0f, visibleDistance);
            }

            return interpolated;
        }
    }
}
