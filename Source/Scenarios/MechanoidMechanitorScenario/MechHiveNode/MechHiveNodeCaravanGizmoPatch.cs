using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 为停留在建设中机械巢节点地块、且满足交付条件的远行队追加“提供材料”Gizmo。
    /// 窄补丁：只在 <see cref="Caravan.GetGizmos"/> 尾部按需追加，不改变其他远行队行为。
    /// </summary>
    [HarmonyPatch(typeof(Caravan), "GetGizmos")]
    public static class MechHiveNodeCaravanGizmoPatch
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.IsStoryConfigurationActive)
            {
                yield break;
            }

            Gizmo? deliver = MechHiveNodeCaravanInteraction.TryGetDeliverGizmo(__instance);
            if (deliver != null)
            {
                yield return deliver;
            }
        }
    }
}
