using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 虫巢追杀（Pursuit）模式的通用判定与地图选择工具。
    /// 只负责“是否启用”“目标地图是否合法”“能否承载虫灾”等与调度相关的纯判定，
    /// 不负责具体事件执行与状态持久化（状态由 GameComponent_MechanoidMechanitorInsectPursuitManager 持有）。
    /// </summary>
    public static class MechanoidMechanitorInsectPursuitUtility
    {
        /// <summary>
        /// 追杀模式是否真正启用。
        /// 必须同时读取剧情配置中的虫巢关系模式，不能通过“虫巢当前真实 Hostile”判断，
        /// 因为 Default 模式下虫巢本来也可能是 Hostile。
        /// </summary>
        public static bool IsPursuitActive()
        {
            if (Current.Game == null
                || !GameComponent_MechanoidMechanitorStoryState.HasActiveConfiguration)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? state =
                Current.Game
                    .GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (state == null)
            {
                return false;
            }

            if (!state.TryGetInsectRelationMode(
                    out MechanoidMechanitorInsectRelationMode mode))
            {
                return false;
            }

            return mode == MechanoidMechanitorInsectRelationMode.Pursuit;
        }

        /// <summary>
        /// 判断地图是否可以作为追杀事件的目标。
        /// 优先保持简单：普通玩家 Home 地图可用；Odyssey 启用时，玩家逆重飞船当前所在/落地地图也可作为目标。
        /// 不要求在地图上拥有普通人类殖民者（机械族机械师 MOD 不能假设殖民地一定存在人类殖民者）。
        /// </summary>
        public static bool IsEligibleTargetMap(Map map)
        {
            if (map == null || !Find.Maps.Contains(map))
            {
                return false;
            }

            if (map.IsPlayerHome)
            {
                return true;
            }

            if (ModsConfig.OdysseyActive
                && TryGetPlayerGravEngine(map) != null)
            {
                return true;
            }

            return false;
        }

        public static bool HasAnyEligibleTargetMap()
        {
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                if (IsEligibleTargetMap(Find.Maps[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从当前合法目标地图中选择一个。优先当前地图，否则在合法地图中随机选择。
        /// pending 到期时才选地图（不在每日抽签时提前锁定）。
        /// </summary>
        public static Map? ChooseEligibleMap()
        {
            Map? current = Find.CurrentMap;
            if (current != null && IsEligibleTargetMap(current))
            {
                return current;
            }

            Map? fallback = null;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map candidate = Find.Maps[i];
                if (!IsEligibleTargetMap(candidate))
                {
                    continue;
                }

                fallback = candidate;
                if (Rand.Value < 1f / (Find.Maps.Count + 1f))
                {
                    return candidate;
                }
            }

            return fallback;
        }

        /// <summary>
        /// 当前地图是否有资格“此刻开始一次 Pursuit 追猎”（仅用于 Hunt Warning 阶段筛选）。
        /// 至少检查：
        /// 1. 原版虫灾自身资格：虫族派系存在；
        /// 2. 原版虫灾自身资格：已有 Hive 数量 &lt; 30；
        /// 3. 原版 InfestationCellFinder 有合法位置，或（Pursuit 设置允许时）存在无厚岩顶 fallback 位置。
        ///
        /// 注意：此处的 InfestationCellFinder.TryFindCell 调用发生在 CanFireNowSub 作用域之外，
        /// 不会触发 fallback postfix，因此返回的是原版真实结果。
        /// 真正攻击阶段仍必须重新调用 TryPrepareManagedInfestation，因为 8~12 小时内地图可能变化。
        /// </summary>
        public static bool CanCurrentlyHostManagedInfestation(Map map)
        {
            if (map == null)
            {
                return false;
            }

            // 原版虫灾自身资格：虫族派系必须存在。
            if (Faction.OfInsects == null)
            {
                return false;
            }

            // 原版虫灾自身资格：已有 Hive 数量必须小于 30。
            if (HiveUtility.TotalSpawnedHivesCount(map) >= 30)
            {
                return false;
            }

            if (InfestationCellFinder.TryFindCell(out _, map))
            {
                return true;
            }

            if (MAPMechanitorMod.Settings != null
                && MAPMechanitorMod.Settings.pursuitAllowInfestationWithoutThickRoof
                && MechanoidMechanitorInsectPursuitInfestationUtility
                    .TryFindFallbackCell(map, out _))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 当前地图是否挂在一个进行中的虫族追猎（用于 Alert 显示）。
        /// </summary>
        public static bool IsActiveHuntOnMap(
            GameComponent_MechanoidMechanitorInsectPursuitManager manager,
            Map map)
        {
            return manager != null
                && map != null
                && manager.ActiveHuntMap == map
                && manager.ActiveHuntAttackTick >= 0;
        }

        /// <summary>
        /// 获取玩家逆重飞船引擎（Odyssey）。仅在 Odyssey 启用时有效，避免非 Odyssey 环境因引用
        /// Odyssey 类型而产生类型加载问题。
        /// </summary>
        public static Building_GravEngine? TryGetPlayerGravEngine(Map map)
        {
            if (map == null || !ModsConfig.OdysseyActive)
            {
                return null;
            }

            return GravshipUtility.GetPlayerGravEngine_NewTemp(map);
        }
    }
}
