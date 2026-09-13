using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体入口。由具有先天合体资格的机械族发起，目标必须是其当前合法的
    /// 玩家阵营人类监管者；实际执行统一进入延迟队列与统一事务。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class MechFusionGizmoPatch
    {
        public static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result,
            Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                    __instance,
                    out MechFusionSession? session)
                && session != null)
            {
                yield return new Gizmo_MechFusionBar(
                    session,
                    MechFusionBarKind.Energy);
                if (session.MaxStability > 0f)
                {
                    yield return new Gizmo_MechFusionBar(
                        session,
                        MechFusionBarKind.Stability);
                }

                yield return BuildManualReleaseCommand(__instance, session);
            }

            if (!ShouldShowFor(__instance))
            {
                yield break;
            }

            yield return BuildCommand(__instance);
        }

        private static bool ShouldShowFor(Pawn pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Discarded
                && !pawn.Dead
                && pawn.Spawned
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && MechFusionEligibilityUtility.HasFusionEligibility(pawn)
                && MechTransformationUtility.IsInPawnForm(pawn)
                && !MechTransformationUtility.IsTransitionInProgress(pawn)
                && !GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(
                    pawn,
                    out _);
        }

        private static Command BuildCommand(Pawn source)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel =
                    "MAP_MechanoidMechanitor.Fusion.Gizmo.Label".Translate(),
                defaultDesc =
                    "MAP_MechanoidMechanitor.Fusion.Gizmo.Description".Translate(),
                icon = TexCommand.Install,
                action = delegate
                {
                    OpenTargetMenu(source);
                }
            };

            List<FloatMenuOption> options = BuildTargetOptions(source);
            if (options.Count == 0)
            {
                command.Disable(
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                        .Translate());
            }

            return command;
        }

        private static Command BuildManualReleaseCommand(
            Pawn wearer,
            MechFusionSession session)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel =
                    "MAP_MechanoidMechanitor.Fusion.Release.Label".Translate(),
                defaultDesc =
                    "MAP_MechanoidMechanitor.Fusion.Release.Description"
                        .Translate(),
                icon = TexCommand.ReleaseAnimals,
                action = delegate
                {
                    MechFusionTeardownService.TryTeardown(
                        session,
                        MechFusionExitReason.Manual,
                        force: false);
                }
            };

            if (session.IsEnding)
            {
                command.Disable(
                    "MAP_MechanoidMechanitor.Fusion.Release.InProgress"
                        .Translate());
            }
            else if (MechanicalFlightUtility.IsAirborne(wearer))
            {
                command.Disable(
                    "MAP_MechanoidMechanitor.Fusion.Release.Flying".Translate());
            }

            return command;
        }

        private static void OpenTargetMenu(Pawn source)
        {
            List<FloatMenuOption> options = BuildTargetOptions(source);
            if (options.Count == 0)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.Fusion.NoTarget".Translate(),
                    source,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<FloatMenuOption> BuildTargetOptions(Pawn source)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            Pawn? wearer = ResolveOverseerCandidate(source);
            if (wearer == null
                || !MechFusionValidator.CanStart(source, wearer, out _))
            {
                return options;
            }

            options.Add(new FloatMenuOption(
                wearer.LabelShortCap,
                delegate
                {
                    if (!GameComponent_MechFusionSessionRegistry.TryQueueStart(
                            source,
                            wearer,
                            sendFailureMessage: true,
                            out string? queueFailure)
                        && !string.IsNullOrEmpty(queueFailure))
                    {
                        Messages.Message(
                            queueFailure!,
                            source,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                }));
            return options;
        }

        /// <summary>
        /// 合法目标只能是 source 当前的人类监管者。优先直接读取 GetOverseer；
        /// 只有监管者关系无法由此反映时才走不遍历全地图的关系入口。
        /// </summary>
        private static Pawn? ResolveOverseerCandidate(Pawn source)
        {
            Pawn? overseer = source.GetOverseer();
            if (overseer != null)
            {
                return overseer;
            }

            return MAPOverseerRelationDirectionUtility.FindActualOverseer(source);
        }
    }
}
