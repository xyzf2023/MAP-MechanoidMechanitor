using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体实例 Stat 的唯一窄入口。只拦截 StatWorker.GetValueUnfinalized：
    /// 固定合体服装读取快照护甲，存在有效活动合体记录的人类读取快照属性；
    /// 其他对象在补丁前缀即安全返回，行为与原版完全一致。
    /// </summary>
    [HarmonyPatch(
        typeof(StatWorker),
        nameof(StatWorker.GetValueUnfinalized))]
    internal static class MechFusionStatWorkerPatch
    {
        private static readonly AccessTools.FieldRef<StatWorker, StatDef>
            StatField = AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");

        public static void Postfix(
            StatWorker __instance,
            StatRequest req,
            ref float __result)
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession)
            {
                return;
            }

            StatDef? stat = StatField(__instance);
            if (stat == null)
            {
                return;
            }

            if (req.Thing is Pawn pawn)
            {
                MechFusionStatUtility.ApplyToPawn(pawn, stat, ref __result);
            }
            else if (req.Thing is Apparel apparel)
            {
                MechFusionStatUtility.ApplyToApparel(apparel, stat, ref __result);
            }
        }
    }

    /// <summary>
    /// 移速必须在原版 FinalizeValue 完成全部容量、地形与最小值处理后覆盖，
    /// 才能真正实现“强制使用机械族速度”。其他 Stat 仍沿用未最终化入口。
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.FinalizeValue))]
    internal static class MechFusionMoveSpeedFinalPatch
    {
        private static readonly AccessTools.FieldRef<StatWorker, StatDef>
            StatField = AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");

        public static void Postfix(
            StatWorker __instance,
            StatRequest req,
            ref float val)
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession
                || StatField(__instance) != StatDefOf.MoveSpeed
                || req.Thing is not Pawn pawn
                || !MechFusionStatUtility.TryGetForcedMoveSpeed(
                    pawn,
                    out float forcedSpeed))
            {
                return;
            }

            val = forcedSpeed;
        }
    }
}
