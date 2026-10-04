using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class AutonomousMechRepairUtility
    {
        /// <summary>在生成和读档初始化组件前补齐先天自律 Def，开关由原版组件保存。</summary>
        internal static void EnsureInnateRepairableDefs()
        {
            if (!ModsConfig.BiotechActive)
                return;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race?.IsMechanoid != true || def.comps == null)
                    continue;
                bool autonomous = false;
                bool repairable = false;
                foreach (CompProperties props in def.comps)
                {
                    if (props == null)
                        continue;
                    autonomous |= props is CompProperties_AutonomousMech;
                    repairable |= props.compClass != null
                        && typeof(CompMechRepairable).IsAssignableFrom(props.compClass);
                }
                if (autonomous && !repairable)
                    def.comps.Add(new CompProperties_MechRepairable());
            }
        }
    }

    /// <summary>无监管者的玩家自律机械体仍可在管理表中切换原版自动修复状态。</summary>
    [HarmonyPatch(typeof(PawnColumnWorker_AutoRepair), nameof(PawnColumnWorker_AutoRepair.DoCell))]
    internal static class AutonomousMechAutoRepairColumnPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PawnColumnWorker_AutoRepair __instance, Rect rect, Pawn pawn)
        {
            // 原版跳过玩家阵营中无监管者的机械体；只补齐具有自律资格的这一分支。
            if (!ModsConfig.BiotechActive
                || !AutonomousMechUtility.IsPlayerAutonomousMech(pawn)
                || pawn.GetOverseer() != null)
                return true;

            CompMechRepairable? repairable = pawn.TryGetComp<CompMechRepairable>();
            if (repairable == null)
                return true;

            rect.xMin += (rect.width - 24f) / 2f;
            rect.yMin += (rect.height - 24f) / 2f;
            Widgets.Checkbox(rect.position, ref repairable.autoRepair, 24f,
                disabled: false, __instance.def.paintable);
            return false;
        }
    }

    /// <summary>普通自律机械体缺少自动修复按钮时补出原版按钮，不保存第二份开关状态。</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class AutonomousMechAutoRepairGizmoPatch
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            bool eligible = ModsConfig.BiotechActive
                && AutonomousMechUtility.IsPlayerAutonomousMech(__instance)
                && !MechanoidMechanitorSelfWorkModeUtility.HasSelfWorkMode(__instance);
            bool hasAutoRepair = false;
            string autoRepairLabel = eligible ? "CommandAutoRepair".Translate().ToString() : string.Empty;
            foreach (Gizmo gizmo in __result)
            {
                if (eligible && gizmo is Command_Toggle toggle && toggle.defaultLabel == autoRepairLabel)
                    hasAutoRepair = true;
                yield return gizmo;
            }
            if (!eligible || hasAutoRepair)
                yield break;
            CompMechRepairable? repairable = __instance.TryGetComp<CompMechRepairable>();
            if (repairable == null)
                yield break;
            // 使用同一组件的原版按钮、图标和翻译；autoRepair 仍由 PostExposeData 保存。
            foreach (Gizmo gizmo in repairable.CompGetGizmosExtra())
                yield return gizmo;
        }
    }
}
