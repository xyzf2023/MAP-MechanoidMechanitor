using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.AnyAutofeeder))]
    public static class Patch_Pawn_MindState_AnyAutofeeder_MechanicalChildcare
    {
        [HarmonyPrefix]
        public static void Prefix(List<Pawn>? possibleFeeders, ref bool __state)
        {
            __state = possibleFeeders == null;
        }

        [HarmonyPostfix]
        public static void Postfix(
            Pawn_MindState __instance,
            AutofeedMode autofeed,
            Predicate<Pawn, Pawn> feederPredicate,
            bool __state,
            ref bool __result)
        {
            if (__result
                || !__state
                || autofeed != AutofeedMode.Childcare
                || feederPredicate == null)
            {
                return;
            }

            Pawn? baby = __instance.pawn;
            Map? map = baby?.MapHeld;
            if (baby == null || map == null)
            {
                return;
            }

            IReadOnlyList<Pawn> spawnedPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn feeder = spawnedPawns[i];
                if (!IsMechanicalChildcareFeederCandidate(baby, feeder))
                {
                    continue;
                }

                if (autofeed != __instance.AutofeedSetting(feeder))
                {
                    continue;
                }

                if (feederPredicate(baby, feeder))
                {
                    __result = true;
                    return;
                }
            }
        }

        private static bool IsMechanicalChildcareFeederCandidate(Pawn? baby, Pawn? feeder)
        {
            if (feeder == null || baby == null || ReferenceEquals(feeder, baby))
            {
                return false;
            }

            if (feeder.Dead || feeder.Downed)
            {
                return false;
            }

            if (feeder.MapHeld != baby.MapHeld)
            {
                return false;
            }

            if (!MatchesFreeHumanlikeFactionScope(baby, feeder))
            {
                return false;
            }

            if (!MechanicalChildcareUtility.IsAuthorized(feeder))
            {
                return false;
            }

            WorkTypeDef? childcare = MechanicalChildcareUtility.ChildcareWorkType;
            if (childcare == null
                || feeder.WorkTypeIsDisabled(childcare)
                || feeder.workSettings == null
                || !feeder.workSettings.WorkIsActive(childcare))
            {
                return false;
            }

            return true;
        }

        private static bool MatchesFreeHumanlikeFactionScope(Pawn baby, Pawn feeder)
        {
            if (feeder.Faction != baby.Faction)
            {
                return false;
            }

            if (ModsConfig.AnomalyActive && feeder.IsSubhuman)
            {
                return false;
            }

            return feeder.HostFaction == null || feeder.IsSlave;
        }
    }
}
