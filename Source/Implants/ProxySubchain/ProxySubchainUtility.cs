using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class ProxySubchainUtility
    {
        private static readonly Dictionary<Pawn, int> supportedHolderTransitionDepth =
            new Dictionary<Pawn, int>();

        public static bool HasImplant(Pawn? pawn)
        {
            return ImplantEffectUtility.HasHediff(
                pawn,
                MAPMechanitor_HediffDefOf.MAP_ProxySubchain);
        }

        public static bool CanMaintainControl(Pawn? pawn)
        {
            if (!IsEligibleHost(pawn))
            {
                return false;
            }

            if (pawn!.Spawned)
            {
                return true;
            }

            if (pawn.MapHeld == null)
            {
                return false;
            }

            if (pawn.ParentHolder is Pawn_CarryTracker carryTracker)
            {
                return carryTracker.pawn != null && carryTracker.pawn.Spawned;
            }

            return pawn.ParentHolder is Building_CryptosleepCasket casket
                && casket.Spawned;
        }

        public static Pawn? BeginSupportedHolderTransition(Thing? thing)
        {
            if (thing is not Pawn pawn || !IsEligibleHost(pawn))
            {
                return null;
            }

            supportedHolderTransitionDepth.TryGetValue(pawn, out int depth);
            supportedHolderTransitionDepth[pawn] = depth + 1;
            return pawn;
        }

        public static void EndSupportedHolderTransition(Pawn? pawn)
        {
            if (pawn == null || !supportedHolderTransitionDepth.TryGetValue(pawn, out int depth))
            {
                return;
            }

            if (depth <= 1)
            {
                supportedHolderTransitionDepth.Remove(pawn);
                return;
            }

            supportedHolderTransitionDepth[pawn] = depth - 1;
        }

        public static bool ShouldPreserveControlDuringCurrentHolderTransition(Pawn? pawn)
        {
            return IsEligibleHost(pawn)
                && pawn != null
                && supportedHolderTransitionDepth.ContainsKey(pawn);
        }

        public static bool TryGetHeldCommandOrigin(
            Pawn? mech,
            out Map? map,
            out IntVec3 origin)
        {
            map = null;
            origin = IntVec3.Invalid;

            Pawn? overseer = mech == null
                ? null
                : MAPOverseerRelationDirectionUtility.FindActualOverseer(mech);
            if (overseer == null
                || overseer.Spawned
                || !CanMaintainControl(overseer)
                || overseer.mechanitor?.ControlledPawns?.Contains(mech!) != true)
            {
                return false;
            }

            map = overseer.MapHeld;
            origin = overseer.PositionHeld;
            return map != null && origin.IsValid;
        }

        private static bool IsEligibleHost(Pawn? pawn)
        {
            return ModsConfig.BiotechActive
                && pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && !pawn.IsPrisoner
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && pawn.mechanitor != null
                && MechanitorUtility.IsMechanitor(pawn)
                && HasImplant(pawn);
        }
    }
}
