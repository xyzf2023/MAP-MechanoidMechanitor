using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class DataProcessingFusionGizmoPatch
    {
        private static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Dead || __instance.Destroyed
                || !ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                || !DataProcessingOverseerResolver.TryGetWearerSession(
                    __instance, out MechFusionSession? session)
                || !session!.IsActive)
                return;

            Pawn source = session.SourcePawn!;
            Command_Action command = DataProcessingAllocationGizmoUtility.MakeCommand(source);
            command.defaultLabel = "数据处理分配（合体）";
            command.defaultDesc = $"通过当前合体载体管理{source.LabelShortCap}的数据处理分配。"
                + "\n分配记录、意识预算和数据流分发代价仍属于源机械族。"
                + "\n自身分配在本次合体期间冻结，解除合体后才能修改。";
            __result = Append(__result, command);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> original, Gizmo command)
        {
            foreach (Gizmo gizmo in original) yield return gizmo;
            yield return command;
        }
    }

    [HarmonyPatch(typeof(Dialog_DataProcessingAllocationDashboard), "DrawTargetEditor")]
    internal static class DataProcessingFusionSelfEditorPatch
    {
        private static readonly FieldInfo Mode = AccessTools.Field(
            typeof(Dialog_DataProcessingAllocationDashboard), "mode");
        private static readonly object MonitorMode = Enum.Parse(Mode.FieldType, "Monitor");

        private static bool Prefix(
            Dialog_DataProcessingAllocationDashboard __instance,
            Pawn ___overseer, Rect rect, Pawn target)
        {
            if (!DataProcessingOverseerResolver.IsFrozenSelf(___overseer, target)) return true;

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                if (Widgets.ButtonText(new Rect(rect.x, rect.y, 126f, 30f), "返回监控"))
                    Mode.SetValue(__instance, MonitorMode);

                Widgets.Label(new Rect(rect.x, rect.y + 48f, rect.width, rect.height - 48f),
                    "自身分配已冻结\n\n本次合体沿用开始合体时的自身分配档位和特化。"
                    + "其成本仍占用源机械族的数据处理预算。\n\n"
                    + "可以在左侧选择其他机械体继续调整；解除合体后恢复自身分配的编辑与动态调整。");
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(GameComponent_DataProcessingAllocationRegistry),
        nameof(GameComponent_DataProcessingAllocationRegistry.GetCachedDynamicStateLabelForUI))]
    internal static class DataProcessingFusionSelfStatusPatch
    {
        private static void Postfix(Pawn? target, ref string __result)
        {
            if (DataProcessingOverseerResolver.IsFrozenSelf(target, target))
                __result = "合体中：自身分配已冻结";
        }
    }
}
