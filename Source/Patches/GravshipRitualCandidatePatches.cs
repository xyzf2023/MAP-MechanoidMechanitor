using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.CreateRitualRoleAssignments))]
    public static class GravshipRitualRoleAssignments_CreateRitualRoleAssignments_Patch
    {
        public static void Postfix(
            Precept_Ritual ritual,
            TargetInfo target,
            Map map,
            Dialog_BeginRitual.PawnFilter filter,
            List<Pawn> requiredPawns,
            Dictionary<string, Pawn> forcedForRole,
            Pawn selectedPawn,
            ref RitualRoleAssignments __result)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (!IsGravshipLaunch(ritual))
            {
                return;
            }

            if (!IsPilotConsoleTarget(target))
            {
                return;
            }

            if (map == null || __result == null)
            {
                return;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!IsCandidate(pawn))
                {
                    continue;
                }

                if (filter != null && !filter(pawn, true, true))
                {
                    continue;
                }

                __result.AllCandidatePawns.AddUnique(pawn);
            }
        }

        private static bool IsGravshipLaunch(Precept_Ritual ritual)
        {
            return ritual != null && ritual.def == PreceptDefOf.GravshipLaunch;
        }

        private static bool IsPilotConsoleTarget(TargetInfo target)
        {
            return target.Thing != null && target.Thing.TryGetComp<CompPilotConsole>() != null;
        }

        private static bool IsCandidate(Pawn pawn)
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

            if (pawn.skills == null || pawn.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled)
            {
                return false;
            }

            return true;
        }
    }
}
