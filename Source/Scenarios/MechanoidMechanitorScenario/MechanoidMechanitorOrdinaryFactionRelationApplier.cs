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
            FactionRelationKind relationKind)
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

            if (!MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(target))
            {
                return false;
            }

            applying = true;
            try
            {
                FactionRelation? playerRelation = player.RelationWith(target, allowNull: true);
                FactionRelation? targetRelation = target.RelationWith(player, allowNull: true);
                if (playerRelation == null || targetRelation == null)
                {
                    player.TryMakeInitialRelationsWith(target);
                    playerRelation = player.RelationWith(target, allowNull: true);
                    targetRelation = target.RelationWith(player, allowNull: true);
                    if (playerRelation == null || targetRelation == null)
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 无法写入普通派系关系：派系 "
                            + target.Name
                            + " 缺少与玩家的双向关系记录。");
                        return false;
                    }
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

                if (previousPlayerKind != relationKind)
                {
                    player.Notify_RelationKindChanged(
                        target,
                        previousPlayerKind,
                        canSendLetter: false,
                        reason: null,
                        GlobalTargetInfo.Invalid,
                        out _);
                }

                if (previousTargetKind != relationKind)
                {
                    target.Notify_RelationKindChanged(
                        player,
                        previousTargetKind,
                        canSendLetter: false,
                        reason: null,
                        GlobalTargetInfo.Invalid,
                        out _);
                }

                return true;
            }
            finally
            {
                applying = false;
            }
        }

        public static bool TryApplyOption(
            Faction target,
            MechanoidMechanitorFactionRelationOption option)
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

            return ApplyExactPlayerRelation(target, goodwill, relationKind);
        }

        public static void ApplyInitialOrdinaryFactionRelations(
            GameComponent_MechanoidMechanitorStoryState storyState)
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

                TryApplyOption(faction, option);
            }

            temporaryOrdinaryFactions.Clear();
            storyState.MarkInitialOrdinaryFactionRelationsApplied();
        }

        public static void CalibrateLockedOrdinaryFactionRelations(
            GameComponent_MechanoidMechanitorStoryState storyState)
        {
            if (storyState == null
                || !storyState.InitialOrdinaryFactionRelationsApplied)
            {
                return;
            }

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

                ApplyExactPlayerRelation(faction, goodwill, relationKind);
            }

            temporaryOrdinaryFactions.Clear();
        }

        public static void ApplyPolicyToNewOrdinaryFaction(Faction faction)
        {
            if (faction == null
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                || !GameComponent_MechanoidMechanitorStoryState
                    .HasAppliedInitialOrdinaryFactionRelations
                || !MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(faction))
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                    .TryGetEffectiveOrdinaryFactionRelationOptionFor(
                        faction,
                        out MechanoidMechanitorFactionRelationOption option))
            {
                return;
            }

            TryApplyOption(faction, option);
        }
    }
}
