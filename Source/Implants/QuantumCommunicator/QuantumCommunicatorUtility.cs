using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class QuantumCommunicatorUtility
    {
        public static bool HasImplant(Pawn? pawn)
        {
            return ImplantEffectUtility.HasHediff(
                pawn,
                MAPMechanitor_HediffDefOf.MAP_QuantumCommunicator);
        }

        public static bool HasEffect(Pawn? pawn)
        {
            return HasImplant(pawn)
                || MechFusionMechanitorSynchronizationService
                    .GrantsQuantumCommunicatorEffect(pawn);
        }

        public static bool GrantsCommandRangeBypass(Pawn? mech)
        {
            if (mech == null)
            {
                return false;
            }

            Pawn? overseer = mech.GetOverseer();
            return overseer != null
                && !overseer.Destroyed
                && !overseer.Dead
                && HasEffect(overseer);
        }
    }

    [HarmonyPatch(
        typeof(Pawn_MechanitorTracker),
        nameof(Pawn_MechanitorTracker.DrawCommandRadius))]
    public static class QuantumCommunicatorDrawCommandRadiusPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn_MechanitorTracker __instance)
        {
            return !QuantumCommunicatorUtility.HasEffect(__instance.Pawn);
        }
    }
}
