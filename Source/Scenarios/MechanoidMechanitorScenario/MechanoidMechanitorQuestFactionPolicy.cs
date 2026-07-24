using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 任务派系相关的统一判断工具。
    /// 只识别 MOD 的运行时普通派系关系锁（直接修改 FactionDef.permanentEnemy 之外的实现），
    /// 不处理机械巢、玩家派系、隐藏的非普通派系等不属于现有普通派系关系锁的对象。
    /// 所有判断都复用现有的 TryGetLockedOrdinaryFactionRelation，不复制剧本风格/全局模式/自定义设置解析逻辑。
    /// </summary>
    public static class MechanoidMechanitorQuestFactionPolicy
    {
        /// <summary>
        /// 该普通派系与玩家之间是否属于任意锁定的普通派系关系。
        /// 覆盖永久敌对、永久中立、永久盟友以及自定义模式中的对应锁定选项。
        /// 不把普通的初始敌对/中立/盟友关系视为锁定。
        /// </summary>
        public static bool IsGoodwillLockedForPlayer(Faction? faction)
        {
            if (faction == null)
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return false;
            }

            return GameComponent_MechanoidMechanitorStoryState.TryGetLockedOrdinaryFactionRelation(
                player,
                faction,
                out _,
                out _);
        }

        /// <summary>
        /// 该普通派系是否被 MOD 锁定为对玩家永久敌对。
        /// 只有锁定关系为 FactionRelationKind.Hostile 时才返回 true；
        /// 永久中立、永久盟友以及非锁定的普通敌对关系都返回 false。
        /// </summary>
        public static bool IsPermanentlyHostileToPlayer(Faction? faction)
        {
            if (faction == null)
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return false;
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                    .TryGetLockedOrdinaryFactionRelation(
                        player,
                        faction,
                        out _,
                        out _,
                        out FactionRelationKind relationKind))
            {
                return false;
            }

            return relationKind == FactionRelationKind.Hostile;
        }
    }
}
