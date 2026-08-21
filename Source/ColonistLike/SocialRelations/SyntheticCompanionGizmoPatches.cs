using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 统一在 Pawn.GetGizmos 之后追加动态授权仿生伴侣 Gizmo。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class SyntheticCompanionGizmoPatches
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (__instance == null)
            {
                yield break;
            }

            foreach (Gizmo gizmo in SyntheticCompanionGizmoUtility.GetGizmos(__instance))
            {
                yield return gizmo;
            }
        }
    }
}
