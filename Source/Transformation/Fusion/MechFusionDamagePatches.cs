using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 穿戴合体外甲时禁止附着火焰；直接检查穿戴状态，避免依赖会话初始化顺序。
    /// </summary>
    [HarmonyPatch(typeof(FireUtility), nameof(FireUtility.CanEverAttachFire))]
    internal static class MechFusionPreventFirePatch
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (!__result || t is not Pawn pawn || pawn.apparel == null)
            {
                return;
            }

            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                if (apparel.TryGetComp<CompMechFusionShell>() != null)
                {
                    __result = false;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 结构稳定值伤害路由。顶层 TakeDamage 使用 Prefix/Postfix/Finalizer
    /// 维护深度安全的伤害上下文，异常路径也保证清理且不吞异常。
    /// 真正吸收伤害挂在 DamageWorker_AddInjury 的最终伤口写入点：
    /// 护甲、IncomingDamageFactor 与 injury.Severity 均已确定，
    /// 但尚未调用 pawn.health.AddHediff、尚未修改 DamageResult。
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class MechFusionTakeDamageContextPatch
    {
        public static void Prefix(
            Thing __instance,
            DamageInfo dinfo,
            out MechFusionDamageContext? __state)
        {
            __state = MechFusionDamageContext.BeginTakeDamage(__instance, dinfo);
        }

        public static void Postfix(MechFusionDamageContext? __state)
        {
            MechFusionDamageContext.EndTakeDamage(__state);
        }

        public static Exception? Finalizer(
            Exception? __exception,
            MechFusionDamageContext? __state)
        {
            // 正常路径与异常路径都只能 End 一次；原异常原样返回，绝不吞掉。
            MechFusionDamageContext.EndTakeDamage(__state);
            return __exception;
        }
    }

    /// <summary>
    /// 活动合体外甲的普通耐久豁免。只豁免“具有有效 sessionId、会话处于
    /// Active 且载体引用一致”的固定合体外甲；结构稳定值是唯一承伤资源。
    /// Destroy、强制移除、外部删除等仍会走 PostDestroy 通知并触发异常解除。
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class MechFusionShellDurabilityPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(
            Thing __instance,
            ref DamageWorker.DamageResult __result)
        {
            if (__instance is not Apparel apparel)
            {
                return true;
            }

            CompMechFusionShell? shell =
                apparel.TryGetComp<CompMechFusionShell>();
            if (shell == null || shell.GetActiveSession() == null)
            {
                return true;
            }

            __result = new DamageWorker.DamageResult();
            return false;
        }
    }

    /// <summary>
    /// 最终伤口写入点拦截。护甲和最终严重度已经完成，这里只做两件事：
    /// 累计扣除结构稳定值，以及按一次概率结果决定是否让原版继续写入人类健康状态。
    /// 被结构吸收时跳过整段原版写入，因此不会污染 DamageResult、
    /// 不会添加 additionalHediffsThisPart、也不会重复计算护甲。
    /// </summary>
    [HarmonyPatch(
        typeof(DamageWorker_AddInjury),
        "FinalizeAndAddInjury",
        new[]
        {
            typeof(Pawn),
            typeof(Hediff_Injury),
            typeof(DamageInfo),
            typeof(DamageWorker.DamageResult)
        })]
    internal static class MechFusionInjuryRoutingPatch
    {
        public static bool Prefix(
            Pawn pawn,
            Hediff_Injury injury,
            DamageInfo dinfo,
            DamageWorker.DamageResult result,
            ref float __result)
        {
            if (!MechFusionDamageContext.TryGetForPawn(
                    pawn,
                    out MechFusionDamageContext? context)
                || context == null)
            {
                return true;
            }

            if (dinfo.Def == null
                || dinfo.Def == DamageDefOf.EMP
                || !dinfo.Def.harmsHealth)
            {
                return true;
            }

            float finalDamage = injury.Severity;
            if (finalDamage <= 0f)
            {
                return true;
            }

            bool allowLeak = context.EnsureProbabilityRolled();
            context.StabilityLoss += finalDamage;
            context.StabilitySettled = true;
            context.Session.ConsumeStability(finalDamage);

            if (allowLeak)
            {
                // 允许泄漏：让原版当前伤害继续完整落到人类。
                return true;
            }

            // 不允许泄漏：在原版写入健康状态与 DamageResult 前结束该段伤害。
            __result = 0f;
            return false;
        }
    }
}
