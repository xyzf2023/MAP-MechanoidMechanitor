using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 在 ColonistBar.CheckRecacheEntries 中，为开启「头像显示」的普通玩家机械体
    /// 整合进对应地图分组，并按原版 displayOrder 规则排序。
    /// </summary>
    public static class JusticeScenarioColonistBarPortraitUtility
    {
        private const string LogPrefix = "[MAP_MechanoidMechanitor]";

        private const int ErrorKeyMapGroupNotContiguousBase = 879345200;

        private static readonly List<Map> TmpMaps = new List<Map>();
        private static readonly List<Pawn> TmpPawns = new List<Pawn>();
        private static readonly List<ColonistBar.Entry> TmpEntries = new List<ColonistBar.Entry>();
        private static readonly HashSet<Pawn> TmpPawnSet = new HashSet<Pawn>();
        private static readonly HashSet<Map> TmpMapSet = new HashSet<Map>();

        public static void AppendMapPortraitDisplayEntries(List<ColonistBar.Entry> cachedEntries)
        {
            if (!GameComponent_JusticeScenarioState.IsEnabled)
            {
                return;
            }

            if (cachedEntries == null)
            {
                return;
            }

            CollectMapsNeedingPortraitIntegration(cachedEntries, TmpMaps);

            TmpMaps.Sort((a, b) =>
            {
                TryFindMapGroupSpan(cachedEntries, b, out int startB, out _, out _);
                TryFindMapGroupSpan(cachedEntries, a, out int startA, out _, out _);
                return startB.CompareTo(startA);
            });

            for (int i = 0; i < TmpMaps.Count; i++)
            {
                IntegratePortraitDisplayEntriesForMap(cachedEntries, TmpMaps[i]);
            }
        }

        private static void CollectMapsNeedingPortraitIntegration(
            List<ColonistBar.Entry> cachedEntries,
            List<Map> mapsOut)
        {
            mapsOut.Clear();
            TmpMapSet.Clear();

            IReadOnlyList<Pawn> enabledPawns =
                GameComponent_JusticeScenarioState.PortraitDisplayEnabledPawnsList;
            for (int i = 0; i < enabledPawns.Count; i++)
            {
                Pawn pawn = enabledPawns[i];
                if (!JusticeScenarioFreeColonistUtility.CanUsePortraitDisplayToggle(pawn))
                {
                    continue;
                }

                if (!pawn.Spawned || pawn.Map == null)
                {
                    continue;
                }

                TmpMapSet.Add(pawn.Map);
            }

            foreach (Map map in TmpMapSet)
            {
                if (!TryFindMapGroupSpan(cachedEntries, map, out _, out _, out _))
                {
                    continue;
                }

                mapsOut.Add(map);
            }
        }

        private static void IntegratePortraitDisplayEntriesForMap(
            List<ColonistBar.Entry> cachedEntries,
            Map map)
        {
            if (!TryFindMapGroupSpan(
                    cachedEntries,
                    map,
                    out int startIndex,
                    out int count,
                    out int group))
            {
                return;
            }

            if (!VerifyGroupContinuity(cachedEntries, startIndex, count, group))
            {
                Log.ErrorOnce(
                    $"{LogPrefix} ColonistBar map group {group} for map {map.uniqueID} " +
                    "is not contiguous; skipping portrait integration.",
                    GetMapGroupNotContiguousErrorKey(map.uniqueID, group));
                return;
            }

            TmpPawnSet.Clear();
            for (int i = startIndex; i < startIndex + count; i++)
            {
                Pawn? pawn = cachedEntries[i].pawn;
                if (pawn != null)
                {
                    TmpPawnSet.Add(pawn);
                }
            }

            CollectPortraitPawnsToAddForMap(map, TmpPawnSet, TmpPawns);
            for (int i = 0; i < TmpPawns.Count; i++)
            {
                TmpPawnSet.Add(TmpPawns[i]);
            }

            TmpPawns.Clear();
            foreach (Pawn pawn in TmpPawnSet)
            {
                TmpPawns.Add(pawn);
            }

            if (TmpPawns.Count == 0)
            {
                return;
            }

            EnsureDisplayOrdersInitialized(TmpPawns);
            PlayerPawnsDisplayOrderUtility.Sort(TmpPawns);

            TmpEntries.Clear();
            for (int i = 0; i < TmpPawns.Count; i++)
            {
                TmpEntries.Add(new ColonistBar.Entry(TmpPawns[i], map, group));
            }

            cachedEntries.RemoveRange(startIndex, count);
            cachedEntries.InsertRange(startIndex, TmpEntries);
        }

        private static void CollectPortraitPawnsToAddForMap(
            Map map,
            HashSet<Pawn> existingPawns,
            List<Pawn> additionsOut)
        {
            additionsOut.Clear();

            IReadOnlyList<Pawn> enabledPawns =
                GameComponent_JusticeScenarioState.PortraitDisplayEnabledPawnsList;
            for (int i = 0; i < enabledPawns.Count; i++)
            {
                Pawn pawn = enabledPawns[i];
                if (!JusticeScenarioFreeColonistUtility.CanUsePortraitDisplayToggle(pawn))
                {
                    continue;
                }

                if (!pawn.Spawned || pawn.Map != map)
                {
                    continue;
                }

                if (existingPawns.Contains(pawn))
                {
                    continue;
                }

                additionsOut.Add(pawn);
            }
        }

        private static void EnsureDisplayOrdersInitialized(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.playerSettings == null)
                {
                    continue;
                }

                if (pawn.playerSettings.displayOrder != Pawn_PlayerSettings.UnsetDisplayOrder)
                {
                    continue;
                }

                int maxDisplayOrder = 0;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn? other = pawns[j];
                    if (other?.playerSettings == null)
                    {
                        continue;
                    }

                    if (other.playerSettings.displayOrder > maxDisplayOrder)
                    {
                        maxDisplayOrder = other.playerSettings.displayOrder;
                    }
                }

                pawn.playerSettings.displayOrder = Mathf.Max(maxDisplayOrder, 0) + 1;
            }
        }

        private static bool TryFindMapGroupSpan(
            List<ColonistBar.Entry> cachedEntries,
            Map map,
            out int startIndex,
            out int count,
            out int group)
        {
            startIndex = -1;
            count = 0;
            group = -1;

            for (int i = 0; i < cachedEntries.Count; i++)
            {
                if (cachedEntries[i].map != map)
                {
                    continue;
                }

                startIndex = i;
                group = cachedEntries[i].group;
                break;
            }

            if (startIndex < 0)
            {
                return false;
            }

            for (int i = startIndex; i < cachedEntries.Count; i++)
            {
                ColonistBar.Entry entry = cachedEntries[i];
                if (entry.group != group || entry.map != map)
                {
                    break;
                }

                count++;
            }

            return count > 0;
        }

        private static bool VerifyGroupContinuity(
            List<ColonistBar.Entry> cachedEntries,
            int startIndex,
            int count,
            int group)
        {
            for (int i = 0; i < startIndex; i++)
            {
                if (cachedEntries[i].group == group)
                {
                    return false;
                }
            }

            int endIndex = startIndex + count;
            for (int i = endIndex; i < cachedEntries.Count; i++)
            {
                if (cachedEntries[i].group == group)
                {
                    return false;
                }
            }

            return true;
        }

        private static int GetMapGroupNotContiguousErrorKey(int mapUniqueId, int group)
        {
            return unchecked(ErrorKeyMapGroupNotContiguousBase + mapUniqueId * 31 + group);
        }
    }
}
