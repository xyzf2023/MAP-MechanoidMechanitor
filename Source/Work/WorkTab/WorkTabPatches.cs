using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MainTabWindow_Work), "get_Pawns")]
    public static class WorkTabPatches
    {
        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            if (__result == null)
            {
                return;
            }

            __result = WorkTabPawnListUtility.AppendEligiblePawns(__result);
        }
    }

    /// <summary>
    /// 原版与第三方工作窗口共用的名单追加规则；查询不初始化 Pawn 或改写工作设置。
    /// </summary>
    internal static class WorkTabPawnListUtility
    {
        internal static IEnumerable<Pawn> AppendEligiblePawns(IEnumerable<Pawn> source)
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            if (settings == null || !settings.addMechanoidMechanitorsToWorkTab)
            {
                return source;
            }

            Map? map = Find.CurrentMap;
            if (map == null)
            {
                return source;
            }

            // 不修改窗口或其他 MOD 提供的原始名单；剧本已追加的成员保持原样。
            List<Pawn> pawns = source.ToList();
            HashSet<Pawn> existing = new HashSet<Pawn>(pawns);

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!CompWorkTabVisibleUser.PawnCanShowInWorkTab(pawn))
                {
                    continue;
                }

                if (pawn.guest == null || pawn.workSettings?.Initialized != true)
                    continue;

                if (existing.Add(pawn))
                {
                    pawns.Add(pawn);
                }
            }

            return pawns;
        }

        internal static void NotifyPawnsChangedIfReady()
        {
            if (Current.ProgramState == ProgramState.Playing && Current.Game != null)
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
    }
}
