using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(CompDisruptorFlare), "PsychicStun")]
    public static class Patch_CompDisruptorFlare_PsychicStun_PsychicCore
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn)
        {
            return !PsychicCoreUtility.HasPsychicCoreEffect(pawn);
        }
    }

    [HarmonyPatch(
        typeof(Pawn_HealthTracker),
        nameof(Pawn_HealthTracker.AddHediff),
        new[]
        {
            typeof(HediffDef),
            typeof(BodyPartRecord),
            typeof(DamageInfo?),
            typeof(DamageWorker.DamageResult)
        })]
    public static class Patch_PawnHealthTracker_AddHediff_DisruptorFlash_PsychicCore
    {
        [HarmonyPrefix]
        public static bool Prefix(
            Pawn ___pawn,
            HediffDef def,
            ref Hediff? __result)
        {
            if (def == HediffDefOf.DisruptorFlash
                && PsychicCoreUtility.HasPsychicCoreEffect(___pawn))
            {
                __result = null;
                return false;
            }

            return true;
        }
    }
}
