using System.Collections.Generic;
using HarmonyLib;
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

            if (QuantumCommunicatorUtility.GrantsCommandRangeBypass(mech))
            {
                __result = true;
                return false;
            }

            if (DataProcessingAllocationUtility.HasCommandRangeBypass(mech))
            {
                __result = true;
                return false;
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

            // 只解析一次实际监管者，避免依赖 GetOverseer 的方向补丁。
            Pawn? overseer =
                MAPOverseerRelationDirectionUtility.FindActualOverseer(mech);
            Pawn_MechanitorTracker? tracker = overseer?.mechanitor;
            List<Pawn>? controlledPawns = tracker?.ControlledPawns;

            if (controlledPawns == null || !controlledPawns.Contains(mech))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
