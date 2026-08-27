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
    /// 机械族机械师不是 Humanlike Colonist，因此仅创建 tracker 不够。
    /// 本补丁仅当其 owner 为机械族机械师时覆盖原版结果，普通人类 / 普通机械族 / 囚犯完全走原版。
    /// 不修改 Pawn.IsColonist。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_TimetableTracker), "get_CurrentAssignment")]
    public static class Patch_Pawn_TimetableTracker_CurrentAssignment
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_TimetableTracker __instance, ref TimeAssignmentDef __result)
        {
            Pawn? pawn = MechanoidMechanitorTimetablePatches.PawnFieldRef(__instance);
            if (pawn != null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                // 放行其真实小时 Assignment；其余 Pawn 保持原版 Anything / 原版殖民者逻辑。
                __result = __instance.GetAssignment(GenLocalDate.HourOfDay(pawn));
            }
        }
    }

    /// <summary>
    /// 原版 MainTabWindow_Schedule 中显示机械族机械师。
    /// 只 Patch Pawns getter，不改动 MapPawns.FreeColonists 等全局集合，
    /// 也不改动 MainTabWindow_PawnTable 的全局行为。
    /// 候选从 Registry.CurrentRegisteredMechanitors 读取，不每次打开 UI 全地图扫描。
    /// UI getter 不承担初始化副作用，不在此调用 EnsureRoleState（必须由生命周期提前保证）。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Schedule), "get_Pawns")]
    public static class Patch_MainTabWindow_Schedule_Pawns
    {
        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            Map? currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;

            List<Pawn>? additions = null;
            for (int i = 0; i < registered.Count; i++)
            {
                Pawn pawn = registered[i];
                if (pawn == null
                    || pawn.Destroyed
                    || pawn.Dead
                    || !pawn.Spawned
                    || pawn.Map != currentMap)
                {
                    continue;
                }

                if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
                {
                    continue;
                }

                // timetable 必须由生命周期（EnsureRoleState）提前保证存在；
                // 极端异常下若仍为 null，安全跳过，不让 UI 崩溃。
                if (pawn.timetable == null)
                {
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
