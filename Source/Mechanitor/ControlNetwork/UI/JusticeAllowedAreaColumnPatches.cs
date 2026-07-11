using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 机械师/机械体管理表格「活动区限制」列：无外部监管者的 MAP 机械师节点
    // （如正义）活动区显示与设置入口。
    //
    // 原版 PawnColumnWorker_AllowedArea.DoCell 要求机械体必须有 overseer；
    // 正义无外部 overseer，原版该列为空。本 Prefix 仅对
    // requiresExternalOverseer=false 的 MAP mechanitor node 绘制原版活动区 UI，
    // 其余 pawn 走原版逻辑。
    [HarmonyPatch(typeof(PawnColumnWorker_AllowedArea), nameof(PawnColumnWorker_AllowedArea.DoCell))]
    public static class Patch_PawnColumnWorker_AllowedArea_JusticeNode
    {
        [HarmonyPrefix]
        public static bool Prefix(Rect rect, Pawn pawn, PawnTable table)
        {
            if (!ShouldHandle(pawn))
            {
                return true;
            }

            DrawAllowedAreaCellForMapNode(rect, pawn);
            return false;
        }

        private static bool ShouldHandle(Pawn? pawn)
        {
            return pawn != null
                && ModsConfig.BiotechActive
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && pawn.RaceProps.IsMechanoid
                && MAPMechanitorNodeUtility.HasNode(pawn)
                && MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn);
        }

        private static void DrawAllowedAreaCellForMapNode(Rect rect, Pawn pawn)
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
