using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class JusticeScenarioFreeColonistUtility
    {
        /// <summary>
        /// 特殊剧本中符合条件的玩家方机械族机械师（不涉及机械意识宿主身份）。
        /// </summary>
        public static bool IsJusticeScenarioPlayerMechanoidMechanitor(Pawn? pawn)
        {
            if (!GameComponent_JusticeScenarioState.IsEnabled || pawn == null)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer
                || pawn.Dead
                || pawn.Destroyed
                || pawn.IsPrisoner
                || pawn.HostFaction != null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 是否可追加到指定地图的 FreeColonists / FreeColonistsSpawned 列表。
        /// </summary>
        public static bool IsEligibleForMapFreeColonistAppend(
            Pawn? pawn,
            MapPawns mapPawns,
            bool requireSpawned)
        {
            if (!IsJusticeScenarioPlayerMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (!mapPawns.AllPawns.Contains(pawn!))
            {
                return false;
            }

            if (requireSpawned)
            {
                if (!pawn!.Spawned || pawn.Map == null)
                {
                    return false;
                }

                if (pawn.Map.mapPawns != mapPawns)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 是否可在 ColonistBar 远行队分组中作为殖民者头像显示（机械族机械师自动显示）。
        /// </summary>
        public static bool IsEligibleForColonistBarCaravan(Pawn? pawn) =>
            IsJusticeScenarioPlayerMechanoidMechanitor(pawn);

        /// <summary>
        /// 普通玩家机械体是否可在机械族管理表中使用「头像显示」开关。
        /// </summary>
        public static bool CanUsePortraitDisplayToggle(Pawn? pawn)
        {
            if (!GameComponent_JusticeScenarioState.IsEnabled || pawn == null)
            {
                return false;
            }

            if (IsJusticeScenarioPlayerMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer
                || pawn.Dead
                || pawn.Destroyed
                || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 是否因「头像显示」开关而在 ColonistBar 远行队分组中显示头像。
        /// </summary>
        public static bool IsPortraitDisplayEnabledForColonistBar(Pawn pawn) =>
            GameComponent_JusticeScenarioState.IsPortraitDisplayEnabled(pawn)
            && CanUsePortraitDisplayToggle(pawn);

        /// <summary>
        /// ColonistBar 远行队分组中的 IsColonist 兼容判断（不修改 Pawn.IsColonist 本身）。
        /// </summary>
        public static bool CountsAsColonistForCaravanBar(Pawn pawn)
        {
            if (pawn.IsColonist)
            {
                return true;
            }

            if (IsEligibleForColonistBarCaravan(pawn))
            {
                return true;
            }

            return IsPortraitDisplayEnabledForColonistBar(pawn);
        }

        /// <summary>
        /// 全局资格：仅在正义专属剧本启用时有效，且仅允许唯一的机械意识宿主充当自由殖民者替代者。
        /// 另需为机械族机械师、玩家派系成员，并存活、未销毁、非俘虏且无 HostFaction。
        /// </summary>
        public static bool IsEligibleGlobal(Pawn? pawn)
        {
            if (!GameComponent_JusticeScenarioState.IsEnabled || pawn == null)
            {
                return false;
            }

            if (!JusticeScenarioUtility.IsMechanicalConsciousnessHost(pawn)
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer
                || pawn.Dead
                || pawn.HostFaction != null
                || pawn.IsPrisoner
                || pawn.Destroyed)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 地图资格：在全局资格基础上，检查 Pawn 与指定地图的关系。
        /// 仅供唯一机械意识宿主相关逻辑使用。
        /// </summary>
        public static bool IsEligibleOnMap(
            Pawn? pawn,
            MapPawns mapPawns,
            bool requireSpawned)
        {
            if (!IsEligibleGlobal(pawn))
            {
                return false;
            }

            if (requireSpawned)
            {
                if (!pawn!.Spawned || pawn.Map == null)
                {
                    return false;
                }

                if (pawn.Map.mapPawns != mapPawns)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 机械意识宿主是否位于地图、远行队或运输载具等 PawnsFinder 全局存活集合中。
        /// </summary>
        public static bool IsPresentInMapsCaravansAndTransporters(Pawn pawn)
        {
            return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive.Contains(pawn);
        }

        /// <summary>
        /// 后天升格或身份确认后，刷新殖民者栏与相关 Pawn 表格缓存。
        /// </summary>
        public static void NotifyColonistDisplaysDirtyIfReady()
        {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
            {
                return;
            }

            Find.ColonistBar?.MarkColonistsDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
    }
}
