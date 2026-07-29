using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(MainButtonWorker_ToggleMechTab),
        nameof(MainButtonWorker_ToggleMechTab.Disabled),
        MethodType.Getter)]
    public static class MAPMechMainButtonPatches
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            // 原版已经认为按钮可用时，不需要执行额外判断。
            if (!__result || !ModsConfig.BiotechActive)
            {
                return;
            }

            Map? currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                return;
            }

            // 只读取 MOD 自己维护的机械族机械师缓存，避免在 UI 高频路径中扫描整张地图的 Pawn。
            IReadOnlyList<Pawn> registeredMechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;

            if (MapHasVisibleMapNode(currentMap, registeredMechanitors))
            {
                __result = false;
            }
        }

        private static bool MapHasVisibleMapNode(
            Map currentMap,
            IReadOnlyList<Pawn> registeredMechanitors)
        {
            for (int i = 0; i < registeredMechanitors.Count; i++)
            {
                Pawn? pawn = registeredMechanitors[i];
                if (pawn == null
                    || pawn.Destroyed
                    || pawn.Dead
                    || pawn.MapHeld != currentMap)
                {
                    continue;
                }

                if (MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
