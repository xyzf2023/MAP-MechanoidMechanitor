using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class ImplantEffectUtility
    {
        internal static void RefreshDistributedEffects(Pawn? provider)
        {
            if (provider?.health?.hediffSet?.hediffs == null) return;
            // 增删效果可能修改同一个 Pawn 的健康状态，先取组件快照。
            var effects = new List<HediffComp_DistributedImplantEffect>();
            foreach (Hediff hediff in provider.health.hediffSet.hediffs)
            {
                if (hediff is not HediffWithComps withComps || withComps.comps == null) continue;
                foreach (HediffComp comp in withComps.comps)
                    if (comp is HediffComp_DistributedImplantEffect effect) effects.Add(effect);
            }
            foreach (HediffComp_DistributedImplantEffect effect in effects)
                if (provider.health.hediffSet.hediffs.Contains(effect.parent))
                    effect.RefreshAfterControlChange();
        }

        public static HashSet<Pawn> CollectControlledMechsAndSelf(
            Pawn? provider,
            bool includeMechanoidMechanitorSelf)
        {
            HashSet<Pawn> result = new HashSet<Pawn>();
            FillControlledMechsAndSelf(
                provider,
                includeMechanoidMechanitorSelf,
                result);
            return result;
        }

        /// <summary>
        /// 无分配填充：调用方复用 destination，方法开始时明确清空。
        /// 判定来源与 CollectControlledMechsAndSelf 完全一致，仅允许
        /// 真实受控且通过 IsActualOverseerOf / GetOverseer 校验的目标。
        /// </summary>
        public static void FillControlledMechsAndSelf(
            Pawn? provider,
            bool includeMechanoidMechanitorSelf,
            HashSet<Pawn> destination)
        {
            destination.Clear();
            if (!IsValidProvider(provider))
            {
                return;
            }

            Pawn_MechanitorTracker? mechanitor = provider!.mechanitor;
            if (mechanitor != null)
            {
                List<Pawn>? controlledPawns = mechanitor.ControlledPawns;
                if (controlledPawns != null)
                {
                    for (int i = 0; i < controlledPawns.Count; i++)
                    {
                        Pawn controlledPawn = controlledPawns[i];
                        if (!IsValidMechRecipient(controlledPawn))
                        {
                            continue;
                        }

                        bool isActuallyControlledByProvider;
                        if (MAPMechanitorNodeUtility.HasNode(controlledPawn)
                            && MAPMechanitorNodeUtility.UsesVanillaControlPath(controlledPawn))
                        {
                            isActuallyControlledByProvider =
                                MAPOverseerRelationDirectionUtility.IsActualOverseerOf(
                                    provider,
                                    controlledPawn);
                        }
                        else
                        {
                            isActuallyControlledByProvider =
                                controlledPawn.GetOverseer() == provider;
                        }

                        if (isActuallyControlledByProvider)
                        {
                            destination.Add(controlledPawn);
                        }
                    }
                }
            }

            if (includeMechanoidMechanitorSelf
                && IsValidMechRecipient(provider)
                && MechanoidMechanitorCapabilityUtility.HasCapability(provider, MechanoidMechanitorCapability.ImplantSelfEffects))
            {
                destination.Add(provider);
            }
        }

        public static bool HasHediff(Pawn? pawn, HediffDef? hediffDef)
        {
            return pawn?.health?.hediffSet != null
                && hediffDef != null
                && pawn.health.hediffSet.HasHediff(hediffDef);
        }

        private static bool IsValidProvider(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.health?.hediffSet != null;
        }

        private static bool IsValidMechRecipient(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.RaceProps.IsMechanoid
                && pawn.health?.hediffSet != null;
        }
    }
}
