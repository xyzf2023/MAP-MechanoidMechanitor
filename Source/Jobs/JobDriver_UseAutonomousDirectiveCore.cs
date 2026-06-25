using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class JobDriver_UseAutonomousDirectiveCore : JobDriver
    {
        private const int InstallDurationTicks = 600;
        private const string AutonomousDirectiveCoreDefName = "MAP_AutonomousDirectiveCore";

        private Thing Module => TargetThingA;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(
                job.GetTarget(TargetIndex.A),
                job,
                1,
                -1,
                null,
                errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanInstallNow());

            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil wait = Toils_General.Wait(InstallDurationTicks, TargetIndex.A);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            yield return Toils_General.Do(ApplyInstallation);
        }

        private bool CanInstallNow()
        {
            return ModsConfig.BiotechActive
                && pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && pawn.health?.hediffSet != null
                && Module != null
                && !Module.Destroyed
                && Module.Spawned
                && !Module.IsForbidden(pawn)
                && IsAutonomousDirectiveCore(Module)
                && !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn);
        }

        private void ApplyInstallation()
        {
            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return;
            }

            if (!CanInstallNow())
            {
                Messages.Message(
                    $"{pawn.LabelShort}无法安装自律指令核心。",
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (!MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn)
                || !MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn)
                || !MechanoidMechanitorRoleUtility.HasAcquiredMechanitorHediff(pawn))
            {
                Messages.Message(
                    $"{pawn.LabelShort}无法安装自律指令核心。",
                    pawn,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Module.Destroy(DestroyMode.Vanish);
            Messages.Message(
                $"{pawn.LabelShort}已成为机械族机械师。",
                pawn,
                MessageTypeDefOf.PositiveEvent);
        }

        private static bool IsAutonomousDirectiveCore(Thing? thing)
        {
            return thing?.def?.defName == AutonomousDirectiveCoreDefName;
        }
    }
}
