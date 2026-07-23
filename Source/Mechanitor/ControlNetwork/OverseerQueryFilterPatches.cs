using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 查询层修复：MAP 机械族机械师节点的 Overseer 方向解析。
    //
    // 原版 Overseer reflexive=true，GetFirstDirectRelationPawn 不判断方向。
    // 正义/后天节点既是机械体又是机械师；隐者同时有上级与下属。
    // 权威解析下沉到 GetFirstDirectRelationPawn；GetOverseer 自然跟随。
    // 不修改底层 DirectRelations、控制组或带宽逻辑。
    [HarmonyPatch(
        typeof(Pawn_RelationsTracker),
        nameof(Pawn_RelationsTracker.GetFirstDirectRelationPawn),
        new[] { typeof(PawnRelationDef), typeof(Predicate<Pawn>) })]
    public static class Patch_Pawn_RelationsTracker_GetFirstDirectRelationPawn_MAPOverseer
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn ___pawn,
            PawnRelationDef def,
            Predicate<Pawn>? predicate,
            ref Pawn? __result)
        {
            if (def != PawnRelationDefOf.Overseer || ___pawn == null)
            {
                return;
            }

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.HasNode(___pawn)
                || !MAPMechanitorNodeUtility.UsesVanillaControlPath(___pawn))
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.RequiresExternalOverseer(___pawn))
            {
                __result = null;
                return;
            }

            __result = MAPOverseerRelationDirectionUtility.FindActualOverseer(
                ___pawn,
                predicate);
        }
    }
}
