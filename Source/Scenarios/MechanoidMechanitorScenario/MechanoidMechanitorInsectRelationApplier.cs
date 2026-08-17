using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorInsectRelationApplier
    {
        private static bool applying;

        public static bool IsApplying => applying;

        public static bool ApplyExactInsectRelation(
            Faction insectFaction,
            FactionRelationKind relationKind,
            bool hostileOnHarmByPlayer,
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
                    "[MAP-机械族机械师] 无法写入虫巢关系：玩家派系不存在。");
                return false;
            }

            if (insectFaction == null)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null || !storyState.IsCurrentInsectFaction(insectFaction))
            {
                return false;
            }

            applying = true;
            try
            {
                if (!TryEnsureBidirectionalRelations(player, insectFaction))
                {
                    return false;
                }

                FactionRelation? playerRelation =
                    player.RelationWith(insectFaction, allowNull: true);
                FactionRelation? insectRelation =
                    insectFaction.RelationWith(player, allowNull: true);
                if (playerRelation == null || insectRelation == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 无法写入虫巢关系：缺少与玩家的双向关系记录。");
                    return false;
                }

                FactionRelationKind previousPlayerKind = playerRelation.kind;
                FactionRelationKind previousInsectKind = insectRelation.kind;

                playerRelation.kind = relationKind;
                insectRelation.kind = relationKind;
                insectFaction.factionHostileOnHarmByPlayer = hostileOnHarmByPlayer;

                if (!ValidateAppliedInsectRelation(
                        player,
                        insectFaction,
                        relationKind,
                        hostileOnHarmByPlayer))
                {
                    return false;
                }

                if (previousPlayerKind != relationKind)
                {
                    MechanoidMechanitorFactionRelationNotificationUtility.Dispatch(
                        storyState,
                        notificationMode,
                        player,
                        insectFaction,
                        previousPlayerKind,
                        relationKind,
                        "虫巢关系：玩家侧");
                }

                if (previousInsectKind != relationKind)
                {
                    MechanoidMechanitorFactionRelationNotificationUtility.Dispatch(
                        storyState,
                        notificationMode,
                        insectFaction,
                        player,
                        previousInsectKind,
                        relationKind,
                        "虫巢关系：虫巢侧");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 写入虫巢关系时发生异常。虫巢="
                    + insectFaction.Name
                    + "，目标关系="
                    + relationKind
                    + "，hostileOnHarmByPlayer="
                    + hostileOnHarmByPlayer
                    + "\n"
                    + ex);
                return false;
            }
            finally
            {
                applying = false;
            }
        }

        private static bool ValidateAppliedInsectRelation(
            Faction player,
            Faction insectFaction,
            FactionRelationKind relationKind,
            bool hostileOnHarmByPlayer)
        {
            FactionRelation? playerRelation =
                player.RelationWith(insectFaction, allowNull: true);
            FactionRelation? insectRelation =
                insectFaction.RelationWith(player, allowNull: true);
            if (playerRelation != null
                && insectRelation != null
                && playerRelation.kind == relationKind
                && insectRelation.kind == relationKind
                && insectFaction.factionHostileOnHarmByPlayer == hostileOnHarmByPlayer)
            {
                return true;
            }

            string playerKind = playerRelation?.kind.ToString() ?? "null";
            string insectKind = insectRelation?.kind.ToString() ?? "null";
            Log.Error(
                "[MAP-机械族机械师] 虫巢关系写入后状态不一致：虫巢="
                + insectFaction.Name
                + "，目标="
                + relationKind
                + "，玩家侧="
                + playerKind
                + "，虫巢侧="
                + insectKind
                + "，hostileOnHarmByPlayer目标="
                + hostileOnHarmByPlayer
                + "，实际="
                + insectFaction.factionHostileOnHarmByPlayer
                + "。");
            return false;
        }

        private static bool TryEnsureBidirectionalRelations(
            Faction player,
            Faction insectFaction)
        {
            FactionRelation? playerSide =
                player.RelationWith(insectFaction, allowNull: true);
            FactionRelation? insectSide =
                insectFaction.RelationWith(player, allowNull: true);
            if (playerSide != null && insectSide != null)
            {
                return true;
            }

            if (playerSide == null && insectSide == null)
            {
                player.TryMakeInitialRelationsWith(insectFaction);
            }
            else if (playerSide != null)
            {
                insectFaction.SetRelation(
                    new FactionRelation
                    {
                        other = player,
                        kind = playerSide.kind,
                        baseGoodwill = playerSide.baseGoodwill
                    });
            }
            else
            {
                player.SetRelation(
                    new FactionRelation
                    {
                        other = insectFaction,
                        kind = insectSide!.kind,
                        baseGoodwill = insectSide.baseGoodwill
                    });
            }

            playerSide = player.RelationWith(insectFaction, allowNull: true);
            insectSide = insectFaction.RelationWith(player, allowNull: true);
            if (playerSide == null || insectSide == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法写入虫巢关系：缺少与玩家的双向关系记录。");
                return false;
            }

            return true;
        }

        public static void ApplyInitialInsectRelation(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorFactionRelationNotificationMode notificationMode =
                MechanoidMechanitorFactionRelationNotificationMode.Deferred)
        {
            if (storyState == null)
            {
                return;
            }

            if (storyState.InitialInsectRelationApplied)
            {
                Log.Error(
                    "[MAP-机械族机械师] 虫巢初始关系被重复应用，已跳过。");
                return;
            }

            storyState.RebuildRuntimeCaches();

            if (!storyState.TryGetInsectRelationMode(
                    out MechanoidMechanitorInsectRelationMode mode))
            {
                Log.Error(
                    "[MAP-机械族机械师] 虫巢初始关系应用失败：活动配置不存在。");
                return;
            }

            if (mode == MechanoidMechanitorInsectRelationMode.Default)
            {
                storyState.MarkInitialInsectRelationApplied();
                return;
            }

            Faction? insectFaction = storyState.CachedInsectFaction;
            if (insectFaction == null)
            {
                storyState.MarkInitialInsectRelationApplied();
                return;
            }

            if (!MechanoidMechanitorInsectRelationPolicy.TryGetInitialTarget(
                    mode,
                    out FactionRelationKind relationKind,
                    out bool hostileOnHarmByPlayer))
            {
                storyState.MarkInitialInsectRelationApplied();
                return;
            }

            if (!ApplyExactInsectRelation(
                    insectFaction,
                    relationKind,
                    hostileOnHarmByPlayer,
                    notificationMode))
            {
                Log.Error(
                    "[MAP-机械族机械师] 虫巢初始关系未成功写入，已保持未初始化状态。");
                return;
            }

            storyState.MarkInitialInsectRelationApplied();
        }

        public static void CalibrateLockedInsectRelation(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorFactionRelationNotificationMode notificationMode =
                MechanoidMechanitorFactionRelationNotificationMode.Immediate)
        {
            if (storyState == null
                || !storyState.InitialInsectRelationApplied)
            {
                return;
            }

            Faction? insectFaction = storyState.CachedInsectFaction;
            if (insectFaction == null)
            {
                return;
            }

            if (!MechanoidMechanitorInsectRelationPolicy.TryGetEffectiveLockedTarget(
                    storyState,
                    out FactionRelationKind relationKind,
                    out bool hostileOnHarmByPlayer))
            {
                return;
            }

            if (!ApplyExactInsectRelation(
                    insectFaction,
                    relationKind,
                    hostileOnHarmByPlayer,
                    notificationMode))
            {
                Log.Error(
                    "[MAP-机械族机械师] 虫巢永久关系校准失败。");
            }
        }

        public static void ApplyPolicyToNewInsectFaction(Faction faction)
        {
            if (faction == null
                || applying
                || Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null
                || !storyState.InitialInsectRelationApplied
                || !storyState.IsCurrentInsectFaction(faction))
            {
                return;
            }

            if (!MechanoidMechanitorInsectRelationPolicy.TryGetEffectiveInitialTarget(
                    storyState,
                    out FactionRelationKind relationKind,
                    out bool hostileOnHarmByPlayer))
            {
                return;
            }

            if (!ApplyExactInsectRelation(faction, relationKind, hostileOnHarmByPlayer))
            {
                Log.Error(
                    "[MAP-机械族机械师] 新加入虫巢的关系应用失败。");
            }
        }
    }
}
