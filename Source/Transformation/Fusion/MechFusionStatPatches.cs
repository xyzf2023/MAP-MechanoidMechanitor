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
}
