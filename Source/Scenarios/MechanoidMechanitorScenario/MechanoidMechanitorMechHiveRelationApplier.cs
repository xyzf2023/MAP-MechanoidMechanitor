using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorMechHiveRelationApplier
    {
        private static bool applying;

        public static bool IsApplying => applying;

        public static bool ApplyExactMechHiveRelation(
            Faction mechHive,
            FactionRelationKind relationKind,
            bool hostileOnHarmByPlayer)
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
                    "[MAP-机械族机械师] 无法写入机械巢关系：玩家派系不存在。");
                return false;
            }

            if (mechHive == null)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null || !storyState.IsCurrentMechHive(mechHive))
            {
                return false;
            }

            applying = true;
            try
            {
                if (!TryEnsureBidirectionalRelations(player, mechHive))
                {
                    return false;
                }

                FactionRelation? playerRelation = player.RelationWith(mechHive, allowNull: true);
                FactionRelation? mechRelation = mechHive.RelationWith(player, allowNull: true);
                if (playerRelation == null || mechRelation == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 无法写入机械巢关系：缺少与玩家的双向关系记录。");
                    return false;
                }

                mechHive.factionHostileOnHarmByPlayer = hostileOnHarmByPlayer;

                if (playerRelation.kind != relationKind)
                {
                    player.SetRelationDirect(
                        mechHive,
                        relationKind,
                        canSendHostilityLetter: false,
                        reason: null,
                        lookTarget: GlobalTargetInfo.Invalid);
                }
                else if (mechRelation.kind != relationKind)
                {
                    mechHive.SetRelationDirect(
                        player,
                        relationKind,
                        canSendHostilityLetter: false,
                        reason: null,
                        lookTarget: GlobalTargetInfo.Invalid);
                }

                return ValidateAppliedMechHiveRelation(
                    player,
                    mechHive,
                    relationKind,
                    hostileOnHarmByPlayer);
            }
            finally
            {
                applying = false;
            }
        }

        private static bool ValidateAppliedMechHiveRelation(
            Faction player,
            Faction mechHive,
            FactionRelationKind relationKind,
            bool hostileOnHarmByPlayer)
        {
            FactionRelation? playerRelation = player.RelationWith(mechHive, allowNull: true);
            FactionRelation? mechRelation = mechHive.RelationWith(player, allowNull: true);
            if (playerRelation != null
                && mechRelation != null
                && playerRelation.kind == relationKind
                && mechRelation.kind == relationKind
                && mechHive.factionHostileOnHarmByPlayer == hostileOnHarmByPlayer)
            {
                return true;
            }

            string playerKind = playerRelation?.kind.ToString() ?? "null";
            string mechKind = mechRelation?.kind.ToString() ?? "null";
            Log.Error(
                "[MAP-机械族机械师] 机械巢关系写入后状态不一致：机械巢="
                + mechHive.Name
                + "，目标="
                + relationKind
                + "，玩家侧="
                + playerKind
                + "，机械巢侧="
                + mechKind
                + "，hostileOnHarmByPlayer目标="
                + hostileOnHarmByPlayer
                + "，实际="
                + mechHive.factionHostileOnHarmByPlayer
                + "。");
            return false;
        }

        private static bool TryEnsureBidirectionalRelations(Faction player, Faction mechHive)
        {
            FactionRelation? playerSide = player.RelationWith(mechHive, allowNull: true);
            FactionRelation? mechSide = mechHive.RelationWith(player, allowNull: true);
            if (playerSide != null && mechSide != null)
            {
                return true;
            }

            if (playerSide == null && mechSide == null)
            {
                player.TryMakeInitialRelationsWith(mechHive);
            }
            else if (playerSide != null)
            {
                player.SetRelation(
                    new FactionRelation
                    {
                        other = mechHive,
                        kind = playerSide.kind,
                        baseGoodwill = playerSide.baseGoodwill
                    });
            }
            else
            {
                mechHive.SetRelation(
                    new FactionRelation
                    {
                        other = player,
                        kind = mechSide!.kind,
                        baseGoodwill = mechSide.baseGoodwill
                    });
            }

            playerSide = player.RelationWith(mechHive, allowNull: true);
            mechSide = mechHive.RelationWith(player, allowNull: true);
            if (playerSide == null || mechSide == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法写入机械巢关系：缺少与玩家的双向关系记录。");
                return false;
            }

            return true;
        }

        public static void ApplyInitialMechHiveRelation(
            GameComponent_MechanoidMechanitorStoryState storyState)
        {
            if (storyState == null)
            {
                return;
            }

            if (storyState.InitialMechHiveRelationApplied)
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械巢初始关系被重复应用，已跳过。");
                return;
            }

            storyState.RebuildRuntimeCaches();
            if (!storyState.TryGetMechHiveRelationMode(
                    out MechanoidMechanitorMechHiveRelationMode mode))
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械巢初始关系应用失败：活动配置不存在。");
                return;
            }

            if (mode == MechanoidMechanitorMechHiveRelationMode.Default)
            {
                storyState.MarkInitialMechHiveRelationApplied();
                return;
            }

            Faction? mechHive = storyState.CachedMechHive;
            if (mechHive == null)
            {
                storyState.MarkInitialMechHiveRelationApplied();
                return;
            }

            if (!MechanoidMechanitorMechHiveRelationPolicy.TryGetInitialTarget(
                    mode,
                    out FactionRelationKind relationKind,
                    out bool hostileOnHarmByPlayer))
            {
                storyState.MarkInitialMechHiveRelationApplied();
                return;
            }

            if (!ApplyExactMechHiveRelation(mechHive, relationKind, hostileOnHarmByPlayer))
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械巢初始关系未成功写入，已保持未初始化状态。");
                return;
            }

            storyState.MarkInitialMechHiveRelationApplied();
        }

        public static void CalibrateLockedMechHiveRelation(
            GameComponent_MechanoidMechanitorStoryState storyState)
        {
            if (storyState == null
                || !storyState.InitialMechHiveRelationApplied)
            {
                return;
            }

            Faction? mechHive = storyState.CachedMechHive;
            if (mechHive == null
                || !storyState.TryGetMechHiveRelationMode(
                    out MechanoidMechanitorMechHiveRelationMode mode))
            {
                return;
            }

            if (!MechanoidMechanitorMechHiveRelationPolicy.TryGetLockedTarget(
                    mode,
                    out FactionRelationKind relationKind))
            {
                return;
            }

            if (!ApplyExactMechHiveRelation(
                    mechHive,
                    relationKind,
                    hostileOnHarmByPlayer: false))
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械巢永久关系校准失败。");
            }
        }

        public static void ApplyPolicyToNewMechHive(Faction faction)
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
                || !storyState.InitialMechHiveRelationApplied
                || !storyState.IsCurrentMechHive(faction)
                || !storyState.TryGetMechHiveRelationMode(
                    out MechanoidMechanitorMechHiveRelationMode mode))
            {
                return;
            }

            if (!MechanoidMechanitorMechHiveRelationPolicy.TryGetInitialTarget(
                    mode,
                    out FactionRelationKind relationKind,
                    out bool hostileOnHarmByPlayer))
            {
                return;
            }

            if (!ApplyExactMechHiveRelation(faction, relationKind, hostileOnHarmByPlayer))
            {
                Log.Error(
                    "[MAP-机械族机械师] 新加入机械巢的关系应用失败。");
            }
        }
    }
}
