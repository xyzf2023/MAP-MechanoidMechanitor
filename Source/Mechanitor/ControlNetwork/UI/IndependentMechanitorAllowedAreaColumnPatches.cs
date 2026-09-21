using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 机械师/机械体管理表格「活动区限制」列：自律机械体与独立机械师节点共用入口。
    //
    // 原版 PawnColumnWorker_AllowedArea.DoCell 要求机械体必须有 overseer；
    // 自律机械体无外部 overseer，原版该列为空。本 Prefix 为其绘制原版活动区 UI，
    // 同时保留既有独立节点入口；存储与 AI 限制继续使用原版 playerSettings。
    [HarmonyPatch(typeof(PawnColumnWorker_AllowedArea), nameof(PawnColumnWorker_AllowedArea.DoCell))]
    public static class Patch_PawnColumnWorker_AllowedArea_IndependentMechanitorNode
    {
        [HarmonyPrefix]
        public static bool Prefix(Rect rect, Pawn pawn, PawnTable table)
        {
            if (!ShouldHandle(pawn))
            {
                return true;
            }

            DrawAllowedAreaCell(rect, pawn);
            return false;
        }

        private static bool ShouldHandle(Pawn? pawn)
        {
            return pawn != null
                && pawn.playerSettings != null
                && ModsConfig.BiotechActive
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && pawn.RaceProps.IsMechanoid
                && (AutonomousMechUtility.IsPlayerAutonomousMech(pawn)
                    || (MAPMechanitorNodeUtility.HasNode(pawn)
                        && MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn)
                        && !MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn)));
        }

        private static void DrawAllowedAreaCell(Rect rect, Pawn pawn)
        {
            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            if (pawn.IsMutant && !pawn.mutant.Def.respectsAllowedArea)
            {
                return;
            }

            if (pawn.playerSettings.SupportsAllowedAreas)
            {
                AreaAllowedGUI.DoAllowedAreaSelectors(rect, pawn);
            }
            else if (AnimalPenUtility.NeedsToBeManagedByRope(pawn))
            {
                AnimalPenGUI.DoAllowedAreaMessage(rect, pawn);
            }
            else if (pawn.RaceProps.Dryad)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(rect, "CannotAssignAllowedAreaToDryad".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }
    }
}
