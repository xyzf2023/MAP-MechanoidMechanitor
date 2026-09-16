using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    ///     机械族机械师：在 MechanitorUtility.GetMechGizmos 后置补丁中继续追加分配 Gizmo。
    ///     人类机械师由独立补丁在 Pawn.GetGizmos 中处理，避免重复 Gizmo。
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
    public static class Patch_MechanitorUtility_GetMechGizmos_DataProcessingAllocation
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn mech)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (DataProcessingAllocationGizmoUtility.ShouldShowForMechanoid(mech))
            {
                yield return DataProcessingAllocationGizmoUtility.MakeCommand(mech);
            }
        }
    }

    /// <summary>
    ///     人类机械师（安装并行思维接口）：在 Pawn.GetGizmos 后置补丁中追加“意识分配” Gizmo。
    ///     机械体跳过，避免与机械族补丁重复。
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_HumanInterfaceDataProcessingAllocation
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            // 机械体已由另一补丁处理，跳过避免重复 Gizmo。
            if (__instance.RaceProps.IsMechanoid)
            {
                yield break;
            }

            if (DataProcessingAllocationGizmoUtility.ShouldShowForHumanInterfaceMechanitor(__instance))
            {
                yield return DataProcessingAllocationGizmoUtility.MakeCommand(__instance);
            }

            if (!__instance.Dead && !__instance.Destroyed
                && ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                && DataProcessingOverseerResolver.TryGetWearerSession(__instance, out MechFusionSession? session)
                && session!.IsActive)
            {
                Pawn source = session.SourcePawn!;
                Command_Action command = DataProcessingAllocationGizmoUtility.MakeCommand(source);
                command.defaultLabel = "数据处理分配（合体）";
                command.defaultDesc = $"通过当前合体载体管理{source.LabelShortCap}的数据处理分配。"
                    + "\n分配记录、意识预算和数据流分发代价仍属于源机械族。"
                    + "\n自身分配在本次合体期间冻结，解除合体后才能修改。";
                yield return command;
            }
        }
    }
}
