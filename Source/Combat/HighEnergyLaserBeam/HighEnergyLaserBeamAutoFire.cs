using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅为原版索敌提供激光射程与射线判定，实际施放仍由已有 Job 执行。</summary>
    internal sealed class HighEnergyLaserBeamTargetSearcher : IAttackTargetSearcher
    {
        private const float MinTargetDistance = 5f;
        private readonly Pawn pawn;
        private readonly CompHighEnergyLaserBeam laser;

        public Thing Thing => pawn;
        public Verb CurrentEffectiveVerb { get; }
        public LocalTargetInfo LastAttackedTarget => pawn.LastAttackedTarget;
        public int LastAttackTargetTick => pawn.LastAttackTargetTick;

        internal HighEnergyLaserBeamTargetSearcher(Pawn pawn, CompHighEnergyLaserBeam laser)
        {
            this.pawn = pawn;
            this.laser = laser;
            CurrentEffectiveVerb = new Verb_HighEnergyLaserBeamTargeting
            {
                caster = pawn,
                verbTracker = pawn.VerbTracker,
                verbProps = new VerbProperties
                {
                    verbClass = typeof(Verb_HighEnergyLaserBeamTargeting),
                    range = laser.Props.range,
                    minRange = MinTargetDistance,
                    requireLineOfSight = true
                }
            };
        }

        internal Thing? FindTarget() => AttackTargetFinder.BestShootTargetFromCurrentPosition(
            this, TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable,
            IsValidTarget, MinTargetDistance, laser.Props.range)?.Thing;

        internal static LocalTargetInfo CastTarget(Thing target) => target is Pawn
            ? new LocalTargetInfo(target) : new LocalTargetInfo(target.Position);

        private bool IsValidTarget(Thing target)
        {
            if (target.Destroyed || !target.Spawned || target.Map != pawn.Map
                || (target.Position - pawn.Position).LengthHorizontalSquared <= MinTargetDistance * MinTargetDistance
                || !laser.ValidInitialTarget(pawn, CastTarget(target))) return false;
            // 搜索器不是 Pawn，显式保留原版对 Pawn 所属 Lord 的目标限制。
            Lord? lord = pawn.GetLord();
            return (lord == null || lord.LordJob.ValidateAttackTarget(pawn, target))
                && CurrentEffectiveVerb.CanHitTargetFrom(pawn.Position, target);
        }

        private sealed class Verb_HighEnergyLaserBeamTargeting : Verb
        {
            protected override bool TryCastShot() => false;
        }
    }

    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    internal static class HighEnergyLaserBeamFireAtWillGizmoPatch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn_DraftController __instance)
        {
            bool showToggle = __instance.Drafted
                && __instance.pawn.GetComp<CompHighEnergyLaserBeam>()?.SupportsFireAtWill == true;
            bool foundToggle = false;
            foreach (Gizmo gizmo in __result)
            {
                if (showToggle && gizmo is Command_Toggle toggle && toggle.tutorTag == "FireAtWillToggle")
                {
                    // 装备额外远程武器时复用原按钮，避免同一状态出现两个开关。
                    toggle.defaultDesc = "MAP_HighEnergyLaserBeam.FireAtWill.Description".Translate();
                    foundToggle = true;
                }
                yield return gizmo;
            }
            if (!showToggle || foundToggle) yield break;
            yield return new Command_Toggle
            {
                hotKey = KeyBindingDefOf.Misc6,
                isActive = () => __instance.FireAtWill,
                toggleAction = () => __instance.FireAtWill = !__instance.FireAtWill,
                icon = TexCommand.FireAtWill,
                defaultLabel = "CommandFireAtWillLabel".Translate(),
                defaultDesc = "MAP_HighEnergyLaserBeam.FireAtWill.Description".Translate(),
                tutorTag = "FireAtWillToggle"
            };
        }
    }

    [HarmonyPatch(typeof(JobDriver_Wait), "CheckForAutoAttack")]
    internal static class HighEnergyLaserBeamAutoAttackPatch
    {
        private static void Postfix(JobDriver_Wait __instance)
        {
            Pawn pawn = __instance.pawn;
            CompHighEnergyLaserBeam? laser = pawn?.GetComp<CompHighEnergyLaserBeam>();
            if (laser?.AutoFireEnabled != true) return;
            // 沿用原版每 4 tick 的待战检查；先保留原版贴身近战、灭火和正在执行的攻击。
            if (pawn?.jobs?.curDriver != __instance || __instance.ended || __instance.collideWithPawns
                || __instance.job?.def != JobDefOf.Wait_Combat || !__instance.job.canUseRangedWeapon
                || !pawn.kindDef.canMeleeAttack || pawn.stances?.FullBodyBusy != false
                || pawn.IsCarryingPawn() || pawn.IsShambler || pawn.WorkTagIsDisabled(WorkTags.Violent)
                || (!pawn.IsPlayerControlled && pawn.IsPsychologicallyInvisible())) return;
            Job? job = laser.TryMakeAutoFireJob();
            if (job != null) pawn.jobs.StartJob(job, JobCondition.InterruptForced, cancelBusyStances: false);
        }
    }
}
