using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(RitualBehaviorWorker_GravshipLaunch), nameof(RitualBehaviorWorker_GravshipLaunch.TryExecuteOn))]
    public static class GravshipLaunchBoarding_TryExecuteOn_Patch
    {
        public static bool Prefix(
            RitualBehaviorWorker_GravshipLaunch __instance,
            TargetInfo target,
            Pawn organizer,
            Precept_Ritual ritual,
            RitualObligation obligation,
            RitualRoleAssignments assignments,
            bool playerForced)
        {
            if (!ShouldHandle(target, ritual, assignments, out Building_GravEngine? engine))
            {
                return true;
            }

            if (__instance.CanStartRitualNow(target, ritual, null, assignments.ForcedRolesForReading) != null
                || !__instance.CanExecuteOn(target, obligation))
            {
                return false;
            }

            HashSet<Pawn> pilotCapableParticipants = CollectGravshipPilotCapableParticipants(assignments);

            engine!.pawnsToBoard = new HashSet<Pawn>();
            engine.pawnsToLeave = new HashSet<Pawn>();

            List<Pawn> tmpPawns = new List<Pawn>();
            tmpPawns.AddRange(target.Map.mapPawns.AllPawnsSpawned);

            foreach (Pawn tmpPawn in tmpPawns)
            {
                if (tmpPawn.Downed)
                {
                    continue;
                }

                if (pilotCapableParticipants.Contains(tmpPawn))
                {
                    continue;
                }

                if (__instance.forceVisitorsToLeave
                    && tmpPawn.Faction != null
                    && tmpPawn.Faction != Faction.OfPlayer
                    && !tmpPawn.Faction.HostileTo(Faction.OfPlayer))
                {
                    engine.pawnsToLeave.Add(tmpPawn);
                    tmpPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                else if (__instance.boardColonyAnimals && tmpPawn.IsColonyAnimal)
                {
                    engine.pawnsToBoard.Add(tmpPawn);
                    tmpPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                else if (ModsConfig.BiotechActive && __instance.boardColonyMechs && tmpPawn.IsColonyMech)
                {
                    engine.pawnsToBoard.Add(tmpPawn);
                    tmpPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }

            if (playerForced)
            {
                foreach (Pawn participant in assignments.Participants)
                {
                    RitualRole ritualRole = assignments.RoleForPawn(participant);
                    if (ritualRole == null
                        || !ritualRole.allowKeepLayingDown
                        || participant.GetPosture() != PawnPosture.LayingInBed)
                    {
                        participant.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    }
                }
            }

            foreach (Pawn participant in assignments.Participants)
            {
                if (participant.GetLord()?.LordJob is LordJob_VoluntarilyJoinable)
                {
                    participant.GetLord().Notify_PawnLost(participant, PawnLostCondition.LeftVoluntarily);
                }
            }

            LordJob_Ritual lordJob = new LordJob_Ritual(
                target,
                ritual,
                obligation,
                __instance.def.stages,
                assignments);

            LordMaker.MakeNewLord(
                Faction.OfPlayer,
                lordJob,
                target.Map,
                assignments.Participants.Where(p => lordJob.RoleFor(p)?.addToLord ?? true));

            ritual.outcomeEffect?.ResetCompDatas();
            lordJob.PreparePawns();

            AbilityGroupDef useCooldownFromAbilityGroupDef = ritual.def.useCooldownFromAbilityGroupDef;
            if (useCooldownFromAbilityGroupDef != null
                && useCooldownFromAbilityGroupDef.cooldownTicks > 0
                && !useCooldownFromAbilityGroupDef.ritualRoleIds.NullOrEmpty())
            {
                foreach (string ritualRoleId in useCooldownFromAbilityGroupDef.ritualRoleIds)
                {
                    if (!assignments.AnyPawnAssigned(ritualRoleId))
                    {
                        continue;
                    }

                    foreach (Pawn assignedPawn in assignments.AssignedPawns(ritualRoleId))
                    {
                        foreach (Ability ability in assignedPawn.abilities.AllAbilitiesForReading)
                        {
                            ability.Notify_GroupStartedCooldown(
                                useCooldownFromAbilityGroupDef,
                                useCooldownFromAbilityGroupDef.cooldownTicks);
                        }
                    }
                }

                ritual.Notify_CooldownFromAbilityStarted(useCooldownFromAbilityGroupDef.cooldownTicks);
            }

            Messages.Message(
                "RitualBegun".Translate(ritual.Label).CapitalizeFirst(),
                target,
                MessageTypeDefOf.NeutralEvent);

            return false;
        }

        private static bool ShouldHandle(
            TargetInfo target,
            Precept_Ritual ritual,
            RitualRoleAssignments assignments,
            out Building_GravEngine? engine)
        {
            engine = null;

            if (!ModsConfig.OdysseyActive)
            {
                return false;
            }

            if (ritual == null || ritual.def != PreceptDefOf.GravshipLaunch)
            {
                return false;
            }

            if (target.Thing == null)
            {
                return false;
            }

            engine = target.Thing.TryGetComp<CompPilotConsole>()?.engine;
            return engine != null && assignments != null;
        }

        private static HashSet<Pawn> CollectGravshipPilotCapableParticipants(RitualRoleAssignments assignments)
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
