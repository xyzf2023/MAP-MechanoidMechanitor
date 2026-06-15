using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Text;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(RitualBehaviorWorker_GravshipLaunch), nameof(RitualBehaviorWorker_GravshipLaunch.TryExecuteOn))]
    public static class GravshipLaunchBoarding_TryExecuteOn_Patch
    {
        public static void Postfix(
            TargetInfo target,
            Pawn organizer,
            Precept_Ritual ritual,
            RitualObligation obligation,
            RitualRoleAssignments assignments,
            bool playerForced)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (ritual == null || ritual.def != PreceptDefOf.GravshipLaunch)
            {
                return;
            }

            if (target.Thing == null)
            {
                return;
            }

            CompPilotConsole? console = target.Thing.TryGetComp<CompPilotConsole>();
            Building_GravEngine? engine = console?.engine;
            if (engine == null || assignments == null)
            {
                return;
            }

            int boardCountBefore = engine.pawnsToBoard?.Count ?? 0;
            int leaveCountBefore = engine.pawnsToLeave?.Count ?? 0;
            HashSet<Pawn> justiceParticipants = CollectGravshipJusticeParticipants(assignments);

            if (GravshipLaunchDiagnosticUtility.ShouldLog)
            {
                LogTryExecuteOnDiagnostics(assignments, engine, boardCountBefore, leaveCountBefore, justiceParticipants);
            }

            foreach (Pawn pawn in justiceParticipants)
            {
                if (!CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
                {
                    continue;
                }

                engine.pawnsToBoard?.Remove(pawn);
                engine.pawnsToLeave?.Remove(pawn);

                if (pawn.jobs?.curJob?.def == JobDefOf.GotoShip)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }

            if (GravshipLaunchDiagnosticUtility.ShouldLog)
            {
                LogTryExecuteOnPostRemoval(engine, justiceParticipants, boardCountBefore, leaveCountBefore);
            }
        }

        private static void LogTryExecuteOnDiagnostics(
            RitualRoleAssignments assignments,
            Building_GravEngine engine,
            int boardCountBefore,
            int leaveCountBefore,
            HashSet<Pawn> justiceParticipants)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TryExecuteOn Postfix running");

            sb.AppendLine("assignments.Participants:");
            foreach (Pawn pawn in assignments.Participants)
            {
                RitualRole? role = assignments.RoleForPawn(pawn);
                sb.AppendLine(
                    $"  {GravshipLaunchDiagnosticUtility.PawnLabel(pawn)} role={role?.id ?? "null"} spectator={assignments.PawnSpectating(pawn)} justice={CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn)}");
            }

            sb.AppendLine("assignments.SpectatorsForReading:");
            foreach (Pawn pawn in assignments.SpectatorsForReading)
            {
                sb.AppendLine(
                    $"  {GravshipLaunchDiagnosticUtility.PawnLabel(pawn)} justice={CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn)}");
            }

            sb.AppendLine($"engine.pawnsToBoard(before)={boardCountBefore}");
            sb.AppendLine($"engine.pawnsToLeave(before)={leaveCountBefore}");
            sb.AppendLine($"justiceParticipantsToRemove.Count={justiceParticipants.Count}");

            foreach (Pawn pawn in justiceParticipants)
            {
                sb.AppendLine($"  removeCandidate={GravshipLaunchDiagnosticUtility.PawnLabel(pawn)}");
            }

            GravshipLaunchDiagnosticUtility.WriteMessage(sb.ToString());
        }

        private static void LogTryExecuteOnPostRemoval(
            Building_GravEngine engine,
            HashSet<Pawn> justiceParticipants,
            int boardCountBefore,
            int leaveCountBefore)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TryExecuteOn Postfix after removal");
            sb.AppendLine($"engine.pawnsToBoard(after)={engine.pawnsToBoard?.Count ?? 0} (before={boardCountBefore})");
            sb.AppendLine($"engine.pawnsToLeave(after)={engine.pawnsToLeave?.Count ?? 0} (before={leaveCountBefore})");

            foreach (Pawn pawn in justiceParticipants)
            {
                sb.AppendLine(
                    $"  {GravshipLaunchDiagnosticUtility.PawnLabel(pawn)} job={GravshipLaunchDiagnosticUtility.JobDefName(pawn)} lordJob={GravshipLaunchDiagnosticUtility.LordJobType(pawn)}");
            }

            GravshipLaunchDiagnosticUtility.WriteMessage(sb.ToString());
        }

        private static HashSet<Pawn> CollectGravshipJusticeParticipants(RitualRoleAssignments assignments)
        {
            HashSet<Pawn> participants = new HashSet<Pawn>();

            foreach (Pawn pawn in assignments.Participants)
            {
                if (CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
                {
                    participants.Add(pawn);
                }
            }

            foreach (Pawn pawn in assignments.SpectatorsForReading)
            {
                if (CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
                {
                    participants.Add(pawn);
                }
            }

            Pawn pilot = assignments.FirstAssignedPawn("pilot");
            if (pilot != null && CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pilot))
            {
                participants.Add(pilot);
            }

            Pawn copilot = assignments.FirstAssignedPawn("copilot");
            if (copilot != null && CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(copilot))
            {
                participants.Add(copilot);
            }

            return participants;
        }
    }
}
