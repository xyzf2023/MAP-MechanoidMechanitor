using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorOrdinaryFactionRelationApplier
    {
        private static bool applying;

        private static readonly List<Faction> temporaryOrdinaryFactions = new List<Faction>();

        public static bool IsApplying => applying;

        public static bool ApplyExactPlayerRelation(
            Faction target,
            int goodwill,
            FactionRelationKind relationKind,
            MechanoidMechanitorFactionRelationNotificationMode notificationMode =
                MechanoidMechanitorFactionRelationNotificationMode.Immediate)
        {
            if (applying)
            {
                return false;
            }

            if (Current.Game == null || Find.FactionManager == null)
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法写入普通派系关系：玩家派系不存在。");
                return false;
            }

            if (target == null || target == player)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState != null)
            {
                if (!storyState.IsCachedOrdinaryFaction(target)
                    && !MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(target))
                {
                    return false;
                }
            }
            else if (!MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(target))
            {
                return false;
            }

            applying = true;
            try
            {
                if (!TryEnsureBidirectionalRelations(
                        player,
                        target,
                        out FactionRelation playerRelation,
                        out FactionRelation targetRelation))
                {
                    return false;
                }

                if (playerRelation.baseGoodwill == goodwill
                    && playerRelation.kind == relationKind
                    && targetRelation.baseGoodwill == goodwill
                    && targetRelation.kind == relationKind)
                {
                    return true;
                }

                FactionRelationKind previousPlayerKind = playerRelation.kind;
                FactionRelationKind previousTargetKind = targetRelation.kind;

                playerRelation.baseGoodwill = goodwill;
                playerRelation.kind = relationKind;
                targetRelation.baseGoodwill = goodwill;
                targetRelation.kind = relationKind;

                if (!ValidateAppliedPlayerRelation(
                        player,
                        target,
                        goodwill,
                        relationKind))
                {
                    return false;
                }

                if (previousPlayerKind != relationKind)
                {
                    MechanoidMechanitorFactionRelationNotificationUtility.Dispatch(
                        storyState,
                        notificationMode,
                        player,
                        target,
                        previousPlayerKind,
                        relationKind,
                        "普通派系关系：玩家侧");
                }

                if (previousTargetKind != relationKind)
                {
                    MechanoidMechanitorFactionRelationNotificationUtility.Dispatch(
                        storyState,
                        notificationMode,
                        target,
                        player,
                        previousTargetKind,
                        relationKind,
                        "普通派系关系：目标派系侧");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 写入普通派系关系时发生异常。目标派系="
                    + target.Name
                    + "，目标好感="
                    + goodwill
                    + "，目标关系="
                    + relationKind
                    + "\n"
                    + ex);
                return false;
            }
            finally
            {
                applying = false;
            }
        }

        public static bool TryApplyOption(
            Faction target,
            MechanoidMechanitorFactionRelationOption option,
            MechanoidMechanitorFactionRelationNotificationMode notificationMode =
                MechanoidMechanitorFactionRelationNotificationMode.Immediate)
        {
            if (option == MechanoidMechanitorFactionRelationOption.Default)
            {
                return true;
            }

            if (!MechanoidMechanitorOrdinaryFactionRelationPolicy.TryGetInitialRelationTarget(
                    option,
                    out int goodwill,
                    out FactionRelationKind relationKind))
            {
                return true;
            }

            return ApplyExactPlayerRelation(
                target,
                goodwill,
                relationKind,
                notificationMode);
        }

        public static void ApplyInitialOrdinaryFactionRelations(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorFactionRelationNotificationMode notificationMode =
                MechanoidMechanitorFactionRelationNotificationMode.Deferred)
        {
            if (storyState == null)
            {
                return;
            }

            if (storyState.InitialOrdinaryFactionRelationsApplied)
            {
                Log.Error(
                    "[MAP-机械族机械师] 普通派系初始关系被重复应用，已跳过。");
                return;
            }

            storyState.RebuildRuntimeCaches();
            bool allSucceeded = true;
            try
            {
                MechanoidMechanitorOrdinaryFactionUtility.CollectOrdinaryFactions(
                    temporaryOrdinaryFactions);
                for (int i = 0; i < temporaryOrdinaryFactions.Count; i++)
                {
                    Faction faction = temporaryOrdinaryFactions[i];
                    if (!storyState.TryResolveEffectiveOrdinaryFactionRelationOption(
                            faction,
                            out MechanoidMechanitorFactionRelationOption option))
                    {
                        continue;
                    }

                    try
                    {
                        if (!TryApplyOption(faction, option, notificationMode))
                        {
                            allSucceeded = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        allSucceeded = false;
                        Log.Error(
                            "[MAP-机械族机械师] 应用普通派系初始关系时发生异常，"
                            + "已继续处理其他派系。目标派系="
                            + faction.Name
                            + "\n"
                            + ex);
                    }
                }
            }
            finally
            {
                temporaryOrdinaryFactions.Clear();
            }

            if (!allSucceeded)
            {
                Log.Error(
                    "[MAP-机械族机械师] 普通派系初始关系未全部成功写入，已保持未初始化状态。");
                return;
            }

            storyState.MarkInitialOrdinaryFactionRelationsApplied();
        }

        public static void CalibrateLockedOrdinaryFactionRelations(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorFactionRelationNotificationMode notificationMode =
                MechanoidMechanitorFactionRelationNotificationMode.Immediate)
        {
            if (storyState == null
                || !storyState.InitialOrdinaryFactionRelationsApplied)
            {
                return;
            }

            try
            {
                MechanoidMechanitorOrdinaryFactionUtility.CollectOrdinaryFactions(
                    temporaryOrdinaryFactions);
                for (int i = 0; i < temporaryOrdinaryFactions.Count; i++)
                {
                    Faction faction = temporaryOrdinaryFactions[i];
                    if (!storyState.TryResolveEffectiveOrdinaryFactionRelationOption(
                            faction,
                            out MechanoidMechanitorFactionRelationOption option))
                    {
                        continue;
                    }

                    if (!MechanoidMechanitorOrdinaryFactionRelationPolicy.TryGetLockedRelationTarget(
                            option,
                            out int goodwill,
                            out FactionRelationKind relationKind))
                    {
                        continue;
                    }

                    try
                    {
                        ApplyExactPlayerRelation(
                            faction,
                            goodwill,
                            relationKind,
                            notificationMode);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 校准普通派系永久关系时发生异常，"
                            + "已继续处理其他派系。当前处于永久关系校准阶段。目标派系="
                            + faction.Name
                            + "，目标好感="
                            + goodwill
                            + "，目标关系="
                            + relationKind
                            + "\n"
                            + ex);
                    }
                }
            }
            finally
            {
                temporaryOrdinaryFactions.Clear();
            }
        }

        public static void ApplyPolicyToNewOrdinaryFaction(Faction faction)
        {
            if (faction == null
                || applying
                || !GameComponent_MechanoidMechanitorStoryState.IsStoryConfigurationActive
                || Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null
                || !storyState.InitialOrdinaryFactionRelationsApplied
                || !storyState.IsCachedOrdinaryFaction(faction))
            {
                return;
            }

            if (!storyState.TryResolveEffectiveOrdinaryFactionRelationOption(
                    faction,
                    out MechanoidMechanitorFactionRelationOption option))
            {
                return;
            }

            TryApplyOption(faction, option);
        }

        private static bool ValidateAppliedPlayerRelation(
            Faction player,
            Faction target,
            int goodwill,
            FactionRelationKind relationKind)
        {
            FactionRelation? playerRelation =
                player.RelationWith(target, allowNull: true);
            FactionRelation? targetRelation =
                target.RelationWith(player, allowNull: true);

            if (playerRelation != null
                && targetRelation != null
                && playerRelation.baseGoodwill == goodwill
                && targetRelation.baseGoodwill == goodwill
                && playerRelation.kind == relationKind
                && targetRelation.kind == relationKind)
            {
                return true;
            }

            Log.Error(
                "[MAP-机械族机械师] 普通派系关系写入后状态不一致。目标派系="
                + target.Name
                + "，目标好感="
                + goodwill
                + "，目标关系="
                + relationKind
                + "，玩家侧好感="
                + (playerRelation?.baseGoodwill.ToString() ?? "null")
                + "，玩家侧关系="
                + (playerRelation?.kind.ToString() ?? "null")
                + "，目标侧好感="
                + (targetRelation?.baseGoodwill.ToString() ?? "null")
                + "，目标侧关系="
                + (targetRelation?.kind.ToString() ?? "null"));

            return false;
        }

        private static bool TryEnsureBidirectionalRelations(
            Faction player,
            Faction target,
            out FactionRelation playerRelation,
            out FactionRelation targetRelation)
        {
            playerRelation = null!;
            targetRelation = null!;

            FactionRelation? playerSide = player.RelationWith(target, allowNull: true);
            FactionRelation? targetSide = target.RelationWith(player, allowNull: true);

            if (playerSide != null && targetSide != null)
            {
                playerRelation = playerSide;
                targetRelation = targetSide;
                return true;
            }

            if (playerSide == null && targetSide == null)
            {
                player.TryMakeInitialRelationsWith(target);
            }
            else if (playerSide != null)
            {
                FactionRelation rebuild = new FactionRelation
                {
                    other = target,
                    baseGoodwill = playerSide.baseGoodwill,
                    kind = playerSide.kind
                };
                player.SetRelation(rebuild);
            }
            else
            {
                FactionRelation rebuild = new FactionRelation
                {
                    other = player,
                    baseGoodwill = targetSide!.baseGoodwill,
                    kind = targetSide.kind
                };
                target.SetRelation(rebuild);
            }

            playerSide = player.RelationWith(target, allowNull: true);
            targetSide = target.RelationWith(player, allowNull: true);
            if (playerSide == null || targetSide == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法写入普通派系关系：派系 "
                    + target.Name
                    + " 缺少与玩家的双向关系记录。");
                return false;
            }

            playerRelation = playerSide;
            targetRelation = targetSide;
            return true;
        }
    }
}
