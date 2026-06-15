using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    internal static class GravshipLaunchDiagnosticUtility
    {
        public const string LogPrefix = "[MAP-GravshipDiagnostic]";

        private static readonly MethodInfo? ShouldCallOffBecausePawnNoLongerOwnedMethod =
            AccessTools.Method(typeof(LordJob_Ritual), "ShouldCallOffBecausePawnNoLongerOwned");

        public static bool ShouldLog => Prefs.DevMode;

        public static bool IsGravshipLaunch(LordJob_Ritual? job)
        {
            return job?.Ritual?.def == PreceptDefOf.GravshipLaunch;
        }

        public static void WriteMessage(string message)
        {
            if (!ShouldLog)
            {
                return;
            }

            Verse.Log.Message($"{LogPrefix} {message}");
        }

        public static string PawnLabel(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "null";
            }

            return $"{pawn.LabelShort}({pawn.ThingID})";
        }

        public static string LordJobType(Pawn? pawn)
        {
            return pawn?.GetLord()?.LordJob?.GetType().Name ?? "null";
        }

        public static string JobDefName(Pawn? pawn)
        {
            return pawn?.CurJobDef?.defName ?? "null";
        }

        public static Building_GravEngine? GetEngine(TargetInfo target)
        {
            return target.Thing?.TryGetComp<CompPilotConsole>()?.engine;
        }

        public static IEnumerable<Pawn> FindJusticePawns(LordJob_Ritual job)
        {
            HashSet<Pawn> justicePawns = new HashSet<Pawn>();

            if (job.assignments != null)
            {
                foreach (Pawn pawn in job.assignments.Participants)
                {
                    if (CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
                    {
                        justicePawns.Add(pawn);
                    }
                }

                foreach (Pawn pawn in job.assignments.SpectatorsForReading)
                {
                    if (CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
                    {
                        justicePawns.Add(pawn);
                    }
                }
            }

            if (job.lord != null)
            {
                foreach (Pawn pawn in job.lord.ownedPawns)
                {
                    if (CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
                    {
                        justicePawns.Add(pawn);
                    }
                }
            }

            return justicePawns;
        }

        public static bool InvokeShouldCallOffBecausePawnNoLongerOwned(LordJob_Ritual job, Pawn pawn)
        {
            if (ShouldCallOffBecausePawnNoLongerOwnedMethod == null)
            {
                return false;
            }

            return (bool)ShouldCallOffBecausePawnNoLongerOwnedMethod.Invoke(job, new object[] { pawn })!;
        }

        public static void AppendEngineBoardingInfo(
            StringBuilder sb,
            Building_GravEngine? engine,
            Pawn? pilot,
            Pawn? copilot,
            IEnumerable<Pawn> justicePawns)
        {
            if (engine == null)
            {
                sb.AppendLine("G. engine: null");
                return;
            }

            int boardCount = engine.pawnsToBoard?.Count ?? 0;
            int leaveCount = engine.pawnsToLeave?.Count ?? 0;
            sb.AppendLine($"G. engine.pawnsToBoard.Count={boardCount}, pawnsToLeave.Count={leaveCount}");

            foreach (Pawn justice in justicePawns)
            {
                sb.AppendLine(
                    $"G. justice {PawnLabel(justice)}: inBoard={engine.pawnsToBoard?.Contains(justice) == true}, inLeave={engine.pawnsToLeave?.Contains(justice) == true}");
            }

            if (pilot != null)
            {
                sb.AppendLine(
                    $"G. pilot {PawnLabel(pilot)}: inBoard={engine.pawnsToBoard?.Contains(pilot) == true}, inLeave={engine.pawnsToLeave?.Contains(pilot) == true}");
            }

            if (copilot != null)
            {
                sb.AppendLine(
                    $"G. copilot {PawnLabel(copilot)}: inBoard={engine.pawnsToBoard?.Contains(copilot) == true}, inLeave={engine.pawnsToLeave?.Contains(copilot) == true}");
            }
        }
    }

    [HarmonyPatch(typeof(LordJob_Ritual), nameof(LordJob_Ritual.ApplyOutcome))]
    public static class GravshipLaunchDiagnostics_ApplyOutcome_Patch
    {
        public static void Prefix(
            LordJob_Ritual __instance,
            float progress,
            bool showFinishedMessage,
            bool showFailedMessage,
            bool cancelled)
        {
            if (!GravshipLaunchDiagnosticUtility.ShouldLog
                || !GravshipLaunchDiagnosticUtility.IsGravshipLaunch(__instance))
            {
                return;
            }

            RitualRoleAssignments? assignments = __instance.assignments;
            Lord? lord = __instance.lord;
            TargetInfo selectedTarget = __instance.selectedTarget;
            Pawn? pilot = assignments?.FirstAssignedPawn("pilot");
            Pawn? copilot = assignments?.FirstAssignedPawn("copilot");
            Building_GravEngine? engine = GravshipLaunchDiagnosticUtility.GetEngine(selectedTarget);
            bool targetStillAllowed = __instance.TargetStillAllowed();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ApplyOutcome");
            sb.AppendLine($"progress={progress}");
            sb.AppendLine($"showFinishedMessage={showFinishedMessage}");
            sb.AppendLine($"showFailedMessage={showFailedMessage}");
            sb.AppendLine($"cancelled={cancelled}");
            sb.AppendLine($"StageIndex={__instance.StageIndex}");
            sb.AppendLine($"TicksPassedWithProgress={__instance.TicksPassedWithProgress}");
            sb.AppendLine($"TicksLeft={__instance.TicksLeft}");
            sb.AppendLine($"participants.Count={assignments?.Participants.Count ?? 0}");
            sb.AppendLine($"spectators.Count={assignments?.SpectatorsForReading.Count ?? 0}");
            sb.AppendLine($"ownedPawns.Count={lord?.ownedPawns.Count ?? 0}");
            sb.AppendLine($"TargetStillAllowed={targetStillAllowed}");
            sb.AppendLine($"selectedTarget.Thing={selectedTarget.Thing?.LabelShort ?? "null"}");
            sb.AppendLine($"selectedTarget.ThingDestroyed={selectedTarget.ThingDestroyed}");
            sb.AppendLine($"pilot={GravshipLaunchDiagnosticUtility.PawnLabel(pilot)}");
            sb.AppendLine($"copilot={GravshipLaunchDiagnosticUtility.PawnLabel(copilot)}");
            sb.AppendLine($"pilotInOwnedPawns={pilot != null && lord != null && lord.ownedPawns.Contains(pilot)}");
            sb.AppendLine($"copilotInOwnedPawns={copilot != null && lord != null && lord.ownedPawns.Contains(copilot)}");

            foreach (Pawn justice in GravshipLaunchDiagnosticUtility.FindJusticePawns(__instance))
            {
                sb.AppendLine($"justice={GravshipLaunchDiagnosticUtility.PawnLabel(justice)}");
                sb.AppendLine($"  inParticipants={assignments?.Participants.Contains(justice) == true}");
                sb.AppendLine($"  inSpectators={assignments?.SpectatorsForReading.Contains(justice) == true}");
                sb.AppendLine($"  inOwnedPawns={lord != null && lord.ownedPawns.Contains(justice)}");
                sb.AppendLine($"  job={GravshipLaunchDiagnosticUtility.JobDefName(justice)}");
                sb.AppendLine($"  lordJob={GravshipLaunchDiagnosticUtility.LordJobType(justice)}");
            }

            GravshipLaunchDiagnosticUtility.AppendEngineBoardingInfo(sb, engine, pilot, copilot, GravshipLaunchDiagnosticUtility.FindJusticePawns(__instance));
            GravshipLaunchDiagnosticUtility.WriteMessage(sb.ToString());
        }
    }

    [HarmonyPatch(typeof(LordJob_Ritual), "ShouldBeCalledOff")]
    public static class GravshipLaunchDiagnostics_ShouldBeCalledOff_Patch
    {
        public static void Postfix(LordJob_Ritual __instance, ref bool __result)
        {
            if (!GravshipLaunchDiagnosticUtility.ShouldLog
                || !__result
                || !GravshipLaunchDiagnosticUtility.IsGravshipLaunch(__instance))
            {
                return;
            }

            RitualRoleAssignments? assignments = __instance.assignments;
            Lord? lord = __instance.lord;
            TargetInfo selectedTarget = __instance.selectedTarget;
            Pawn? pilot = assignments?.FirstAssignedPawn("pilot");
            Pawn? copilot = assignments?.FirstAssignedPawn("copilot");
            Building_GravEngine? engine = GravshipLaunchDiagnosticUtility.GetEngine(selectedTarget);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ShouldBeCalledOff returned true. Evaluating reasons:");

            bool ownedEmpty = lord == null || lord.ownedPawns.Count == 0;
            sb.AppendLine($"A. ownedPawns.Count==0: {ownedEmpty} (count={lord?.ownedPawns.Count ?? 0})");

            bool targetAllowed = __instance.TargetStillAllowed();
            sb.AppendLine($"B. !TargetStillAllowed: {!targetAllowed} (TargetStillAllowed={targetAllowed})");
            sb.AppendLine($"   selectedTarget.Thing={selectedTarget.Thing?.LabelShort ?? "null"}");
            sb.AppendLine($"   selectedTarget.ThingDestroyed={selectedTarget.ThingDestroyed}");
            sb.AppendLine($"   selectedTarget.Cell={selectedTarget.Cell}");
            sb.AppendLine($"   selectedTarget.Map={(selectedTarget.Map == null ? "null" : selectedTarget.Map.ToString())}");

            Pawn? organizer = __instance.Organizer;
            bool organizerDowned = organizer != null && organizer.Downed;
            sb.AppendLine($"C. organizer downed: {organizerDowned}");
            sb.AppendLine($"   organizer={GravshipLaunchDiagnosticUtility.PawnLabel(organizer)}");
            sb.AppendLine($"   organizer.Downed={organizer?.Downed}");

            sb.AppendLine("D. required roles:");
            if (assignments != null)
            {
                foreach (RitualRole role in assignments.AllRolesForReading)
                {
                    if (!role.required)
                    {
                        continue;
                    }

                    Pawn? pawn = assignments.FirstAssignedPawn(role);
                    bool inOwned = pawn != null && lord != null && lord.ownedPawns.Contains(pawn);
                    bool shouldCallOff = pawn != null
                        && lord != null
                        && !lord.ownedPawns.Contains(pawn)
                        && GravshipLaunchDiagnosticUtility.InvokeShouldCallOffBecausePawnNoLongerOwned(__instance, pawn);

                    sb.AppendLine(
                        $"   role={role.id}/{role.Label}: pawn={GravshipLaunchDiagnosticUtility.PawnLabel(pawn)}, lordJob={GravshipLaunchDiagnosticUtility.LordJobType(pawn)}, inOwnedPawns={inOwned}, shouldCallOff={shouldCallOff}");

                    if (pawn != null)
                    {
                        sb.AppendLine(
                            $"     dead={pawn.Dead}, discarded={pawn.Discarded}, spawned={pawn.Spawned}, map={(pawn.Map == null ? "null" : pawn.Map.ToString())}");
                    }
                }
            }

            sb.AppendLine("E. extra required pawns:");
            if (assignments != null && lord != null)
            {
                foreach (Pawn extraPawn in assignments.ExtraRequiredPawnsForReading)
                {
                    bool inOwned = lord.ownedPawns.Contains(extraPawn);
                    bool shouldCallOff = !inOwned
                        && GravshipLaunchDiagnosticUtility.InvokeShouldCallOffBecausePawnNoLongerOwned(__instance, extraPawn);

                    sb.AppendLine(
                        $"   pawn={GravshipLaunchDiagnosticUtility.PawnLabel(extraPawn)}, inOwnedPawns={inOwned}, shouldCallOff={shouldCallOff}, carriedBy={extraPawn.CarriedBy}, corpse={extraPawn.Corpse != null}");

                    IThingHolder? holder = extraPawn.Corpse?.ParentHolder ?? extraPawn.CarriedBy;
                    if (holder is Pawn carrier)
                    {
                        sb.AppendLine($"     holderLord={carrier.GetLord()?.LordJob?.GetType().Name ?? "null"}");
                    }
                }
            }

            sb.AppendLine("F. participants self-defense:");
            if (assignments != null)
            {
                foreach (Pawn participant in assignments.Participants)
                {
                    if (participant == null)
                    {
                        continue;
                    }

                    RitualRole? ritualRole = assignments.RoleForPawn(participant);
                    bool relevant = (ritualRole != null && ritualRole.required)
                        || assignments.Required(participant)
                        || assignments.ExtraRequiredPawnsForReading.Contains(participant);

                    if (!relevant || participant.Map == null)
                    {
                        continue;
                    }

                    bool fleeing = SelfDefenseUtility.ShouldStartFleeing(participant);
                    sb.AppendLine(
                        $"   pawn={GravshipLaunchDiagnosticUtility.PawnLabel(participant)}, fleeing={fleeing}, map={(participant.Map == null ? "null" : participant.Map.ToString())}, pos={participant.Position}, job={GravshipLaunchDiagnosticUtility.JobDefName(participant)}, lordJob={GravshipLaunchDiagnosticUtility.LordJobType(participant)}");
                }
            }

            GravshipLaunchDiagnosticUtility.AppendEngineBoardingInfo(sb, engine, pilot, copilot, GravshipLaunchDiagnosticUtility.FindJusticePawns(__instance));
            GravshipLaunchDiagnosticUtility.WriteMessage(sb.ToString());
        }
    }

    [HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnLost))]
    public static class GravshipLaunchDiagnostics_NotifyPawnLost_Patch
    {
        public static void Prefix(
            Lord __instance,
            Pawn pawn,
            PawnLostCondition cond,
            DamageInfo? dinfo)
        {
            if (!GravshipLaunchDiagnosticUtility.ShouldLog
                || __instance.LordJob is not LordJob_Ritual ritualJob
                || !GravshipLaunchDiagnosticUtility.IsGravshipLaunch(ritualJob))
            {
                return;
            }

            RitualRole? role = ritualJob.assignments?.RoleForPawn(pawn);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Notify_PawnLost");
            sb.AppendLine($"pawn={GravshipLaunchDiagnosticUtility.PawnLabel(pawn)}");
            sb.AppendLine($"condition={cond}");
            sb.AppendLine($"pawnLordJob={GravshipLaunchDiagnosticUtility.LordJobType(pawn)}");
            sb.AppendLine($"pawnJob={GravshipLaunchDiagnosticUtility.JobDefName(pawn)}");
            sb.AppendLine($"isJustice={CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn)}");
            sb.AppendLine($"roleId={role?.id ?? "null"}");
            sb.AppendLine($"spectating={ritualJob.assignments?.PawnSpectating(pawn) == true}");
            sb.AppendLine($"ownedPawns.Count(before)={__instance.ownedPawns.Count}");
            sb.AppendLine($"ownedPawns.Contains(pawn)={__instance.ownedPawns.Contains(pawn)}");
            GravshipLaunchDiagnosticUtility.WriteMessage(sb.ToString());
        }
    }
}
