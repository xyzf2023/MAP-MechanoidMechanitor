using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点与玩家的关系判定统一入口。
    /// 关系本体复用机械族（Faction.OfMechanoids）与玩家的派系关系；
    /// 永久（锁定）判定复用剧情配置已有的关系锁定逻辑。
    /// </summary>
    public static class MechHiveNodeRelationUtility
    {
        public static Faction? GetMechHive()
        {
            return MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
        }

        /// <summary>
        /// 玩家与机械巢的当前关系类别。无显式关系记录时按机械族默认（敌对）处理。
        /// </summary>
        public static FactionRelationKind GetCurrentKind()
        {
            Faction? mechHive = GetMechHive();
            Faction? player = Faction.OfPlayerSilentFail;
            if (mechHive == null || player == null)
            {
                return FactionRelationKind.Hostile;
            }

            FactionRelation? relation = player.RelationWith(mechHive, allowNull: true);
            return relation?.kind ?? FactionRelationKind.Hostile;
        }

        public static bool IsHostile()
        {
            return GetCurrentKind() == FactionRelationKind.Hostile;
        }

        public static bool IsAlly()
        {
            return GetCurrentKind() == FactionRelationKind.Ally;
        }

        public static bool IsNeutral()
        {
            return GetCurrentKind() == FactionRelationKind.Neutral;
        }

        /// <summary>
        /// 是否为永久（锁定）关系。复用剧情状态组件已有的锁定判定。
        /// </summary>
        public static bool IsPermanent()
        {
            return GameComponent_MechanoidMechanitorStoryState.HasLockedMechHiveRelation;
        }

        /// <summary>
        /// 玩家是否被允许进攻机械巢节点：
        /// 敌对可直接进攻；普通中立/普通盟友可进攻（需确认并转敌）；
        /// 永久中立/永久盟友不允许进攻。
        /// </summary>
        public static bool CanPlayerAttack()
        {
            if (GetMechHive() == null)
            {
                return false;
            }

            if (IsHostile())
            {
                return true;
            }

            // 中立或盟友：仅在非永久（未锁定）时允许进攻。
            return !IsPermanent();
        }

        /// <summary>进攻前是否需要“将转为敌对”的确认流程（即当前尚未敌对）。</summary>
        public static bool AttackRequiresConfirmation()
        {
            return !IsHostile();
        }

        /// <summary>盟友或永久盟友（可交付材料、可提供援军）。</summary>
        public static bool IsAllyForNode()
        {
            return IsAlly();
        }

        /// <summary>
        /// 执行“进攻转敌”。调用前必须已通过 <see cref="CanPlayerAttack"/> 校验。
        /// 已经敌对时直接返回 true；永久关系返回 false。
        /// </summary>
        public static bool TryTurnHostileForAttack()
        {
            if (IsHostile())
            {
                return true;
            }

            if (IsPermanent())
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null)
            {
                return false;
            }

            return storyState.TryTurnMechHiveHostileFromPlayerAttack();
        }
    }
}
