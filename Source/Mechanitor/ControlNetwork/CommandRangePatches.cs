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
        public static bool Prefix(Pawn mech, LocalTargetInfo target, ref bool __result)
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

            if (AutonomousMechUtility.IsPlayerAutonomousMech(mech))
            {
                __result = true;
                return false;
            }

            if (ProxySubchainUtility.TryGetHeldCommandOrigin(
                    mech,
                    out Map? commandMap,
                    out IntVec3 commandOrigin))
            {
                __result = commandMap != null
                    && mech.MapHeld == commandMap
                    && target.Cell.InBounds(commandMap)
                    && (float)commandOrigin.DistanceToSquared(target.Cell) < 620.01f;
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

    /// <summary>
    /// 原版 AnySelectedDraftedMechs 通过 selectedPawn.GetOverseer() == pawn 判断
    /// 当前机械师是否监管选中的已征召机械体。Overseer 是 reflexive 关系，
    /// MAP 机械族机械师既能被监管又能监管其他机械体时，该查询可能把关系方向读反，
    /// 从而在选中其上级机械师时错误绘制自身指挥范围。
    /// 对 MAP 原版控制路径节点改为以实际控制组方向判断；普通机械师保持原版逻辑。
    /// </summary>
    [HarmonyPatch(
        typeof(Pawn_MechanitorTracker),
        nameof(Pawn_MechanitorTracker.AnySelectedDraftedMechs),
        MethodType.Getter)]
    public static class Patch_Pawn_MechanitorTracker_AnySelectedDraftedMechs_MAPDirection
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn ___pawn, ref bool __result)
        {
            if (!ModsConfig.BiotechActive
                || !MAPMechanitorNodeUtility.IsMechanitorNodeController(___pawn))
            {
                return true;
            }

            List<Pawn> selectedPawns = Find.Selector.SelectedPawns;
            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn selectedPawn = selectedPawns[i];
                if (selectedPawn != null
                    && selectedPawn.Drafted
                    && MAPOverseerRelationDirectionUtility.IsActualOverseerOf(
                        ___pawn,
                        selectedPawn))
                {
                    __result = true;
                    return false;
                }
            }

            __result = false;
            return false;
        }
    }
}
