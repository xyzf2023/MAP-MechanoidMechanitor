using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorPurgeDirectiveUtility
    {
        public static bool IsPurgeDirectiveActive =>
            GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive;

        public static void Tick(GameComponent_MechanoidMechanitorStoryState storyState)
        {
            if (storyState == null || !IsPurgeDirectiveActive)
            {
                return;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                storyState.PurgeDirectiveRuntimeState;
            if (runtime == null)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame < runtime.NextCheckTick)
            {
                return;
            }

            runtime.ScheduleNextCheckFromNow();

            if (runtime.FinalPenaltyTriggered)
            {
                if (!runtime.RaidQueued)
                {
                    TryQueueFinalRaid(storyState, runtime, runtime.TrackedPawns);
                }

                return;
            }

            RunProtocolCheck(storyState, runtime);
        }

        public static void CollectFleshColonists(List<Pawn> results)
        {
            results.Clear();
            List<Map>? maps = Find.Maps;
            if (maps == null)
            {
                return;
            }

            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                Map map = maps[mapIndex];
                if (map == null || !map.IsPlayerHome)
                {
                    continue;
                }

                List<Pawn> freeColonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < freeColonists.Count; i++)
                {
                    Pawn pawn = freeColonists[i];
                    if (!IsFleshColonist(pawn))
                    {
                        continue;
                    }

                    if (!results.Contains(pawn))
                    {
                        results.Add(pawn);
                    }
                }
            }
        }

        public static bool IsFleshColonist(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
            {
                return false;
            }

            if (!pawn.RaceProps.Humanlike || !pawn.RaceProps.IsFlesh)
            {
                return false;
            }

            if (pawn.IsPrisoner)
            {
                return false;
            }

            return true;
        }

        public static void CollectAliveTrackedPawns(
            List<Pawn> trackedPawns,
            List<Pawn> results)
        {
            results.Clear();
            if (trackedPawns == null)
            {
                return;
            }

            for (int i = 0; i < trackedPawns.Count; i++)
            {
                Pawn? pawn = trackedPawns[i];
                if (pawn == null || pawn.Dead || results.Contains(pawn))
                {
                    continue;
                }

                results.Add(pawn);
            }
        }

        private static void RunProtocolCheck(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorPurgeDirectiveRuntimeState runtime)
        {
            List<Pawn> aliveTracked = new List<Pawn>();
            CollectAliveTrackedPawns(runtime.TrackedPawns, aliveTracked);

            if (aliveTracked.Count > 0)
            {
                HandleFailureWithAliveTargets(storyState, runtime, aliveTracked);
                return;
            }

            if (runtime.TrackedPawns.Count > 0)
            {
                runtime.ClearTrackedPawns();
            }

            List<Pawn> fleshColonists = new List<Pawn>();
            CollectFleshColonists(fleshColonists);
            if (fleshColonists.Count == 0)
            {
                return;
            }

            SendLetter(
                "MAP_MechanoidMechanitor.PurgeDirective.Letter.Yellow.Label",
                "MAP_MechanoidMechanitor.PurgeDirective.Letter.Yellow.Text",
                LetterDefOf.NegativeEvent,
                fleshColonists);
            runtime.SetTrackedPawns(fleshColonists);
        }

        private static void HandleFailureWithAliveTargets(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorPurgeDirectiveRuntimeState runtime,
            List<Pawn> aliveTracked)
        {
            int orangeWarningsSent = runtime.OrangeWarningsSent;
            if (orangeWarningsSent <= 0)
            {
                SendLetter(
                    "MAP_MechanoidMechanitor.PurgeDirective.Letter.Orange1.Label",
                    "MAP_MechanoidMechanitor.PurgeDirective.Letter.Orange1.Text",
                    LetterDefOf.ThreatSmall,
                    aliveTracked);
                runtime.SetOrangeWarningsSent(1);
                runtime.RetainAliveTrackedPawns(aliveTracked);
                return;
            }

            if (orangeWarningsSent == 1)
            {
                SendLetter(
                    "MAP_MechanoidMechanitor.PurgeDirective.Letter.Orange2.Label",
                    "MAP_MechanoidMechanitor.PurgeDirective.Letter.Orange2.Text",
                    LetterDefOf.ThreatSmall,
                    aliveTracked);
                runtime.SetOrangeWarningsSent(2);
                runtime.RetainAliveTrackedPawns(aliveTracked);
                return;
            }

            TriggerFinalPenalty(storyState, runtime, aliveTracked);
        }

        private static void TriggerFinalPenalty(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorPurgeDirectiveRuntimeState runtime,
            List<Pawn> aliveTracked)
        {
            if (runtime.FinalPenaltyTriggered)
            {
                return;
            }

            runtime.MarkFinalPenaltyTriggered();
            storyState.RebuildRuntimeCaches();

            Faction? mechHive = storyState.CachedMechHive;
            if (mechHive != null)
            {
                MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                    mechHive,
                    FactionRelationKind.Hostile,
                    hostileOnHarmByPlayer: false);
            }

            SendLetter(
                "MAP_MechanoidMechanitor.PurgeDirective.Letter.Red.Label",
                "MAP_MechanoidMechanitor.PurgeDirective.Letter.Red.Text",
                LetterDefOf.ThreatBig,
                aliveTracked);

            TryQueueFinalRaid(storyState, runtime, aliveTracked);
            runtime.ClearTrackedPawns();
        }

        private static void TryQueueFinalRaid(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorPurgeDirectiveRuntimeState runtime,
            List<Pawn>? preferredTargetPawns)
        {
            if (runtime.RaidQueued)
            {
                return;
            }

            Map? targetMap = ResolveRaidTargetMap(preferredTargetPawns);
            if (targetMap == null)
            {
                return;
            }

            Faction? mechHive = storyState.CachedMechHive;
            if (mechHive == null)
            {
                return;
            }

            Storyteller? storyteller = Find.Storyteller;
            if (storyteller?.incidentQueue == null)
            {
                return;
            }

            IncidentParms parms = new IncidentParms
            {
                target = targetMap,
                faction = mechHive,
                points = StorytellerUtility.DefaultThreatPointsNow(targetMap),
                forced = true
            };

            if (!storyteller.incidentQueue.Add(
                    IncidentDefOf.RaidEnemy,
                    Find.TickManager.TicksGame
                        + MechanoidMechanitorPurgeDirectiveRuntimeState.FinalRaidDelayTicks,
                    parms))
            {
                return;
            }

            runtime.MarkRaidQueued();
        }

        private static Map? ResolveRaidTargetMap(List<Pawn>? preferredTargetPawns)
        {
            if (preferredTargetPawns != null)
            {
                for (int i = 0; i < preferredTargetPawns.Count; i++)
                {
                    Pawn? pawn = preferredTargetPawns[i];
                    if (pawn == null || pawn.Dead)
                    {
                        continue;
                    }

                    Map? map = pawn.MapHeld;
                    if (map != null && map.IsPlayerHome)
                    {
                        return map;
                    }
                }
            }

            return Find.AnyPlayerHomeMap;
        }

        private static void SendLetter(
            string labelKey,
            string textKey,
            LetterDef letterDef,
            List<Pawn> lookTargetPawns)
        {
            if (lookTargetPawns != null && lookTargetPawns.Count > 0)
            {
                Find.LetterStack.ReceiveLetter(
                    labelKey.Translate(),
                    textKey.Translate(),
                    letterDef,
                    lookTargetPawns);
                return;
            }

            Find.LetterStack.ReceiveLetter(
                labelKey.Translate(),
                textKey.Translate(),
                letterDef);
        }
    }
}
