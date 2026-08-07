using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(CompAssignableToPawn_Throne),
        nameof(CompAssignableToPawn_Throne.AssigningCandidates),
        MethodType.Getter)]
    public static class Patch_CompAssignableToPawn_Throne_AssigningCandidates_Mechanitor
    {
        [HarmonyPostfix]
        public static void Postfix(
            CompAssignableToPawn_Throne __instance,
            ref IEnumerable<Pawn> __result)
        {
            if (!ModsConfig.RoyaltyActive)
            {
                return;
            }

            List<Pawn> candidates =
                __result?.ToList() ?? new List<Pawn>();

            List<Pawn> available =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            for (int i = 0; i < available.Count; i++)
            {
                Pawn pawn = available[i];
                if (!MechanoidMechanitorRoyaltyUtility
                        .IsRoyaltyEligibleMechanitor(pawn)
                    || pawn.royalty == null
                    || (!pawn.royalty.AllTitlesForReading.Any()
                        && !pawn.royalty.CanUpdateTitleOfAnyFaction(
                            out _))
                    || candidates.Contains(pawn))
                {
                    continue;
                }

                candidates.Add(pawn);
            }

            __result = candidates
                .OrderByDescending(
                    pawn => __instance.CanAssignTo(pawn).Accepted)
                .ToList();
        }
    }

    [HarmonyPatch]
    public static class Patch_CompAssignableToPawn_Throne_CanSetUninstallAssignedPawn_Mechanitor
    {
        private static MethodBase? TargetMethod()
        {
            return AccessTools.Method(
                typeof(CompAssignableToPawn_Throne),
                "CanSetUninstallAssignedPawn");
        }

        [HarmonyPostfix]
        public static void Postfix(
            CompAssignableToPawn_Throne __instance,
            Pawn pawn,
            ref bool __result)
        {
            if (__result
                || !MechanoidMechanitorRoyaltyUtility
                    .IsRoyaltyEligibleMechanitor(pawn))
            {
                return;
            }

            if (!__instance.AssignedAnything(pawn)
                && __instance.CanAssignTo(pawn).Accepted)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(
        typeof(Alert_RoyalNoThroneAssigned),
        nameof(Alert_RoyalNoThroneAssigned.Targets),
        MethodType.Getter)]
    public static class Patch_Alert_RoyalNoThroneAssigned_Targets_Mechanitor
    {
        [HarmonyPostfix]
        public static void Postfix(ref List<Pawn> __result)
        {
            if (!ModsConfig.RoyaltyActive)
            {
                return;
            }

            __result ??= new List<Pawn>();

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                IReadOnlyList<Pawn> pawns =
                    maps[i].mapPawns.AllPawnsSpawned;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn pawn = pawns[j];
                    if (!MechanoidMechanitorRoyaltyUtility
                            .IsRoyaltyEligibleMechanitor(pawn)
                        || pawn.royalty == null
                        || pawn.Suspended
                        || !pawn.royalty.CanRequireThroneroom()
                        || pawn.ownership.AssignedThrone != null
                        || __result.Contains(pawn))
                    {
                        continue;
                    }

                    bool hasThroneRequirement = pawn.royalty
                        .AllTitlesForReading
                        .Any(
                            title => !title.def
                                .throneRoomRequirements
                                .NullOrEmpty());
                    if (hasThroneRequirement)
                    {
                        __result.Add(pawn);
                    }
                }
            }
        }
    }
}
