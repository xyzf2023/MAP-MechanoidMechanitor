using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 结构稳定值伤害路由。只在一次顶层 TakeDamage 事件的 AddHediff
    /// 入口拦截实际最终伤害；非合体 Pawn 零行为变化，EMP 完全绕过。
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class MechFusionTakeDamageContextPatch
    {
        public static void Prefix(Thing __instance, DamageInfo dinfo)
        {
            MechFusionDamageContext.BeginTakeDamage(__instance, dinfo);
        }

        public static void Postfix(Thing __instance)
        {
            MechFusionDamageContext.EndTakeDamage(__instance);
        }
    }

    [HarmonyPatch(
        typeof(Pawn_HealthTracker),
        nameof(Pawn_HealthTracker.AddHediff),
        new[]
        {
            typeof(Hediff),
            typeof(BodyPartRecord),
            typeof(DamageInfo?),
            typeof(DamageWorker.DamageResult)
        })]
    internal static class MechFusionInjuryRoutingPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn>
            PawnField = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>(
                "pawn");

        public static bool Prefix(
            Pawn_HealthTracker __instance,
            Hediff hediff,
            DamageInfo? dinfo)
        {
            if (hediff is not Hediff_Injury)
            {
                return true;
            }

            Pawn pawn = PawnField(__instance);
            if (!MechFusionDamageContext.TryGetForPawn(
                    pawn,
                    out MechFusionDamageContext? context)
                || context == null)
            {
                return true;
            }

            if (dinfo.HasValue && dinfo.Value.Def == DamageDefOf.EMP)
            {
                return true;
            }

            float finalDamage = hediff.Severity;
            if (finalDamage <= 0f)
            {
                return true;
            }

            context.StabilityLoss += finalDamage;
            context.StabilitySettled = true;
            context.Session.ConsumeStability(finalDamage);
            return context.AllowDamageLeak;
        }
    }
}
