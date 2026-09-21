using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class SunBossHealthProtection
    {
        internal static bool HasProtectedInjury(Pawn pawn)
        {
            CompSunBossState? state = pawn.GetComp<CompSunBossState>();
            if (pawn.Dead || state?.ProtectCore != true) return false;
            // 旧档可以已有部分核心缺失，但每种生存功能必须仍有实际部件，不能复活完全缺失的核心。
            var cores = pawn.health.hediffSet.GetNotMissingParts().Where(CompSunBossState.IsCore).ToList();
            if (!cores.Any(p => p.IsCorePart) || cores.Any(p => SunBossDamageContext.RawHealth(pawn, p) <= 0f)
                || !cores.Any(p => p.def == SunBossDefOf.MAP_SunCoreProcessor)
                || !cores.Any(p => p.def == SunBossDefOf.MAP_SunReactor)
                || !cores.Any(p => p.def == SunBossDefOf.MAP_SunFluidFilter)) return false;
            // 疾病、植入体或其他能力修改造成的死亡不在核心伤势豁免范围内。
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff is Hediff_Injury) continue;
                if (hediff.CapMods?.Any(m => m.capacity == PawnCapacityDefOf.Consciousness
                    || m.capacity == PawnCapacityDefOf.BloodPumping || m.capacity == PawnCapacityDefOf.BloodFiltration) == true)
                    return false;
                if (hediff.Part != null && CompSunBossState.IsCore(hediff.Part)
                    && hediff.CurStage != null && hediff.CurStage.partEfficiencyOffset != 0f)
                    return false;
            }
            return pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                .Any(h => h.Part != null && CompSunBossState.IsCore(h.Part) && h.Severity > 0f);
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDowned))]
    internal static class SunBossDowningPatch
    {
        public static bool Prefix(Pawn ___pawn, ref bool __result)
        {
            if (___pawn.GetComp<CompSunBossState>()?.ProtectCore != true) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "ShouldBeDeadFromLethalDamageThreshold")]
    internal static class SunBossLethalThresholdPatch
    {
        public static bool Prefix(Pawn ___pawn, ref bool __result)
        {
            if (___pawn.GetComp<CompSunBossState>() == null) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDeadFromRequiredCapacity))]
    internal static class SunBossRequiredCapacityPatch
    {
        public static void Postfix(Pawn ___pawn, ref PawnCapacityDef? __result)
        {
            if ((__result == PawnCapacityDefOf.Consciousness || __result == PawnCapacityDefOf.BloodPumping
                || __result == PawnCapacityDefOf.BloodFiltration) && SunBossHealthProtection.HasProtectedInjury(___pawn))
                __result = null;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDead))]
    internal static class SunBossCoreEfficiencyDeathPatch
    {
        public static void Postfix(Pawn_HealthTracker __instance, Pawn ___pawn, ref bool __result)
        {
            if (!__result || !SunBossHealthProtection.HasProtectedInjury(___pawn)) return;
            // 只豁免尚存核心因伤势产生的效率归零，不拦截 Kill 或 Hediff 自身的即死条件。
            if (PawnCapacityUtility.CalculatePartEfficiency(__instance.hediffSet, ___pawn.RaceProps.body.corePart) <= 0.0001f
                && __instance.ShouldBeDeadFromRequiredCapacity() == null
                && !__instance.hediffSet.hediffs.Any(h => h.CauseDeathNow())) __result = false;
        }
    }
}
