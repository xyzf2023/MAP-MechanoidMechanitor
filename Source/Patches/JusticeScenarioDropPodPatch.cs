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

            AssignAllStartingMechsToJustice(justice, justiceGroup);

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

        private static bool EnsureJusticeMechanitorState(Pawn justice)
        {
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(justice);

            if (justice.relations == null
                || justice.mechanitor == null
                || !MechanitorUtility.IsMechanitor(justice)
                || justice.mechanitor.controlGroups == null
                || justice.mechanitor.controlGroups.Count == 0)
            {
                Log.Error(
                    "[MechanoidMechanitor] Justice scenario could not initialize Justice mechanitor state. " +
                    "Overseer assignment skipped; drop pods will still proceed.");
                return false;
            }

            return true;
        }

        private static bool IsOverseeCandidate(Pawn mech, Pawn justice)
        {
            if (mech == justice || mech.Dead)
            {
                return false;
            }

            if (!mech.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (mech.OverseerSubject == null)
            {
                return false;
            }

            if (MAPMechanitorNodeUtility.IsMechanitorNodeController(mech))
            {
                return false;
            }

            return true;
        }

        private static void AssignAllStartingMechsToJustice(Pawn justice, List<Thing> justiceGroup)
        {
            if (!EnsureJusticeMechanitorState(justice))
            {
                return;
            }

            if (justice.relations == null)
            {
                justice.relations = new Pawn_RelationsTracker(justice);
            }

            foreach (Thing item in justiceGroup)
            {
                if (item is not Pawn mech || !IsOverseeCandidate(mech, justice))
                {
                    continue;
                }

                Pawn? existingOverseer = mech.GetOverseer();
                if (existingOverseer == justice)
                {
                    continue;
                }

                if (existingOverseer != null)
                {
                    existingOverseer.relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
                }

                if (mech.relations == null)
                {
                    mech.relations = new Pawn_RelationsTracker(mech);
                }

                if (!justice.mechanitor.CanOverseeSubject(mech))
                {
                    Log.Warning(
                        "[MechanoidMechanitor] Justice scenario could not assign overseer to " +
                        $"{mech.LabelShort} ({mech.kindDef?.defName ?? "unknown"}): insufficient bandwidth or incompatible subject.");
                    continue;
                }

                justice.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            }
        }
    }
}
