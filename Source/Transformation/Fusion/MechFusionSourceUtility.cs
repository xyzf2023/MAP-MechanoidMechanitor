using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 源机械族健康计时维护。合体期间依赖原版 WorldPawns 的常规 Tick 推进
    /// Hediff 与限时 Comp，但必须避免源机械族因 mothball 而冻结计时；
    /// 这里只添加一个不参与实际效果的最小维持标记，解除合体时移除。
    /// </summary>
    internal static class MechFusionSourceUtility
    {
        private const string DormantGuardDefName = "MAP_FusionDormantGuard";

        internal static bool IsActiveMergedSource(Pawn? pawn)
        {
            return GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(
                    pawn,
                    out MechFusionSession? session)
                && session != null
                && session.IsActive;
        }

        internal static void ApplyDormantGuard(Pawn? pawn)
        {
            HediffSet? hediffSet = pawn?.health?.hediffSet;
            HediffDef? def = GetGuardDef();
            if (hediffSet == null || def == null || hediffSet.HasHediff(def))
            {
                return;
            }

            pawn!.health.AddHediff(HediffMaker.MakeHediff(def, pawn));
        }

        internal static void RemoveDormantGuard(Pawn? pawn)
        {
            HediffSet? hediffSet = pawn?.health?.hediffSet;
            HediffDef? def = GetGuardDef();
            if (hediffSet == null || def == null)
            {
                return;
            }

            Hediff? hediff = hediffSet.GetFirstHediffOfDef(def);
            if (hediff != null)
            {
                pawn!.health.RemoveHediff(hediff);
            }
        }

        private static HediffDef? GetGuardDef()
        {
            return DefDatabase<HediffDef>.GetNamedSilentFail(
                DormantGuardDefName);
        }
    }

    [HarmonyPatch(typeof(Need_MechEnergy), nameof(Need_MechEnergy.NeedInterval))]
    [HarmonyPriority(Priority.First)]
    internal static class MechFusionSourceEnergySuppressionPatch
    {
        private static readonly AccessTools.FieldRef<Need, Pawn> PawnField =
            AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        public static bool Prefix(Need_MechEnergy __instance)
        {
            Pawn pawn = PawnField(__instance);
            return !MechFusionSourceUtility.IsActiveMergedSource(pawn);
        }
    }
}
