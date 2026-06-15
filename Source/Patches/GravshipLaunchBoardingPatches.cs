using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
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

            HashSet<Pawn> justiceParticipants = CollectGravshipJusticeParticipants(assignments);

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
