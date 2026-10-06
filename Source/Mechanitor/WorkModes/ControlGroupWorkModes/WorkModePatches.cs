using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 切换/分配工作模式时同步对应健康状态
    [HarmonyPatch(typeof(MechanitorControlGroup), "SetWorkModeForPawn", new[] { typeof(Pawn), typeof(MechWorkModeDef) })]
    public static class Patch_MechanitorControlGroup_SetWorkModeForPawn
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, MechWorkModeDef workMode)
        {
            MechanoidMechanitorWorkModeUtility.ApplyWorkModeHediff(pawn, workMode);
        }
    }

    // 控制组1的模式变化时，同步机械族机械师自身的健康状态
    [HarmonyPatch(typeof(MechanitorControlGroup), "SetWorkMode", new[] { typeof(MechWorkModeDef), typeof(GlobalTargetInfo) })]
    public static class Patch_MechanitorControlGroup_SetWorkMode
    {
        [HarmonyPostfix]
        public static void Postfix(MechanitorControlGroup __instance)
        {
            MechanoidMechanitorWorkModeUtility.SyncMechanitorWithPrimaryControlGroup(__instance);
        }
    }

    // 载入后补齐已分配机械体的健康状态
    [HarmonyPatch(typeof(MechanitorControlGroup), "ExposeData")]
    public static class Patch_MechanitorControlGroup_ExposeData
    {
        [HarmonyPostfix]
        public static void Postfix(MechanitorControlGroup __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            List<AssignedMech> assigned = __instance.AssignedMechs;
            if (assigned == null)
            {
                return;
            }

            for (int i = 0; i < assigned.Count; i++)
            {
                Pawn pawn = assigned[i].pawn;
                if (pawn != null)
                {
                    MechanoidMechanitorWorkModeUtility.ApplyWorkModeHediff(pawn, __instance.WorkMode);
                }
            }

            MechanoidMechanitorWorkModeUtility.SyncMechanitorWithPrimaryControlGroup(__instance);
        }
    }

    // 仅在机械族机械师控制组显示自定义工作模式
    [HarmonyPatch(typeof(MechanitorControlGroupGizmo), "GetWorkModeOptions")]
    public static class Patch_MechanitorControlGroupGizmo_GetWorkModeOptions
    {
        [HarmonyPrefix]
        public static bool Prefix(MechanitorControlGroup controlGroup, ref IEnumerable<FloatMenuOption> __result)
        {
            bool isMechanoidMechanitor =
                MechanoidMechanitorWorkModeUtility.IsMechanoidMechanitorControlGroup(controlGroup);

            IEnumerable<MechWorkModeDef> defs = DefDatabase<MechWorkModeDef>.AllDefsListForReading
                .Where(d => !MechanoidMechanitorWorkModeUtility.IsMechanoidMechanitorSelfOnlyWorkMode(d));
            // 关闭设置与未研究时采用同一过滤规则；不修改控制组当前模式。
            if (!isMechanoidMechanitor
                || !(MAPMechanitorMod.Settings?.enableExtraWorkModes ?? true)
                || !ResearchFeatureUnlockUtility.IsQuantumTaskComputationUnlocked())
            {
                defs = defs.Where(d => !MechanoidMechanitorWorkModeUtility.IsMechanoidMechanitorWorkMode(d));
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (MechWorkModeDef wm in defs.OrderBy(d => d.uiOrder))
            {
                options.Add(new FloatMenuOption(wm.LabelCap, delegate
                {
                    controlGroup.SetWorkMode(wm);
                }, wm.uiIcon, Color.white, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0, HorizontalJustification.Left, false)
                {
                    tooltip = new TipSignal?(new TipSignal(wm.description, (int)wm.index ^ 234784353))
                });
            }

            __result = options;
            return false;
        }
    }
}
