using System;
using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
    public static class Patch_MechanitorUtility_GetMechGizmos_MechanicalConsciousnessTransfer
    {
        private const string LabelKey = "MAP_MechanoidMechanitor.Justice.ConsciousnessTransfer.Label";
        private const string DescriptionKey =
            "MAP_MechanoidMechanitor.Justice.ConsciousnessTransfer.Description";
        private const string NoCandidateKey =
            "MAP_MechanoidMechanitor.Justice.ConsciousnessTransfer.NoCandidate";
        private const string RecoveringEmergencyDataKey =
            "MAP_MechanoidMechanitor.Justice.ConsciousnessTransfer.RecoveringEmergencyData";
        private const string FailedKey = "MAP_MechanoidMechanitor.Justice.ConsciousnessTransfer.Failed";

        private const string HandoffLabelKey =
            "MAP_MechanoidMechanitor.Justice.ControlHandoff.Label";
        private const string HandoffDescriptionKey =
            "MAP_MechanoidMechanitor.Justice.ControlHandoff.Description";
        private const string HandoffNoCandidateKey =
            "MAP_MechanoidMechanitor.Justice.ControlHandoff.NoCandidate";
        private const string HandoffFailedKey =
            "MAP_MechanoidMechanitor.Justice.ControlHandoff.Failed";

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn mech)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            // 轨道数据网络研究完成后，所有合法机械族机械师（无需是宿主）显示“控制权交接”，
            // 取代原先的“意识转移”；研究前仍走下方原有意识转移分支。
            if (ShouldShowControlHandoffGizmo(mech))
            {
                yield return MakeControlHandoffCommand(mech);
                yield break;
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
                && ResearchFeatureUnlockUtility.IsMechanicalConsciousnessTransferUnlocked()
                && MechanoidMechanitorScenarioUtility.IsScenarioActive
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
                icon = ContentFinder<Texture2D>.Get("UI/Commands/MM_TransferMechanicalConsciousness"),
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

        private static bool ShouldShowControlHandoffGizmo(Pawn? mech)
        {
            return mech != null
                && !mech.Dead
                && !mech.Destroyed
                && mech.jobs != null
                && mech.Spawned
                && mech.Map != null
                && MechanicalControlHandoffUtility.IsValidControlHandoffParticipant(mech);
        }

        private static Command_Action MakeControlHandoffCommand(Pawn source)
        {
            List<Pawn> candidates = BuildControlHandoffTargets(source);
            Command_Action command = new Command_Action
            {
                defaultLabel = HandoffLabelKey.Translate(),
                defaultDesc = HandoffDescriptionKey.Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/MM_TransferMechanicalConsciousness"),
                action = delegate
                {
                    OpenControlHandoffTargetMenu(source);
                }
            };

            if (candidates.Count == 0)
            {
                command.Disable(HandoffNoCandidateKey.Translate());
            }

            return command;
        }

        private static void OpenControlHandoffTargetMenu(Pawn source)
        {
            List<FloatMenuOption> options = BuildControlHandoffFloatMenuOptions(source);
            if (options.Count == 0)
            {
                Messages.Message(
                    HandoffFailedKey.Translate(),
                    source,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<FloatMenuOption> BuildControlHandoffFloatMenuOptions(Pawn source)
        {
            List<Pawn> candidates = BuildControlHandoffTargets(source);
            List<FloatMenuOption> options = new List<FloatMenuOption>(candidates.Count);

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn target = candidates[i];
                Pawn localTarget = target;
                options.Add(new FloatMenuOption(
                    localTarget.LabelShortCap,
                    delegate
                    {
                        TryStartControlHandoffJob(source, localTarget);
                    }));
            }

            return options;
        }

        private static List<Pawn> BuildControlHandoffTargets(Pawn source)
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

                if (!MechanicalControlHandoffUtility.CanTransferControl(source, candidate))
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

        private static void TryStartControlHandoffJob(Pawn source, Pawn target)
        {
            if (source == null
                || target == null
                || source.jobs == null
                || !source.Spawned
                || source.Map == null
                || !MechanicalControlHandoffUtility.CanTransferControl(source, target))
            {
                Messages.Message(
                    HandoffFailedKey.Translate(),
                    source,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_TransferMechanitorControl,
                source,
                target);
            source.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
