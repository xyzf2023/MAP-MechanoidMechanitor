using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 在 ColonistBar.CheckRecacheEntries 中，为开启「头像显示」的普通玩家机械体追加地图头像条目。
    /// </summary>
    public static class JusticeScenarioColonistBarPortraitUtility
    {
        private static readonly FieldInfo CachedEntriesField =
            AccessTools.Field(typeof(ColonistBar), "cachedEntries");

        public static void AppendMapPortraitDisplayEntries(ColonistBar bar)
        {
            if (!GameComponent_JusticeScenarioState.IsEnabled)
            {
                return;
            }

            if (CachedEntriesField.GetValue(bar) is not List<ColonistBar.Entry> cachedEntries)
            {
                return;
            }

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

                if (ContainsPawn(cachedEntries, pawn))
                {
                    continue;
                }

                Map map = pawn.Map;
                if (!TryGetGroupForMap(cachedEntries, map, out int group))
                {
                    continue;
                }

                RemoveNullPlaceholderForMap(cachedEntries, map, group);
                cachedEntries.Add(new ColonistBar.Entry(pawn, map, group));
            }
        }

        private static bool ContainsPawn(List<ColonistBar.Entry> cachedEntries, Pawn pawn)
        {
            for (int i = 0; i < cachedEntries.Count; i++)
            {
                if (cachedEntries[i].pawn == pawn)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetGroupForMap(
            List<ColonistBar.Entry> cachedEntries,
            Map map,
            out int group)
        {
            for (int i = 0; i < cachedEntries.Count; i++)
            {
                if (cachedEntries[i].map == map)
                {
                    group = cachedEntries[i].group;
                    return true;
                }
            }

            group = -1;
            return false;
        }

        private static void RemoveNullPlaceholderForMap(
            List<ColonistBar.Entry> cachedEntries,
            Map map,
            int group)
        {
            for (int i = cachedEntries.Count - 1; i >= 0; i--)
            {
                ColonistBar.Entry entry = cachedEntries[i];
                if (entry.map == map && entry.group == group && entry.pawn == null)
                {
                    cachedEntries.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
