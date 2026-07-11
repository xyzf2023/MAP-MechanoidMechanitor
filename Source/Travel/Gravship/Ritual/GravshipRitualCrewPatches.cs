using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class GravshipRitualCrewUtility
    {
        public static bool IsGravshipLaunch(RitualRoleAssignments? assignments)
        {
            return assignments?.Ritual != null
                && assignments.Ritual.def == PreceptDefOf.GravshipLaunch;
        }

        public static bool IsPilotConsoleTarget(TargetInfo target)
        {
            return target.Thing != null && target.Thing.TryGetComp<CompPilotConsole>() != null;
        }

        public static bool IsGravshipCrewCandidate(Pawn pawn)
        {
            if (!CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (pawn.GuestStatus == GuestStatus.Prisoner)
            {
                return false;
            }

            if (pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                return false;
            }

            return true;
        }

        public static bool CanUseAsGravshipCrew(
            RitualRoleAssignments assignments,
            Pawn pawn,
            TargetInfo ritualTarget)
        {
            if (!IsGravshipLaunch(assignments)
                || !IsPilotConsoleTarget(ritualTarget)
                || !IsGravshipCrewCandidate(pawn))
            {
                return false;
            }

            Precept_Ritual ritual = assignments.Ritual;

            if (ritual.ritualOnlyForIdeoMembers
                && pawn.Ideo != ritual.ideo
                && !ritual.def.allowSpectatorsFromOtherIdeos)
            {
                return false;
            }

            if (ritual.behavior.def.spectatorFilter != null
                && !ritual.behavior.def.spectatorFilter.Allowed(pawn))
            {
                return false;
            }

            if (!ritual.behavior.PawnCanFillRole(pawn, null, out _, ritualTarget))
            {
                return false;
            }

            if (pawn.IsPrisoner)
            {
                return false;
            }

            return GatheringsUtility.ShouldPawnKeepAttendingRitual(
                pawn,
                ritual,
                ritual.behavior.def.spectatorsIgnoreBleeding);
        }
    }

    [HarmonyPatch(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.CanEverSpectate))]
    public static class GravshipRitualCrew_CanEverSpectate_Patch
    {
        public static void Postfix(
            RitualRoleAssignments __instance,
            Pawn pawn,
            TargetInfo ___ritualTarget,
            ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (GravshipRitualCrewUtility.CanUseAsGravshipCrew(__instance, pawn, ___ritualTarget))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class GravshipRitualCrew_PawnNotAssignableReason_Patch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.DeclaredMethod(
                typeof(RitualRoleAssignments),
                nameof(RitualRoleAssignments.PawnNotAssignableReason),
                new[] { typeof(Pawn), typeof(RitualRole), typeof(bool).MakeByRefType() });
        }

        public static void Postfix(
            RitualRoleAssignments __instance,
            Pawn p,
            RitualRole role,
            TargetInfo ___ritualTarget,
            ref string? __result,
            ref bool stillAddToPawnList)
        {
            if (role != null || __result == null)
            {
                return;
            }

            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (!GravshipRitualCrewUtility.IsGravshipLaunch(__instance)
                || !GravshipRitualCrewUtility.IsPilotConsoleTarget(___ritualTarget)
                || !GravshipRitualCrewUtility.IsGravshipCrewCandidate(p))
            {
                return;
            }

            if (GravshipRitualCrewUtility.CanUseAsGravshipCrew(__instance, p, ___ritualTarget))
            {
                __result = null;
                stillAddToPawnList = false;
            }
        }
    }
}
