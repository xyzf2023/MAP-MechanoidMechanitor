using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ScenPart_PlayerPawnsArriveMethod), "DoDropPods")]
    public static class JusticeScenarioDropPodPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            PlayerPawnsArriveMethod ___method,
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

            if (___method != PlayerPawnsArriveMethod.DropPods)
            {
                return true;
            }

            Pawn? scenarioMechanitor = FindScenarioMechanitorInStartingItems(startingItems);
            if (scenarioMechanitor == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 剧本运输舱处理中止：起始物品中未找到有效的机械意识宿主。");
                return false;
            }

            GameComponent_MechanoidMechanitorRegistry.RegisterScenarioPawn(
                scenarioMechanitor,
                promoteIfNeeded: true);
            startingItems.Remove(scenarioMechanitor);

            List<Thing> mechanitorGroup = new List<Thing> { scenarioMechanitor };
            foreach (Thing startingItem in startingItems)
            {
                if (startingItem.def.CanHaveFaction)
                {
                    startingItem.SetFactionDirect(Faction.OfPlayer);
                }

                mechanitorGroup.Add(startingItem);
            }

            startingItems.Clear();
            AssignAllStartingMechsToMechanitor(scenarioMechanitor, mechanitorGroup);

            List<List<Thing>> dropGroups = new List<List<Thing>> { mechanitorGroup };
            bool openImmediately = initData.QuickStarted
                || ___method != PlayerPawnsArriveMethod.DropPods;

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

        private static Pawn? FindScenarioMechanitorInStartingItems(List<Thing> startingItems)
        {
            Pawn? registered = JusticeScenarioUtility.MechanicalConsciousnessHost;
            if (registered != null && startingItems.Contains(registered) && !registered.Dead)
            {
                return registered;
            }

            for (int i = 0; i < startingItems.Count; i++)
            {
                if (startingItems[i] is Pawn pawn
                    && !pawn.Dead
                    && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
                {
                    return pawn;
                }
            }

            return null;
        }

        private static bool EnsureScenarioMechanitorState(Pawn mechanitor)
        {
            MechanoidMechanitorRoleUtility.EnsureRoleState(mechanitor);
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(mechanitor);

            if (mechanitor.relations == null
                || mechanitor.mechanitor == null
                || !MechanitorUtility.IsMechanitor(mechanitor)
                || mechanitor.mechanitor.controlGroups == null
                || mechanitor.mechanitor.controlGroups.Count == 0)
            {
                Log.Error(
                    "[MAP-机械族机械师] 剧本无法初始化所选机械师状态，已跳过监管者分配；运输舱投放仍将继续。");
                return false;
            }

            return true;
        }

        private static bool IsOverseeCandidate(Pawn mech, Pawn mechanitor)
        {
            if (mech == mechanitor || mech.Dead || !mech.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (mech.OverseerSubject == null)
            {
                return false;
            }

            return !MAPMechanitorNodeUtility.IsMechanitorNodeController(mech);
        }

        private static void AssignAllStartingMechsToMechanitor(
            Pawn mechanitor,
            List<Thing> mechanitorGroup)
        {
            if (!EnsureScenarioMechanitorState(mechanitor))
            {
                return;
            }

            mechanitor.relations ??= new Pawn_RelationsTracker(mechanitor);

            foreach (Thing item in mechanitorGroup)
            {
                if (item is not Pawn mech || !IsOverseeCandidate(mech, mechanitor))
                {
                    continue;
                }

                Pawn? existingOverseer = mech.GetOverseer();
                if (existingOverseer == mechanitor)
                {
                    continue;
                }

                if (existingOverseer?.relations != null)
                {
                    existingOverseer.relations.TryRemoveDirectRelation(
                        PawnRelationDefOf.Overseer,
                        mech);
                }

                mech.relations ??= new Pawn_RelationsTracker(mech);
                if (!mechanitor.mechanitor.CanOverseeSubject(mech))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 剧本无法为 " +
                        $"{mech.LabelShort}（{mech.kindDef?.defName ?? "unknown"}）分配监管者：带宽不足或主体不兼容。");
                    continue;
                }

                mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            }
        }
    }
}
