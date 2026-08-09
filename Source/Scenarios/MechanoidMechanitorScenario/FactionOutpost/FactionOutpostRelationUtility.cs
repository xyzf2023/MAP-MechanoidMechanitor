using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨的实时关系判定。节点本身不保存“出生时关系”；所有行为都读取所属 Faction
    /// 当前与玩家的真实关系。剧情风格锁定的永久中立/盟友会阻止玩家主动进攻。
    /// </summary>
    public static class FactionOutpostRelationUtility
    {
        public static FactionRelationKind GetCurrentKind(MAPFactionOutpost? outpost)
        {
            Faction? owner = outpost?.Faction;
            Faction? player = Faction.OfPlayerSilentFail;
            if (owner == null || player == null)
            {
                return FactionRelationKind.Hostile;
            }

            return owner.RelationKindWith(player);
        }

        public static bool CanPlayerAttack(MAPFactionOutpost? outpost)
        {
            Faction? owner = outpost?.Faction;
            Faction? player = Faction.OfPlayerSilentFail;
            if (outpost == null || owner == null || player == null)
            {
                return false;
            }

            if (owner.HostileTo(player))
            {
                return true;
            }

            // 只有本 MOD 的“永久普通派系关系”才需要额外拦截；普通游戏中的盟友/中立
            // 仍允许沿用原版“确认后攻击并转敌”的行为。
            if (GameComponent_MechanoidMechanitorStoryState.TryGetLockedOrdinaryFactionRelation(
                    player,
                    owner,
                    out _,
                    out FactionRelationKind lockedKind)
                && lockedKind != FactionRelationKind.Hostile)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 真正进入地图前的最终复查。未敌对且未被永久关系锁定时，使用原版同类善意变化
        /// 把所属派系转为敌对；最后必须确认真实关系已经变为敌对才允许进入。
        /// </summary>
        public static bool TryEnsureHostileForAttackEntry(
            MAPFactionOutpost? outpost,
            out string? failMessage)
        {
            failMessage = null;
            if (outpost == null || !CanPlayerAttack(outpost))
            {
                failMessage =
                    "MAP_MechanoidMechanitor.FactionOutpost.Attack.RelationBlocked".Translate();
                return false;
            }

            Faction? owner = outpost.Faction;
            Faction? player = Faction.OfPlayerSilentFail;
            if (owner == null || player == null)
            {
                failMessage =
                    "MAP_MechanoidMechanitor.FactionOutpost.Attack.RelationBlocked".Translate();
                return false;
            }

            if (owner.HostileTo(player))
            {
                return true;
            }

            int goodwillChange = player.GoodwillToMakeHostile(owner);
            player.TryAffectGoodwillWith(
                owner,
                goodwillChange,
                canSendMessage: true,
                canSendHostilityLetter: true,
                HistoryEventDefOf.AttackedSettlement);

            if (!owner.HostileTo(player))
            {
                failMessage =
                    "MAP_MechanoidMechanitor.FactionOutpost.Attack.TurnHostileFailed".Translate();
                return false;
            }

            return true;
        }
    }
}
