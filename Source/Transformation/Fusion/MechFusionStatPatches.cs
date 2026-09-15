using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体实例 Stat 的窄入口：固定合体服装读取快照护甲，存在有效活动
    /// 合体记录的人类读取其余快照属性。专属工作速度单独通过原版装备属性
    /// 入口提供，避免继续表现为人类或健康状态自身的加成。
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
    /// 把合体外甲实例的专属工作速度接入原版服饰 equippedStatOffsets
    /// 所使用的统一读取点。无活动合体会话、非合体外甲或非工作速度时
    /// 立即返回，不影响其他服饰、武器与 Pawn。
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.StatOffsetFromGear))]
    internal static class MechFusionGearStatOffsetPatch
    {
        public static void Postfix(
            Thing gear,
            StatDef stat,
            ref float __result)
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession
                || gear is not Apparel apparel)
            {
                return;
            }

            CompMechFusionShell? shell =
                apparel.TryGetComp<CompMechFusionShell>();
            if (shell != null
                && shell.TryGetWorkSpeedOffset(stat, out float offset))
            {
                __result += offset;
            }
        }
    }

    /// <summary>
    /// 原版属性说明只把 Def 中存在固定 equippedStatOffsets 的服饰列为来源。
    /// 合体外甲的数值来自实例会话，因此在说明阶段把当前外甲补入相关装备，
    /// 使 Pawn 属性详情能够明确显示加成来自“合体外甲”。
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), "RelevantGear")]
    internal static class MechFusionRelevantGearPatch
    {
        public static void Postfix(
            Pawn pawn,
            StatDef stat,
            ref IEnumerable<Thing> __result)
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession
                || pawn == null
                || !MechFusionStatUtility.IsApparelWorkSpeedStat(stat)
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive
                || session.FusionApparel is not Apparel apparel
                || apparel.Wearer != pawn
                || apparel.TryGetComp<CompMechFusionShell>() is not
                    CompMechFusionShell shell
                || !shell.TryGetWorkSpeedOffset(stat, out _))
            {
                return;
            }

            __result = AppendIfMissing(__result, apparel);
        }

        private static IEnumerable<Thing> AppendIfMissing(
            IEnumerable<Thing> original,
            Thing apparel)
        {
            bool alreadyIncluded = false;
            if (original != null)
            {
                foreach (Thing gear in original)
                {
                    if (ReferenceEquals(gear, apparel))
                    {
                        alreadyIncluded = true;
                    }

                    yield return gear;
                }
            }

            if (!alreadyIncluded)
            {
                yield return apparel;
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

    /// <summary>
    /// 合体 Pawn 的最终重量等于人类按原版完整结算后的 Mass，再加上源机械族
    /// 当前最终 Mass。只修改 Pawn 的最终总重量，不把机械体重量写入服装、装备
    /// 或库存，因此不会参与 GearAndInventoryMass 的超负重判定。
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.FinalizeValue))]
    internal static class MechFusionMassFinalPatch
    {
        private static readonly AccessTools.FieldRef<StatWorker, StatDef>
            StatField = AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");

        public static void Postfix(
            StatWorker __instance,
            StatRequest req,
            ref float val)
        {
            if (!GameComponent_MechFusionSessionRegistry.HasAnySession
                || StatField(__instance) != StatDefOf.Mass
                || req.Thing is not Pawn pawn
                || !MechFusionStatUtility.TryGetFusionMassContribution(
                    pawn,
                    out float sourceMass))
            {
                return;
            }

            val = Math.Max(0f, val + sourceMass);
        }
    }
}
