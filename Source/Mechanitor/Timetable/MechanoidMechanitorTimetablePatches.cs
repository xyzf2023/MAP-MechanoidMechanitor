using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 作息系统相关的 Harmony 补丁：
    /// 1. 修正 Pawn_TimetableTracker.CurrentAssignment 对机械族机械师的读取（六）。
    /// 2. 在原版 Schedule UI 中追加机械族机械师（七）。
    /// </summary>
    public static class MechanoidMechanitorTimetablePatches
    {
        // 缓存私有字段访问器，避免每次 getter 调用时动态反射 FieldInfo/GetValue。
        // internal 以便同程序集内的 Harmony 补丁类（与此类平级，非嵌套）访问。
        internal static readonly AccessTools.FieldRef<Pawn_TimetableTracker, Pawn> PawnFieldRef =
            AccessTools.FieldRefAccess<Pawn_TimetableTracker, Pawn>("pawn");
    }

    /// <summary>
    /// 原版 Pawn_TimetableTracker.CurrentAssignment 对 !pawn.IsColonist 直接返回 Anything。
    /// 机械族机械师与挂载 ColonistLikeTimetable 组件的非机械师机械族不是 Humanlike Colonist，
    /// 因此仅创建 tracker 不够。本补丁改为查询能力层：仅当 Pawn 具备 ColonistLikeTimetable 能力时
    /// 覆盖原版结果返回其真实小时 Assignment（含第三方自定义 PE_DynamicClass_*，绝不做任何改写）。
    /// 普通人类 / 普通机械族 / 囚犯完全走原版。不修改 Pawn.IsColonist。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_TimetableTracker), "get_CurrentAssignment")]
    public static class Patch_Pawn_TimetableTracker_CurrentAssignment
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_TimetableTracker __instance, ref TimeAssignmentDef __result)
        {
            Pawn? pawn = MechanoidMechanitorTimetablePatches.PawnFieldRef(__instance);
            if (pawn != null
                && MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.ColonistLikeTimetable))
            {
                // 放行其真实小时 Assignment；其余 Pawn 保持原版 Anything / 原版殖民者逻辑。
                __result = __instance.GetAssignment(GenLocalDate.HourOfDay(pawn));
            }
        }
    }

    /// <summary>
    /// 原版 MainTabWindow_Schedule 中显示具备 ColonistLikeTimetable 能力的机械族 Pawn
    /// （含正式机械族机械师，以及挂载 CompColonistLikeTimetableUser 的非机械师机械族）。
    /// 只 Patch Pawns getter，不改动 MapPawns.FreeColonists 等全局集合，
    /// 也不改动 MainTabWindow_PawnTable 的全局行为。
    /// 候选从当前 Map 已生成 Pawn 中按能力层筛选，不再仅依赖 Registry。
    /// UI getter 不承担初始化副作用：timetable 非空应由对应生命周期保证，
    /// 极端异常下若 timetable 仍为 null，这里安全跳过，绝不在此 new Pawn_TimetableTracker。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Schedule), "get_Pawns")]
    public static class Patch_MainTabWindow_Schedule_Pawns
    {
        private const int WarningKeyScheduleTimetableNull = unchecked((int)0x5449_0001);

        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            Map? currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            List<Pawn>? additions = null;
            IReadOnlyList<Pawn> spawnedPawns = currentMap.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn pawn = spawnedPawns[i];
                if (pawn == null
                    || pawn.Destroyed
                    || pawn.Dead
                    || pawn.Map != currentMap)
                {
                    continue;
                }

                if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
                {
                    continue;
                }

                if (pawn.RaceProps?.IsMechanoid != true)
                {
                    continue;
                }

                if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                        pawn,
                        MechanoidMechanitorCapability.ColonistLikeTimetable))
                {
                    continue;
                }

                // 防御性判断：仅控制“本 Patch 是否把该 Pawn 追加进 Schedule 列表”，
                // 不保证 Pawn 不会由 base.MainTabWindow_Schedule.Pawns / FreeColonists 等
                // 其它集合进入 Schedule。timetable 非空应由生命周期保证，而非 UI 层创建。
                // 极端异常下若 timetable 仍为 null，这里安全跳过，不让本 Patch 崩溃。
                if (pawn.timetable == null)
                {
                    if (Prefs.DevMode)
                    {
                        Log.WarningOnce(
                            "[MAP-机械族机械师] Schedule UI 跳过 timetable 为 null 的 "
                            + "ColonistLikeTimetable 机械族（生命周期异常，不在 UI 层初始化）："
                            + $"{pawn.LabelShort}（{pawn.ThingID}）。",
                            WarningKeyScheduleTimetableNull);
                    }

                    continue;
                }

                additions ??= new List<Pawn>();
                additions.Add(pawn);
            }

            if (additions == null || additions.Count == 0)
            {
                return;
            }

            // 去重：避免重复加入原版 FreeColonists 列表中已存在的 Pawn。
            HashSet<Pawn> existing = new HashSet<Pawn>(__result);
            List<Pawn> merged = __result as List<Pawn> ?? __result.ToList();
            foreach (Pawn pawn in additions)
            {
                if (!existing.Contains(pawn))
                {
                    merged.Add(pawn);
                }
            }

            __result = merged;
        }
    }
}
