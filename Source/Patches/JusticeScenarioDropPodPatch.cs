using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ScenPart_PlayerPawnsArriveMethod), "DoDropPods")]
    public static class JusticeScenarioDropPodPatch
    {
        private static readonly FieldInfo MethodField =
            AccessTools.Field(typeof(ScenPart_PlayerPawnsArriveMethod), "method");

        [HarmonyPrefix]
        public static bool Prefix(
            ScenPart_PlayerPawnsArriveMethod __instance,
            Map map,
            List<Thing> startingItems)
        {
            if (!JusticeScenarioUtility.IsJusticeScenarioActive)
            {
                return true;
            }

            GameInitData? initData = Find.GameInitData;
            if (initData == null || initData.startingAndOptionalPawns.Count != 0)
            {
                return true;
            }

            if (GetArriveMethod(__instance) != PlayerPawnsArriveMethod.DropPods)
            {
                return true;
            }

            Pawn? justice = FindJusticeInStartingItems(startingItems);
            if (justice == null)
            {
                Log.Error(
                    "[MechanoidMechanitor] Justice scenario drop pod handling aborted: " +
                    "no living MAP_Mech_Justice found in starting items.");
                return false;
            }

            startingItems.Remove(justice);

            List<Thing> justiceGroup = new List<Thing> { justice };

            foreach (Thing startingItem in startingItems)
            {
                if (startingItem.def.CanHaveFaction)
                {
                    startingItem.SetFactionDirect(Faction.OfPlayer);
                }

                justiceGroup.Add(startingItem);
            }

            startingItems.Clear();

            List<List<Thing>> dropGroups = new List<List<Thing>> { justiceGroup };
            bool openImmediately = initData.QuickStarted
                || GetArriveMethod(__instance) != PlayerPawnsArriveMethod.DropPods;

            DropPodUtility.DropThingGroupsNear(
                MapGenerator.PlayerStartSpot,
                map,
                dropGroups,
                110,
                openImmediately,
                leaveSlag: true,
                canRoofPunch: true,
                forbid: true,
                allowFogged: false);

            AssignStartingMechsToJustice(justice, justiceGroup);

            return false;
        }

        private static PlayerPawnsArriveMethod GetArriveMethod(ScenPart_PlayerPawnsArriveMethod instance)
        {
            return (PlayerPawnsArriveMethod)MethodField.GetValue(instance);
        }

        private static Pawn? FindJusticeInStartingItems(List<Thing> startingItems)
        {
            if (JusticeScenarioUtility.JusticePawnKind == null)
            {
                return null;
            }

            foreach (Thing item in startingItems)
            {
                if (item is Pawn pawn && !pawn.Dead && JusticeScenarioUtility.IsJustice(pawn))
                {
                    return pawn;
                }
            }

            return null;
        }

        private static void AssignStartingMechsToJustice(Pawn justice, List<Thing> justiceGroup)
        {
            VanillaRelayMechanitorUtility.EnsureVanillaRelayMechanitorState(justice);

            if (!MechanitorUtility.IsMechanitor(justice) || justice.mechanitor == null)
            {
                Log.Error(
                    "[MechanoidMechanitor] Justice scenario could not assign starting mech overseers: " +
                    "Justice is not a valid mechanitor.");
                return;
            }

            foreach (Thing item in justiceGroup)
            {
                if (item is not Pawn mech || mech == justice)
                {
                    continue;
                }

                if (mech.GetOverseer() != null)
                {
                    continue;
                }

                if (!justice.mechanitor.CanOverseeSubject(mech))
                {
                    continue;
                }

                justice.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            }
        }
    }
}
