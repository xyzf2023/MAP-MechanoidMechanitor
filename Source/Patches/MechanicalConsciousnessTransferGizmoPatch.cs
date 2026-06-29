using System;
using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
    public static class Patch_MechanitorUtility_GetMechGizmos_MechanicalConsciousnessTransfer
    {
        private const string LabelKey = "MAP_MechanoidMechanitor.ConsciousnessTransfer.Label";
        private const string DescriptionKey =
            "MAP_MechanoidMechanitor.ConsciousnessTransfer.Description";
        private const string NoCandidateKey =
            "MAP_MechanoidMechanitor.ConsciousnessTransfer.NoCandidate";
        private const string RecoveringEmergencyDataKey =
            "MAP_MechanoidMechanitor.ConsciousnessTransfer.RecoveringEmergencyData";
        private const string FailedKey = "MAP_MechanoidMechanitor.ConsciousnessTransfer.Failed";

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn mech)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!ShouldShowConsciousnessTransferGizmo(mech))
            {
                yield break;
            }

            yield return MakeConsciousnessTransferCommand(mech);
        }

        private static bool ShouldShowConsciousnessTransferGizmo(Pawn? mech)
        {
            return ModsConfig.BiotechActive
                && JusticeScenarioUtility.IsJusticeScenarioActive
                && mech != null
                && !mech.Dead
                && !mech.Destroyed
                && GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(mech)
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(mech)
                && mech.Faction != null
                && mech.Faction.IsPlayerSafe()
                && mech.jobs != null
                && mech.Spawned
                && mech.Map != null;
        }

        private static Command_Action MakeConsciousnessTransferCommand(Pawn source)
        {
            List<Pawn> candidates = BuildTransferTargets(source);
            Command_Action command = new Command_Action
            {
                defaultLabel = LabelKey.Translate(),
                defaultDesc = DescriptionKey.Translate(),
                icon = TexCommand.Install,
                action = delegate
                {
                    OpenTransferTargetMenu(source);
                }
            };

            if (EmergencyMechanicalConsciousnessTransferUtility
                    .HasEmergencyConsciousnessTransferHediff(source))
            {
                command.Disable(RecoveringEmergencyDataKey.Translate());
            }
            else if (candidates.Count == 0)
            {
                command.Disable(NoCandidateKey.Translate());
            }

            return command;
        }

        private static void OpenTransferTargetMenu(Pawn source)
        {
            List<FloatMenuOption> options = BuildFloatMenuOptions(source);
            if (options.Count == 0)
            {
                Messages.Message(
                    FailedKey.Translate(),
                    source,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<FloatMenuOption> BuildFloatMenuOptions(Pawn source)
        {
            List<Pawn> candidates = BuildTransferTargets(source);
            List<FloatMenuOption> options = new List<FloatMenuOption>(candidates.Count);

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn target = candidates[i];
                Pawn localTarget = target;
                options.Add(new FloatMenuOption(
                    localTarget.LabelShortCap,
                    delegate
                    {
                        TryStartTransferJob(source, localTarget);
                    }));
            }

            return options;
        }

        private static List<Pawn> BuildTransferTargets(Pawn source)
        {
            HashSet<Pawn> seen = new HashSet<Pawn>();
            List<Pawn> targets = new List<Pawn>();

            foreach (Pawn candidate in GameComponent_MechanoidMechanitorRegistry
                         .GetMechanicalConsciousnessCandidates())
            {
                if (candidate == null
                    || candidate.Destroyed
                    || !seen.Add(candidate))
                {
                    continue;
                }

                if (!MechanicalConsciousnessTransferUtility
                        .CanVoluntarilyTransferMechanicalConsciousness(
                            source,
                            candidate))
                {
                    continue;
                }

                targets.Add(candidate);
            }

            targets.Sort((left, right) => string.Compare(
                left.LabelShortCap,
                right.LabelShortCap,
                StringComparison.Ordinal));
            return targets;
        }

        private static void TryStartTransferJob(Pawn source, Pawn target)
        {
            if (source == null
                || target == null
                || source.jobs == null
                || !source.Spawned
                || source.Map == null
                || !GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(source)
                || !MechanicalConsciousnessTransferUtility
                    .CanVoluntarilyTransferMechanicalConsciousness(
                        source,
                        target))
            {
                Messages.Message(
                    FailedKey.Translate(),
                    source,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_TransferMechanicalConsciousness,
                source,
                target);
            source.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
