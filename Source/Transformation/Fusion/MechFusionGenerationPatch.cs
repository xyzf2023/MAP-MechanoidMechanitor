using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 通用物品池只接受可独立存在的物品；合体外甲必须由合体事务直接创建。
    /// 原版在 Harmony 静态初始化前建立首次缓存，因此初始化后还需清理一次。
    /// </summary>
    [HarmonyPatch(typeof(ThingSetMakerUtility), nameof(ThingSetMakerUtility.CanGenerate))]
    internal static class MechFusionGenerationPatch
    {
        public static void Postfix(ThingDef thingDef, ref bool __result)
        {
            if (__result && thingDef?.defName == MechFusionDefNames.ShellDefName)
            {
                __result = false;
            }
        }

        internal static void RemoveFromInitialCache()
        {
            ThingSetMakerUtility.allGeneratableItems.RemoveAll(
                thingDef => thingDef?.defName == MechFusionDefNames.ShellDefName);
        }
    }
}
