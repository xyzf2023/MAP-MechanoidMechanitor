#nullable enable
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 仅供节点和前哨 DEV Gizmo 使用。按正常清理状态结算任务，再调用原版地点销毁。
    /// </summary>
    internal static class DevSiteRemovalUtility
    {
        internal static Gizmo CreateRemoveGizmo(Site site)
        {
            Command_Action remove = new Command_Action
            {
                defaultLabel = "DEV:移除此地点",
                defaultDesc = "将此地点视为已清理，结算关联任务并移除世界地点。仅用于开发者测试。",
                action = () => TryRemove(site)
            };

            string? blockReason = GetBlockReason(site);
            if (blockReason != null)
            {
                remove.Disable(blockReason);
            }

            return remove;
        }

        private static void TryRemove(Site site)
        {
            string? reason = GetBlockReason(site);
            if (reason != null)
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // 标记清理：复用节点/前哨的幂等信件和常规任务通知，不触发普通战斗判定。
            if (site is MAPMechHiveNode node)
            {
                node.DevForceMarkCleaned();
                if (!node.Cleaned)
                {
                    return;
                }
            }
            else if (site is MAPFactionOutpost outpost)
            {
                outpost.DevForceMarkCleaned();
                if (!outpost.Cleaned)
                {
                    return;
                }
            }
            else
            {
                return;
            }

            // 联合行动的正式流程要求已进入战斗就绪阶段。仅 DEV 主动移除时
            // 对仍活动、精确指向此地点的任务补齐成功结算，避免 Destroy 后被判无效。
            QuestPart_SymbiosisCovenantJointOperation? active =
                SymbiosisCovenantJointOperationUtility.FindActiveOperationPart();
            if (active != null && ReferenceEquals(active.targetWorldObject, site))
            {
                active.DevForceCompleteIfTargetCleared(site);
                if (active.IsActive)
                {
                    Log.Error("[MAP-机械族机械师] DEV 地点移除：关联联合行动仍处于活动状态，已取消地点销毁。");
                    return;
                }
            }

            // Destroy 会发出原版 Despawned / Destroyed 任务信号，并按 MapParent 流程卸载地图。
            site.Destroy();
        }

        private static string? GetBlockReason(Site site)
        {
            if (!DebugSettings.ShowDevGizmos
                || site == null
                || !site.Spawned
                || site.Destroyed
                || (site is not MAPMechHiveNode && site is not MAPFactionOutpost))
            {
                return "当前地点不可使用 DEV 移除功能。";
            }

            if (!site.HasMap)
            {
                return null;
            }

            Map map = site.Map;
            if (map == null || map.Disposed || !Find.Maps.Contains(map))
            {
                return "地点地图状态异常，无法安全移除。";
            }

            if (MapBlocksRemoval(map))
            {
                return "地点地图仍有玩家单位、阻止卸载的建筑或运输活动，请先撤离。";
            }

            List<PocketMapParent> pocketMaps = Find.World.pocketMaps;
            if (pocketMaps != null)
            {
                for (int i = 0; i < pocketMaps.Count; i++)
                {
                    PocketMapParent pocket = pocketMaps[i];
                    if (pocket != null && pocket.sourceMap == map && pocket.Map != null
                        && MapBlocksRemoval(pocket.Map))
                    {
                        return "此地点关联的口袋地图仍有玩家单位或其他阻止卸载的内容。";
                    }
                }
            }

            return null;
        }

        private static bool MapBlocksRemoval(Map map)
        {
            if (map.IsPlayerHome
                || map.mapPawns.AnyPawnBlockingMapRemoval
                || map.AnyBuildingBlockingMapRemoval
                || TransporterUtility.IncomingTransporterPreventingMapRemoval(map))
            {
                return true;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return false;
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && !pawn.Destroyed
                    && (pawn.Faction == player || pawn.HostFaction == player))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
