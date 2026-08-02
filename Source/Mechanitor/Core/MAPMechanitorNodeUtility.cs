using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPMechanitorNodeUtility
    {
        private static readonly Dictionary<ThingDef, CompProperties_MAPMechanitorNode?>
            cachedNodePropsByDef =
                new Dictionary<ThingDef, CompProperties_MAPMechanitorNode?>();

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
        /// 热路径专用的一次性节点配置查询。
        /// 普通机械体仅执行一次 mechanitor 空值判断和一次按 ThingDef 缓存的字典查询；
        /// 原生节点直接读取 Def 上的节点配置，后天机械族机械师仅在自身已有
        /// mechanitor Tracker 时查询注册表，避免在 State 等高频入口重复扫描组件与注册表。
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

            // 后天机械族机械师没有原生节点 Comp，但其正常运行态一定已经拥有
            // mechanitor Tracker。先用 Tracker 作廉价门控，避免普通机械体访问注册表。
            if (pawn.mechanitor != null
                && MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            CompProperties_MAPMechanitorNode? nodeProps = GetCachedNodeProps(pawn.def);
            if (nodeProps == null
                || nodeProps.controlBackend != MAPMechanitorControlBackend.Vanilla)
            {
                return false;
            }

            requiresExternalOverseer = nodeProps.requiresExternalOverseer;
            return true;
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

        public static bool CanControlMechs(Pawn? pawn)
        {
            return PassesMechanitorNodeControllerBasics(pawn)
                && MechanoidMechanitorRoleUtility.UsesVanillaControlPath(pawn);
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.GetExtraMechBandwidth(pawn);
        }

        public static int GetExtraMechControlGroups(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.GetExtraMechControlGroups(pawn);
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
