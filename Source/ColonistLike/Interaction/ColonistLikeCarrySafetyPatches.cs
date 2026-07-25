using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 三层防护之第二层：直接阻止 FloatMenuOptionProvider_CarryPawn 在“执行者 = 目标 = 同一机械体”
    // 时生成自我搬运选项。自我搬运毫无合法意义，故直接返回 null 且不创建 CarryDownedPawnDrafted Job。
    [HarmonyPatch]
    public static class ColonistLikeCarrySelfOptionBlocker
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] ColonistLikeCarrySafetyPatches：";

        // 解析失败时使用一次独立错误键。
        private const int ErrorKeyCarryProviderResolve = 889345101;

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = AccessTools.Method(
                typeof(FloatMenuOptionProvider_CarryPawn),
                "GetSingleOptionFor",
                new[] { typeof(Pawn), typeof(FloatMenuContext) });

            if (method == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 FloatMenuOptionProvider_CarryPawn.GetSingleOptionFor(Pawn, FloatMenuContext)，自我搬运选项阻断补丁未应用。",
                    ErrorKeyCarryProviderResolve);
            }

            return method;
        }

        [HarmonyPrefix]
        public static bool Prefix(
            Pawn? clickedPawn,
            FloatMenuContext? context,
            ref FloatMenuOption? __result)
        {
            if (clickedPawn == null || context == null)
            {
                return true;
            }

            // 执行者与目标为同一 Pawn 引用时，自我搬运没有合法意义，直接不显示该选项。
            if (ReferenceEquals(clickedPawn, context.FirstSelectedPawn))
            {
                __result = null;
                return false;
            }

            return true;
        }
    }

    // 三层防护之第三层（bool 返回值重载）：在携带系统底层拒绝“Pawn 携带自身”的非法请求，
    // 阻止循环 ParentHolder / holdingOwner 链形成，避免查询 MapHeld 或根持有者时无限循环卡死。
    [HarmonyPatch]
    public static class ColonistLikeCarryTrackerSelfGuardBool
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] ColonistLikeCarrySafetyPatches：";

        private const int ErrorKeyTryStartCarryBoolResolve = 889345102;
        private const int ErrorKeySelfCarryBool = 889345104;

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = AccessTools.Method(
                typeof(Pawn_CarryTracker),
                nameof(Pawn_CarryTracker.TryStartCarry),
                new[] { typeof(Thing) });

            if (method == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 Pawn_CarryTracker.TryStartCarry(Thing)，自我携带底层防护（bool）未应用。",
                    ErrorKeyTryStartCarryBoolResolve);
            }

            return method;
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn_CarryTracker __instance, Thing? item, ref bool __result)
        {
            if (item != null && ReferenceEquals(item, __instance.pawn))
            {
                Log.ErrorOnce(
                    $"{LogPrefix}检测到非法的 Pawn 自我携带请求，已拒绝：{__instance.pawn}",
                    ErrorKeySelfCarryBool);
                __result = false;
                return false;
            }

            return true;
        }
    }

    // 三层防护之第三层（int 返回值重载）：同上，覆盖 TryStartCarry(Thing, int, bool) 入口。
    [HarmonyPatch]
    public static class ColonistLikeCarryTrackerSelfGuardInt
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] ColonistLikeCarrySafetyPatches：";

        private const int ErrorKeyTryStartCarryIntResolve = 889345103;
        private const int ErrorKeySelfCarryInt = 889345105;

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = AccessTools.Method(
                typeof(Pawn_CarryTracker),
                nameof(Pawn_CarryTracker.TryStartCarry),
                new[] { typeof(Thing), typeof(int), typeof(bool) });

            if (method == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 Pawn_CarryTracker.TryStartCarry(Thing, int, bool)，自我携带底层防护（int）未应用。",
                    ErrorKeyTryStartCarryIntResolve);
            }

            return method;
        }

        [HarmonyPrefix]
        public static bool Prefix(
            Pawn_CarryTracker __instance,
            Thing? item,
            int count,
            bool reserve,
            ref int __result)
        {
            if (item != null && ReferenceEquals(item, __instance.pawn))
            {
                Log.ErrorOnce(
                    $"{LogPrefix}检测到非法的 Pawn 自我携带请求，已拒绝：{__instance.pawn}",
                    ErrorKeySelfCarryInt);
                __result = 0;
                return false;
            }

            return true;
        }
    }
}
