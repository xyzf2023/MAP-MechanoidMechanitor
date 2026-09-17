using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>合体瞬间保存供能资格；减耗由机体同调在会话扣能入口提供。</summary>
    internal static class MechFusionVoidEngineUtility
    {
        [ThreadStatic] private static Pawn? energyQueryPawn;

        internal static void Capture(MechFusionSession session, Pawn source)
        {
            if (session.VoidEngineCaptured) return;
            bool authorized = ImplantEffectUtility.HasHediff(source, MAPMechanitor_HediffDefOf.MAP_VoidEngine)
                || ImplantEffectUtility.HasHediff(source, MAPMechanitor_HediffDefOf.MAP_ReactorSelfPowering);
            if (!authorized)
            {
                Pawn? overseer = MAPMechanitorNodeUtility.HasNode(source)
                    && MAPMechanitorNodeUtility.UsesVanillaControlPath(source)
                    ? MAPOverseerRelationDirectionUtility.FindActualOverseer(source)
                    : source.GetOverseer();
                if (ImplantEffectUtility.HasHediff(overseer, MAPMechanitor_HediffDefOf.MAP_VoidEngine))
                {
                    var recipients = new HashSet<Pawn>();
                    ImplantEffectUtility.FillControlledMechsAndSelf(overseer, false, recipients);
                    authorized = recipients.Contains(source);
                }
            }
            session.CaptureVoidEngine(authorized);
        }

        internal static bool IsRunning(MechFusionSession session)
        {
            return session.IsActive && session.VoidEngineAuthorized
                && ImplantEffectUtility.HasHediff(session.WearerPawn,
                    DefDatabase<HediffDef>.GetNamedSilentFail(MechFusionDefNames.BodySynchronizationHediffDefName));
        }

        internal static float ConsumptionFactor(MechFusionSession session) => IsRunning(session) ? 0.3f : 1f;

        // 这些机械能源属性不通过人类的通用属性快照结算；旧快照也使用相同过滤。
        internal static bool IsEnergyStat(StatDef stat) => stat.defName == "MechEnergyUsageFactor"
            || stat.defName == "MechEnergyLossPerHP";

        internal static float GetUsageFactor(Pawn source, StatDef stat)
        {
            Pawn? previous = energyQueryPawn;
            energyQueryPawn = source;
            try
            {
                // StatRequest 重载绕开 Pawn 属性缓存，防止读取旧值或污染普通形态的缓存。
                return stat.Worker.GetValue(StatRequest.For(source));
            }
            finally
            {
                energyQueryPawn = previous;
            }
        }

        internal static bool ExcludeStage(Pawn pawn, StatDef stat, HediffStage stage)
        {
            return energyQueryPawn == pawn && stat.defName == "MechEnergyUsageFactor"
                && (MAPMechanitor_HediffDefOf.MAP_VoidEngine?.stages?.Contains(stage) == true
                    || MAPMechanitor_HediffDefOf.MAP_ReactorSelfPowering?.stages?.Contains(stage) == true);
        }
    }

    // 仅在上述合体专用属性查询作用域内跳过两种供能状态，保留其他属性来源及最终处理。
    [HarmonyPatch(typeof(HediffStatsUtility), nameof(HediffStatsUtility.GetStatFactorForSeverity))]
    internal static class MechFusionVoidEngineFactorPatch
    {
        public static bool Prefix(StatDef stat, HediffStage stage, Pawn pawn, ref float __result)
        {
            if (!MechFusionVoidEngineUtility.ExcludeStage(pawn, stat, stage)) return true;
            __result = 1f;
            return false;
        }
    }

    [HarmonyPatch(typeof(HediffStatsUtility), nameof(HediffStatsUtility.GetStatOffsetForSeverity))]
    internal static class MechFusionVoidEngineOffsetPatch
    {
        public static bool Prefix(StatDef stat, HediffStage stage, Pawn pawn, ref float __result)
        {
            if (!MechFusionVoidEngineUtility.ExcludeStage(pawn, stat, stage)) return true;
            __result = 0f;
            return false;
        }
    }
}
