using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 联合军事行动「自动诊断日志」的只读辅助类。
    /// 仅用于在不改变任何游戏行为的前提下，输出统一前缀的诊断信息，
    /// 帮助定位「进入目标地图后没有触发援军」的真实断点。
    ///
    /// 本类不包含任何业务逻辑：不修改 questTags、不修改 stage、不生成 Pawn / Lord /
    /// 信件 / 奖励，不对任务或地图状态产生任何副作用。所有读取都做了 null 安全处理。
    /// </summary>
    internal static class SymbiosisCovenantJointOperationDiagnostics
    {
        /// <summary>
        /// 所有本次新增诊断日志的统一前缀。必须与原 QuestPart 既有的 JointOpLogPrefix 一致，
        /// 否则会产生两个不同前缀。
        /// </summary>
        internal const string LogPrefix = "[MAP-机械族机械师] 联合行动：";

        /// <summary>
        /// 仅在开发者模式下输出普通诊断 Log.Message。非 DevMode 时零开销、零副作用。
        /// </summary>
        internal static void Log(string eventName, string details)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Verse.Log.Message(LogPrefix + " Event=" + eventName + " | " + details);
        }

        /// <summary>
        /// 仅在开发者模式下输出诊断 Log.Warning。用于「异常状态」提示，同样不改变游戏行为。
        /// </summary>
        internal static void LogWarning(string eventName, string details)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Verse.Log.Warning(LogPrefix + " Event=" + eventName + " | " + details);
        }

        /// <summary>
        /// 安全读取并格式化 WorldObject 的 questTags 列表（只读，不修改）。
        /// 空列表显示 []，非空显示 [tag1, tag2, ...]。
        /// </summary>
        internal static string FormatQuestTags(IEnumerable<string>? questTags)
        {
            if (questTags == null)
            {
                return "[]";
            }

            List<string> list = new List<string>(questTags);
            if (list.Count == 0)
            {
                return "[]";
            }

            return "[" + string.Join(", ", list) + "]";
        }

        /// <summary>
        /// 判断给定 questTags 是否包含目标 tag（只读，不修改）。
        /// </summary>
        internal static bool QuestTagContains(IEnumerable<string>? questTags, string? targetQuestTag)
        {
            if (questTags == null || targetQuestTag == null)
            {
                return false;
            }

            foreach (string tag in questTags)
            {
                if (tag == targetQuestTag)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 只读描述 MapParent 的地图存在情况，不产生任何副作用。
        /// </summary>
        internal static string DescribeMapParent(MapParent? mapParent)
        {
            if (mapParent == null)
            {
                return "mapParentNull=True";
            }

            Map? map = mapParent.Map;
            bool mapNull = map == null;
            bool disposed = !mapNull && map!.Disposed;
            bool inFindMaps = !mapNull && !disposed && Find.Maps.Contains(map);
            string mapId = !mapNull && !disposed ? map!.GetUniqueLoadID() : "-";

            return "hasMap=" + mapParent.HasMap
                + " | mapNull=" + mapNull
                + " | mapDisposed=" + disposed
                + " | findMapsContains=" + inFindMaps
                + " | mapId=" + mapId;
        }

        /// <summary>
        /// 只读统计目标地图上的 Pawn 数量（不含死亡）。只读取，不产生任何副作用。
        /// 返回：已生成 Pawn 总数、Faction == OfPlayer 数量、HostFaction == OfPlayer 数量。
        /// </summary>
        internal static string DescribePlayerPawnCounts(Map? map)
        {
            int total = 0;
            int playerFaction = 0;
            int hostFaction = 0;

            if (map != null && !map.Disposed && Faction.OfPlayer != null)
            {
                Faction player = Faction.OfPlayer;
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null || pawn.Dead)
                    {
                        continue;
                    }

                    total++;
                    if (pawn.Faction == player)
                    {
                        playerFaction++;
                    }
                    else if (pawn.HostFaction == player)
                    {
                        hostFaction++;
                    }
                }
            }

            return "totalPawnCount=" + total
                + " | playerFactionPawnCount=" + playerFaction
                + " | hostFactionIsPlayerPawnCount=" + hostFaction;
        }
    }
}
