using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorPurgeDirectiveUtility
    {
        public static void Tick(GameComponent_MechanoidMechanitorStoryState storyState)
        {
            if (storyState == null || !storyState.PurgeDirectiveEnabled)
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

            if (runtime.FinalPenaltyTriggered)
            {
                if (runtime.RaidQueued)
                {
                    return;
                }

                if (ticksGame < runtime.NextFinalizationRetryTick)
                {
                    return;
                }

                if (!TryCompleteFinalRaidQueue(storyState, runtime))
                {
                    runtime.ScheduleFinalizationRetryFromNow();
                }

                return;
            }

            if (ticksGame < runtime.NextCheckTick)
            {
                return;
            }

            ForceProtocolCheckNow(storyState);
        }

        public static void ForceProtocolCheckNow()
        {
            if (Current.Game == null)
            {
                return;
            }

            ForceProtocolCheckNow(
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>());
        }

        public static void ForceProtocolCheckNow(
            GameComponent_MechanoidMechanitorStoryState? storyState)
        {
            if (storyState == null || !storyState.PurgeDirectiveEnabled)
            {
                return;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                storyState.PurgeDirectiveRuntimeState;
            if (runtime == null || runtime.FinalPenaltyTriggered)
            {
                return;
            }

            runtime.ScheduleNextCheckFromNow();
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
            runtime.RetainAliveTrackedPawns(aliveTracked);

            SendLetter(
                "MAP_MechanoidMechanitor.PurgeDirective.Letter.Red.Label",
                "MAP_MechanoidMechanitor.PurgeDirective.Letter.Red.Text",
                LetterDefOf.ThreatBig,
                aliveTracked);

            if (!TryCompleteFinalRaidQueue(storyState, runtime))
            {
                runtime.ScheduleFinalizationRetryFromNow();
            }
        }

        private static bool TryCompleteFinalRaidQueue(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorPurgeDirectiveRuntimeState runtime)
        {
            if (runtime.RaidQueued)
            {
                return true;
            }

            if (!TryApplyAndVerifyFinalMechHiveHostile(
                    storyState,
                    runtime,
                    out Faction mechHive))
            {
                return false;
            }

            if (!TryQueueFinalRaid(storyState, runtime, mechHive, runtime.TrackedPawns))
            {
                return false;
            }

            runtime.ClearFinalizationRetryTick();
            runtime.ClearTrackedPawns();
            runtime.ClearFinalizationFailureReason();
            return true;
        }

        private static bool TryApplyAndVerifyFinalMechHiveHostile(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorPurgeDirectiveRuntimeState runtime,
            out Faction mechHive)
        {
            mechHive = null!;
            storyState.RebuildRuntimeCaches();

            Faction? cachedMechHive = storyState.CachedMechHive;
            if (cachedMechHive == null)
            {
                NotifyFinalizationFailureOnce(runtime, "missingMechHive");
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                NotifyFinalizationFailureOnce(runtime, "missingPlayerFaction");
                return false;
            }

            if (!MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                    cachedMechHive,
                    FactionRelationKind.Hostile,
                    hostileOnHarmByPlayer: false))
            {
                NotifyFinalizationFailureOnce(runtime, "applyHostileFailed");
                return false;
            }

            if (!HasFinalMechHiveHostileLockState(player, cachedMechHive))
            {
                NotifyFinalizationFailureOnce(runtime, "hostileVerificationFailed");
                return false;
            }

            mechHive = cachedMechHive;
            return true;
        }

        private static bool HasFinalMechHiveHostileLockState(Faction player, Faction mechHive)
        {
            FactionRelation? playerRelation = player.RelationWith(mechHive, allowNull: true);
            FactionRelation? mechRelation = mechHive.RelationWith(player, allowNull: true);
            return playerRelation != null
                && mechRelation != null
                && playerRelation.kind == FactionRelationKind.Hostile
                && mechRelation.kind == FactionRelationKind.Hostile
                && !mechHive.factionHostileOnHarmByPlayer;
        }

        private static bool TryQueueFinalRaid(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorPurgeDirectiveRuntimeState runtime,
            Faction mechHive,
            List<Pawn>? preferredTargetPawns)
        {
            if (runtime.RaidQueued)
            {
                return true;
            }

            if (mechHive == null || !storyState.IsCurrentMechHive(mechHive))
            {
                NotifyFinalizationFailureOnce(runtime, "missingMechHive");
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                NotifyFinalizationFailureOnce(runtime, "missingPlayerFaction");
                return false;
            }

            if (!HasFinalMechHiveHostileLockState(player, mechHive))
            {
                NotifyFinalizationFailureOnce(runtime, "hostileVerificationFailed");
                return false;
            }

            Map? targetMap = ResolveRaidTargetMap(preferredTargetPawns);
            if (targetMap == null)
            {
                NotifyFinalizationFailureOnce(runtime, "missingPlayerHomeMap");
                return false;
            }

            Storyteller? storyteller = Find.Storyteller;
            if (storyteller?.incidentQueue == null)
            {
                NotifyFinalizationFailureOnce(runtime, "missingIncidentQueue");
                return false;
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
                NotifyFinalizationFailureOnce(runtime, "incidentQueueAddFailed");
                return false;
            }

            runtime.MarkRaidQueued();
            return true;
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

        private static void NotifyFinalizationFailureOnce(
            MechanoidMechanitorPurgeDirectiveRuntimeState runtime,
            string reason)
        {
            if (runtime == null || !runtime.TryNoteNewFinalizationFailureReason(reason))
            {
                return;
            }

            Log.Warning(
                "[MAP-机械族机械师] 肃清指令最终机械巢袭击尚未入队："
                + DescribeFinalizationFailure(reason)
                + "。将按低频重试，不会改用其他敌对派系。");
        }

        private static string DescribeFinalizationFailure(string reason)
        {
            return reason switch
            {
                "missingMechHive" => "当前没有可用的机械巢缓存",
                "missingPlayerFaction" => "玩家派系不存在",
                "applyHostileFailed" => "机械巢敌对关系写入失败",
                "hostileVerificationFailed" => "机械巢敌对关系验证失败",
                "missingPlayerHomeMap" => "当前没有有效的玩家殖民地地图",
                "missingIncidentQueue" => "故事叙述者袭击队列不可用",
                "incidentQueueAddFailed" => "袭击事件未能加入队列",
                _ => reason
            };
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
