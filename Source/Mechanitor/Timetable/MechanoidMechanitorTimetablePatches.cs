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
    /// 候选来自双来源：正式机械族机械师优先走权威 Registry，
    /// 非机械师机械族（如恋人）走真实 CompColonistLikeTimetableUser 标记。
    /// UI getter 不承担初始化副作用：timetable 非空应由对应生命周期保证，
    /// 极端异常下若 timetable 仍为 null，这里安全跳过，绝不在此 new Pawn_TimetableTracker。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Schedule), "get_Pawns")]
    public static class Patch_MainTabWindow_Schedule_Pawns
    {
        // WarningOnce key 基准：与 pawn.thingIDNumber 异或，做到“每个 Pawn 一次”，
        // 而不是整个游戏只有一个 Pawn 能打印 Dev 警告。
        private const int WarningKeyScheduleTimetableNullBase = unchecked((int)0x5449_0001);

        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            Map? currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            // 双来源枚举：
            //  A. 正式机械族机械师来自权威 Registry，避免对地图全部 Pawn 做 capability 聚合查询；
            //  B. 挂载真实 CompColonistLikeTimetableUser 的非机械师机械族（如恋人），
            //     仅作廉价 Race/Faction 过滤 + GetComp 标记，再对命中极少数 Pawn
            //     做 capability 一致性断言，显著降低昂贵查询次数。
            // 使用 HashSet 去重，避免升格后的恋人同时出现在两来源中而重复加入。
            HashSet<Pawn> additions = new HashSet<Pawn>();

            foreach (Pawn pawn in
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors)
            {
                if (IsRegistryScheduleCandidate(pawn, currentMap))
                {
                    additions.Add(pawn);
                }
            }

            IReadOnlyList<Pawn> spawnedPawns = currentMap.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn pawn = spawnedPawns[i];
                if (IsCompScheduleCandidate(pawn, currentMap))
                {
                    additions.Add(pawn);
                }
            }

            if (additions.Count == 0)
            {
                return;
            }

            // 单次 materialize 原 __result，再用 HashSet 去重后合并，避免对 __result 重复枚举。
            List<Pawn> merged = __result as List<Pawn> ?? __result.ToList();
            HashSet<Pawn> existing = new HashSet<Pawn>(merged);
            foreach (Pawn pawn in additions)
            {
                if (!existing.Contains(pawn))
                {
                    merged.Add(pawn);
                }
            }

            __result = merged;
        }

        private static bool IsRegistryScheduleCandidate(Pawn? pawn, Map currentMap)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Map != currentMap)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (pawn.RaceProps?.IsMechanoid != true)
            {
                return false;
            }

            if (pawn.timetable == null)
            {
                WarnScheduleTimetableNull(pawn);
                return false;
            }

            // 一致性断言：正式机械族机械师天然具备该能力；仅在异常情况下才跳过。
            return MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.ColonistLikeTimetable);
        }

        private static bool IsCompScheduleCandidate(Pawn? pawn, Map currentMap)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (pawn.Map != currentMap)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (pawn.RaceProps?.IsMechanoid != true)
            {
                return false;
            }

            // 廉价标记过滤：优先用真实 ThingComp 而非完整 capability 聚合。
            if (pawn.GetComp<CompColonistLikeTimetableUser>() == null)
            {
                return false;
            }

            // 仅对命中 Comp 的极少数 Pawn 做 capability 一致性断言。
            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.ColonistLikeTimetable))
            {
                return false;
            }

            if (pawn.timetable == null)
            {
                WarnScheduleTimetableNull(pawn);
                return false;
            }

            return true;
        }

        private static void WarnScheduleTimetableNull(Pawn pawn)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Log.WarningOnce(
                "[MAP-机械族机械师] Schedule UI 跳过 timetable 为 null 的 "
                + "ColonistLikeTimetable 机械族（生命周期异常，不在 UI 层初始化）："
                + $"{pawn.LabelShort}（{pawn.ThingID}）。",
                WarningKeyScheduleTimetableNullBase ^ pawn.thingIDNumber);
        }
    }
}
