using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPMechanitorNodeUtility
    {
        private enum VanillaControlNodeProfile : byte
        {
            NotNode = 0,
            Overseerless = 1,
            RequiresExternalOverseer = 2
        }

        private static readonly Dictionary<ThingDef, CompProperties_MAPMechanitorNode?>
            cachedNodePropsByDef =
                new Dictionary<ThingDef, CompProperties_MAPMechanitorNode?>();

        /// <summary>
        /// CompOverseerSubject.State 等热路径使用的 Pawn 最终节点分类缓存。
        /// 普通机械体的否定结果同样缓存，避免每次 State 查询都重新定位注册表。
        /// 使用 ThingID 作为键，避免静态缓存长期持有 Pawn 引用。
        /// </summary>
        private static readonly Dictionary<int, VanillaControlNodeProfile>
            cachedVanillaControlNodeProfilesByThingId =
                new Dictionary<int, VanillaControlNodeProfile>();

        private static Game? cachedVanillaControlNodeProfileGame;

        public static bool TryGetNodeComp(Pawn? pawn, out CompMAPMechanitorNode? comp)
        {
            return CompMAPMechanitorNode.TryGetNodeComp(pawn, out comp);
        }

        public static bool HasNode(Pawn? pawn)
        {
            return CompMAPMechanitorNode.PawnHasNode(pawn)
                || MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn);
        }

        public static bool UsesVanillaControlPath(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.UsesVanillaControlPath(pawn);
        }

        /// <summary>
        /// 热路径专用的节点档案查询。
        /// 首次遇到 Pawn 时按“后天身份优先、ThingDef 节点配置其次”的权威顺序解析；
        /// 后续按 ThingID 直接读取最终分类。普通机械体也缓存为 NotNode。
        /// </summary>
        public static bool TryGetVanillaControlNodeProfile(
            Pawn? pawn,
            out bool requiresExternalOverseer)
        {
            requiresExternalOverseer = false;
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            EnsureVanillaControlNodeProfileCacheForCurrentGame();

            int thingId = pawn.thingIDNumber;
            if (thingId > 0
                && cachedVanillaControlNodeProfilesByThingId.TryGetValue(
                    thingId,
                    out VanillaControlNodeProfile cachedProfile))
            {
                return ApplyVanillaControlNodeProfile(
                    cachedProfile,
                    out requiresExternalOverseer);
            }

            VanillaControlNodeProfile resolvedProfile =
                ResolveVanillaControlNodeProfile(pawn);

            // 极早期生成阶段可能尚未取得有效 ThingID；此时只返回结果，不写缓存。
            if (thingId > 0)
            {
                cachedVanillaControlNodeProfilesByThingId[thingId] = resolvedProfile;
            }

            return ApplyVanillaControlNodeProfile(
                resolvedProfile,
                out requiresExternalOverseer);
        }

        /// <summary>
        /// 机械族机械师注册表结构发生变化时清除 Pawn 最终分类缓存。
        /// ThingDef 节点配置是静态 Def 数据，不需要随注册表变化清理。
        /// </summary>
        internal static void InvalidateVanillaControlNodeProfileCache()
        {
            cachedVanillaControlNodeProfilesByThingId.Clear();
            cachedVanillaControlNodeProfileGame = Current.Game;
        }

        public static bool IsMechanitorNodeController(Pawn? pawn)
        {
            if (!PassesMechanitorNodeControllerBasics(pawn))
            {
                return false;
            }

            return UsesVanillaControlPath(pawn);
        }

        public static bool IsVanillaRelayMechanitorNode(Pawn? pawn)
        {
            if (!PassesMechanitorNodeControllerBasics(pawn))
            {
                return false;
            }

            return UsesVanillaControlPath(pawn) && RequiresExternalOverseer(pawn);
        }

        public static bool RequiresExternalOverseer(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.RequiresExternalOverseer(pawn);
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.GetExtraMechBandwidth(pawn);
        }

        public static int GetExtraMechControlGroups(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.GetExtraMechControlGroups(pawn);
        }

        private static void EnsureVanillaControlNodeProfileCacheForCurrentGame()
        {
            Game? currentGame = Current.Game;
            if (ReferenceEquals(cachedVanillaControlNodeProfileGame, currentGame))
            {
                return;
            }

            cachedVanillaControlNodeProfilesByThingId.Clear();
            cachedVanillaControlNodeProfileGame = currentGame;
        }

        private static VanillaControlNodeProfile ResolveVanillaControlNodeProfile(
            Pawn pawn)
        {
            // 不以 mechanitor Tracker 是否已建立作为身份前提。
            // 读档或升格初始化的短暂阶段可能已经存在后天身份记录，
            // 但动态 Tracker 尚未补齐；此时仍必须拦截原版 State，避免错误方向查询。
            if (MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn))
            {
                return VanillaControlNodeProfile.Overseerless;
            }

            CompProperties_MAPMechanitorNode? nodeProps = GetCachedNodeProps(pawn.def);
            if (nodeProps == null
                || nodeProps.controlBackend != MAPMechanitorControlBackend.Vanilla)
            {
                return VanillaControlNodeProfile.NotNode;
            }

            return nodeProps.requiresExternalOverseer
                ? VanillaControlNodeProfile.RequiresExternalOverseer
                : VanillaControlNodeProfile.Overseerless;
        }

        private static bool ApplyVanillaControlNodeProfile(
            VanillaControlNodeProfile profile,
            out bool requiresExternalOverseer)
        {
            requiresExternalOverseer =
                profile == VanillaControlNodeProfile.RequiresExternalOverseer;
            return profile != VanillaControlNodeProfile.NotNode;
        }

        private static CompProperties_MAPMechanitorNode? GetCachedNodeProps(ThingDef? def)
        {
            if (def == null)
            {
                return null;
            }

            if (cachedNodePropsByDef.TryGetValue(
                    def,
                    out CompProperties_MAPMechanitorNode? cached))
            {
                return cached;
            }

            CompProperties_MAPMechanitorNode? found = null;
            List<CompProperties>? comps = def.comps;
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i] is CompProperties_MAPMechanitorNode nodeProps)
                    {
                        found = nodeProps;
                        break;
                    }
                }
            }

            cachedNodePropsByDef[def] = found;
            return found;
        }

        private static bool PassesMechanitorNodeControllerBasics(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            return HasNode(pawn);
        }
    }
}
