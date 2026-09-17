using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_ChariotLaserSystem : CompProperties
    {
        public float range = 29.9f;
        public float minRange = 2.9f;
        public float sweepMaxSpan = 10f;
        public float effectRadius = 1.5f;

        public int warmupTicks = 60;
        public int trackingDurationTicks = 600;
        public int sweepDurationTicks = 240;
        public int energyIntervalTicks = 6;
        public int pawnDamageIntervalTicks = 10;
        public int cooldownTicks = 180;
        public int muzzleFlashIntervalTicks = 22;

        public float energyFractionPerInterval = 0.0005f;
        public float muzzleFlashScale = 9f;
        public float trackingPrimaryDamage = 3f;
        public float trackingSplashDamage = 1.5f;
        public float sweepCenterDamage = 4f;
        public float sweepSplashDamage = 2f;
        public float buildingDamage = 50f;
        public float armorPenetration = 1.5f;
        public float trackingIgnitionChance = 0.7f;
        public float splashIgnitionChance = 0.3f;
        public float sweepIgnitionChance = 0.4f;

        public DamageDef? heatDamageDef;
        public DamageDef? buildingDamageDef;
        public ThingDef? beamMoteDef;
        public SoundDef? beamSoundDef;

        public CompProperties_ChariotLaserSystem()
        {
            compClass = typeof(CompChariotLaserSystem);
        }
    }

    /// <summary>
    /// 战车激光的资格、参数与共享冷却。融合形态读取源战车上的同一个组件，
    /// 因而冷却不会因合体/解除合体而重置。
    /// </summary>
    public sealed class CompChariotLaserSystem : ThingComp
    {
        private int cooldownUntilTick;

        public CompProperties_ChariotLaserSystem Props =>
            (CompProperties_ChariotLaserSystem)props;

        public int CooldownTicksRemaining =>
            Mathf.Max(0, cooldownUntilTick - Find.TickManager.TicksGame);

        public bool IsOnCooldown => CooldownTicksRemaining > 0;

        public void StartCooldown()
        {
            cooldownUntilTick = Mathf.Max(
                cooldownUntilTick,
                Find.TickManager.TicksGame + Props.cooldownTicks);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent is Pawn pawn && pawn.Faction == Faction.OfPlayer)
            {
                foreach (Gizmo gizmo in ChariotLaserCommandUtility.GetGizmos(
                             pawn,
                             this))
                {
                    yield return gizmo;
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(
                ref cooldownUntilTick,
                "chariotLaserCooldownUntilTick");
        }
    }

    internal static class ChariotLaserContextUtility
    {
        public static bool TryGetSourceComp(
            Pawn actor,
            out CompChariotLaserSystem? comp)
        {
            comp = actor.GetComp<CompChariotLaserSystem>();
            if (comp != null)
            {
                return true;
            }

            if (MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                    actor,
                    out MechFusionSession? session)
                && session?.SourcePawn != null)
            {
                comp = session.SourcePawn.GetComp<CompChariotLaserSystem>();
            }

            return comp != null;
        }

        public static bool IsLaserJob(JobDef? def)
        {
            return def == MAPMechanitor_JobDefOf.MAP_ChariotTrackingLaser
                || def == MAPMechanitor_JobDefOf.MAP_ChariotSweepLaser;
        }

        public static bool CanStart(
            Pawn actor,
            CompChariotLaserSystem comp,
            out string? reason)
        {
            reason = null;
            if (!actor.Spawned || actor.Map == null || actor.jobs == null
                || actor.Dead || actor.Downed)
            {
                reason =
                    "MAP_MechanoidMechanitor.ChariotLaser.Disabled.Unavailable"
                        .Translate();
                return false;
            }

            if (actor.Faction != Faction.OfPlayer || !actor.Drafted)
            {
                reason =
                    "MAP_MechanoidMechanitor.ChariotLaser.Disabled.NotDrafted"
                        .Translate();
                return false;
            }

            if (actor.CurJobDef
                == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding)
            {
                reason =
                    "MAP_MechanoidMechanitor.ChariotLaser.Disabled.Emergency"
                        .Translate();
                return false;
            }

            if (comp.IsOnCooldown)
            {
                reason =
                    "MAP_MechanoidMechanitor.ChariotLaser.Disabled.Cooldown"
                        .Translate(
                            (comp.CooldownTicksRemaining / 60f).ToString("0.0"));
                return false;
            }

            if (!MechanicalFlightEnergyUtility.TryGetEnergyFraction(
                    actor,
                    out float energy)
                || energy + 0.000001f
                    < comp.Props.energyFractionPerInterval)
            {
                reason =
                    "MAP_MechanoidMechanitor.ChariotLaser.Disabled.NoEnergy"
                        .Translate();
                return false;
            }

            return true;
        }

        public static bool TryConsumeEnergy(
            Pawn actor,
            CompProperties_ChariotLaserSystem props)
        {
            return MechanicalFlightEnergyUtility.TryGetEnergyFraction(
                       actor,
                       out float energy)
                && energy + 0.000001f >= props.energyFractionPerInterval
                && MechanicalFlightEnergyUtility
                    .TryConsumeMaximumEnergyFraction(
                        actor,
                        props.energyFractionPerInterval);
        }
    }

    [StaticConstructorOnStartup]
    internal static class ChariotLaserCommandUtility
    {
        private static readonly Texture2D TrackingLaserIcon =
            ContentFinder<Texture2D>.Get(
                "UI/LaserFocus",
                reportFailure: false)
            ?? TexCommand.Attack;

        private static readonly Texture2D SweepLaserIcon =
            ContentFinder<Texture2D>.Get(
                "UI/LaserSweep",
                reportFailure: false)
            ?? TexCommand.Attack;

        public static IEnumerable<Gizmo> GetGizmos(
            Pawn actor,
            CompChariotLaserSystem comp)
        {
            bool trackingActive =
                actor.CurJobDef
                == MAPMechanitor_JobDefOf.MAP_ChariotTrackingLaser;
            bool sweepActive =
                actor.CurJobDef
                == MAPMechanitor_JobDefOf.MAP_ChariotSweepLaser;

            yield return BuildTrackingCommand(
                actor,
                comp,
                trackingActive,
                sweepActive);
            yield return BuildSweepCommand(
                actor,
                comp,
                sweepActive,
                trackingActive);
        }

        private static Command BuildTrackingCommand(
            Pawn actor,
            CompChariotLaserSystem comp,
            bool active,
            bool otherLaserActive)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = active
                    ? "MAP_MechanoidMechanitor.ChariotLaser.StopTracking.Label"
                        .Translate()
                    : "MAP_MechanoidMechanitor.ChariotLaser.Tracking.Label"
                        .Translate(),
                defaultDesc = active
                    ? "MAP_MechanoidMechanitor.ChariotLaser.Stop.Description"
                        .Translate()
                    : "MAP_MechanoidMechanitor.ChariotLaser.Tracking.Description"
                        .Translate(),
                icon = TrackingLaserIcon,
                action = active
                    ? () => actor.jobs?.EndCurrentJob(
                        JobCondition.InterruptForced)
                    : () => BeginTrackingTargeting(actor, comp)
            };

            ApplyDisabledState(
                command,
                actor,
                comp,
                active,
                otherLaserActive);
            return command;
        }

        private static Command BuildSweepCommand(
            Pawn actor,
            CompChariotLaserSystem comp,
            bool active,
            bool otherLaserActive)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = active
                    ? "MAP_MechanoidMechanitor.ChariotLaser.StopSweep.Label"
                        .Translate()
                    : "MAP_MechanoidMechanitor.ChariotLaser.Sweep.Label"
                        .Translate(),
                defaultDesc = active
                    ? "MAP_MechanoidMechanitor.ChariotLaser.Stop.Description"
                        .Translate()
                    : "MAP_MechanoidMechanitor.ChariotLaser.Sweep.Description"
                        .Translate(),
                icon = SweepLaserIcon,
                action = active
                    ? () => actor.jobs?.EndCurrentJob(
                        JobCondition.InterruptForced)
                    : () => BeginSweepStartTargeting(actor, comp)
            };

            ApplyDisabledState(
                command,
                actor,
                comp,
                active,
                otherLaserActive);
            return command;
        }

        private static void ApplyDisabledState(
            Command command,
            Pawn actor,
            CompChariotLaserSystem comp,
            bool active,
            bool otherLaserActive)
        {
            if (active)
            {
                return;
            }

            if (otherLaserActive)
            {
                command.Disable(
                    "MAP_MechanoidMechanitor.ChariotLaser.Disabled.Busy"
                        .Translate());
                return;
            }

            if (!ChariotLaserContextUtility.CanStart(
                    actor,
                    comp,
                    out string? reason))
            {
                command.Disable(reason ?? string.Empty);
            }
        }

        private static void BeginTrackingTargeting(
            Pawn actor,
            CompChariotLaserSystem comp)
        {
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetLocations = false,
                canTargetItems = false,
                canTargetSelf = false,
                mapObjectTargetsMustBeAutoAttackable = false
            };

            Find.Targeter.BeginTargeting(
                parameters,
                target => TryStartTrackingJob(actor, comp, target),
                target =>
                {
                    DrawActorRange(actor, comp.Props);
                    DrawTargetHighlight(target);
                },
                target => IsValidTrackingTarget(actor, comp.Props, target),
                actor);
        }

        private static void BeginSweepStartTargeting(
            Pawn actor,
            CompChariotLaserSystem comp)
        {
            Find.Targeter.BeginTargeting(
                TargetingParameters.ForCell(),
                first => BeginSweepEndTargeting(actor, comp, first.Cell),
                target =>
                {
                    DrawActorRange(actor, comp.Props);
                    DrawTargetHighlight(target);
                },
                target => IsValidSweepStart(actor, comp.Props, target.Cell),
                actor);
        }

        private static void BeginSweepEndTargeting(
            Pawn actor,
            CompChariotLaserSystem comp,
            IntVec3 first)
        {
            if (!actor.Spawned || actor.Map == null)
            {
                return;
            }

            Find.Targeter.BeginTargeting(
                TargetingParameters.ForCell(),
                second => TryStartSweepJob(
                    actor,
                    comp,
                    first,
                    second.Cell),
                target =>
                {
                    DrawActorRange(actor, comp.Props);
                    GenDraw.DrawTargetHighlight(first);
                    DrawTargetHighlight(target);
                    GenDraw.DrawRadiusRing(first, comp.Props.sweepMaxSpan);
                },
                target => IsValidSweepEnd(
                    actor,
                    comp.Props,
                    first,
                    target.Cell),
                actor);
        }

        private static void TryStartTrackingJob(
            Pawn actor,
            CompChariotLaserSystem comp,
            LocalTargetInfo target)
        {
            if (!ChariotLaserContextUtility.CanStart(actor, comp, out _)
                || !IsValidTrackingTarget(actor, comp.Props, target))
            {
                Reject(actor);
                return;
            }

            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_ChariotTrackingLaser,
                target);
            job.playerForced = true;
            if (actor.jobs == null
                || !actor.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                Reject(actor);
            }
        }

        private static void TryStartSweepJob(
            Pawn actor,
            CompChariotLaserSystem comp,
            IntVec3 first,
            IntVec3 second)
        {
            if (!ChariotLaserContextUtility.CanStart(actor, comp, out _)
                || !IsValidSweepStart(actor, comp.Props, first)
                || !IsValidSweepEnd(actor, comp.Props, first, second))
            {
                Reject(actor);
                return;
            }

            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_ChariotSweepLaser,
                first,
                second);
            job.playerForced = true;
            if (actor.jobs == null
                || !actor.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                Reject(actor);
            }
        }

        internal static bool IsValidTrackingTarget(
            Pawn actor,
            CompProperties_ChariotLaserSystem props,
            LocalTargetInfo target)
        {
            Thing? thing = target.Thing;
            if (thing == null || thing == actor || !thing.Spawned
                || thing.Map != actor.Map)
            {
                return false;
            }

            if (thing is Pawn targetPawn)
            {
                if (targetPawn.Dead)
                {
                    return false;
                }
            }
            else if (thing is Building building)
            {
                if (!building.def.useHitPoints || building.Destroyed)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if (thing.Faction != null && thing.Faction == actor.Faction)
            {
                return false;
            }

            return !thing.Fogged()
                && IsWithinWeaponRange(actor, props, thing.Position)
                && GenSight.LineOfSight(
                    actor.Position,
                    thing.Position,
                    actor.Map);
        }

        internal static bool IsValidSweepStart(
            Pawn actor,
            CompProperties_ChariotLaserSystem props,
            IntVec3 cell)
        {
            return cell.IsValid
                && actor.Map != null
                && cell.InBounds(actor.Map)
                && !cell.Fogged(actor.Map)
                && IsWithinWeaponRange(actor, props, cell)
                && GenSight.LineOfSight(actor.Position, cell, actor.Map);
        }

        internal static bool IsValidSweepEnd(
            Pawn actor,
            CompProperties_ChariotLaserSystem props,
            IntVec3 first,
            IntVec3 second)
        {
            return IsValidSweepStart(actor, props, second)
                && (second - first).LengthHorizontalSquared
                    <= props.sweepMaxSpan * props.sweepMaxSpan;
        }

        internal static bool IsWithinWeaponRange(
            Pawn actor,
            CompProperties_ChariotLaserSystem props,
            IntVec3 cell)
        {
            float distanceSquared =
                (cell - actor.Position).LengthHorizontalSquared;
            return distanceSquared >= props.minRange * props.minRange
                && distanceSquared <= props.range * props.range;
        }

        private static void DrawTargetHighlight(LocalTargetInfo target)
        {
            if (target.IsValid)
            {
                GenDraw.DrawTargetHighlight(target);
            }
        }

        private static void DrawActorRange(
            Pawn actor,
            CompProperties_ChariotLaserSystem props)
        {
            if (actor.Spawned)
            {
                GenDraw.DrawRadiusRing(actor.Position, props.range);
                GenDraw.DrawRadiusRing(actor.Position, props.minRange);
            }
        }

        private static void Reject(Pawn actor)
        {
            Messages.Message(
                "MAP_MechanoidMechanitor.ChariotLaser.Disabled.Unavailable"
                    .Translate(),
                actor,
                MessageTypeDefOf.RejectInput,
                historical: false);
        }
    }
}
