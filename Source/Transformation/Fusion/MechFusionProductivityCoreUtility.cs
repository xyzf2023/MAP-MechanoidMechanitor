using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MechFusionProductivityCoreUtility
    {
        internal static void Capture(MechFusionSession session, Pawn source)
        {
            if (session.ProductivityCoreCaptured) return;

            // 生效标记优先；即使本体也有核心，也不叠加或回退到本体等级。
            Pawn? provider = source;
            if (ImplantEffectUtility.HasHediff(source, MAPMechanitor_HediffDefOf.MAP_ProductivityCoreActive))
            {
                provider = MAPMechanitorNodeUtility.HasNode(source)
                    && MAPMechanitorNodeUtility.UsesVanillaControlPath(source)
                    ? MAPOverseerRelationDirectionUtility.FindActualOverseer(source)
                    : source.GetOverseer();
                if (provider == source) provider = null;
            }

            int level = ProductivityCoreUtility.GetLevel(provider);
            session.CaptureProductivityCore(level, level * ProductivityCoreUtility.WorkSpeedOffsetPerLevel);
        }

        internal static bool IsRunning(MechFusionSession session)
        {
            return session.IsActive && session.ProductivityCoreLevel > 0
                && ImplantEffectUtility.HasHediff(session.WearerPawn,
                    DefDatabase<HediffDef>.GetNamedSilentFail(MechFusionDefNames.BodySynchronizationHediffDefName));
        }

        internal static int GetLevel(Pawn? pawn)
        {
            return pawn != null
                && GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(pawn, out MechFusionSession? session)
                && session != null && IsRunning(session)
                    ? session.ProductivityCoreLevel : 0;
        }
    }
}
