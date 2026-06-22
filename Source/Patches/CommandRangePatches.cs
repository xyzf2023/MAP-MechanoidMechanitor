using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.InMechanitorCommandRange))]
    public static class CommandRangePatches
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn mech, ref bool __result)
        {
            if (mech == null || !ModsConfig.BiotechActive)
            {
                return true;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return true;
            }

            if (MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(mech))
            {
                __result = true;
                return false;
            }

            if (!CompMAPMechanitorNode.TryGetNodeComp(mech, out CompMAPMechanitorNode? nodeComp)
                || nodeComp?.NodeProps?.ignoreExternalOverseerCommandRange != true)
            {
                return true;
            }

            Pawn overseer = mech.GetOverseer();
            if (overseer?.mechanitor == null
                || !overseer.mechanitor.ControlledPawns.Contains(mech))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
