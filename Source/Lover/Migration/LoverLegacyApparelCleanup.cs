using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 旧存档兼容：恋人不再穿戴人类服装后，旧存档里已经穿在恋人身上的衣服会变成“不可见但仍穿着”的幽灵装备。
    /// 本补丁在恋人从旧存档载入（SpawnSetup / respawningAfterLoad）时，将其身上残留的服装取下并放到附近地面，
    /// 不销毁玩家已有衣物，也不影响月亮、人类殖民者或普通机械族。
    /// 该逻辑不在每 Tick 执行，仅发生在载入初始化阶段。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class LoverLegacyApparelCleanup
    {
        private const string LoverDefName = "MAP_Mech_Lover";

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, bool respawningAfterLoad)
        {
            // 仅处理旧存档载入场景，且严格限定为恋人。
            if (!respawningAfterLoad
                || __instance?.def?.defName != LoverDefName)
            {
                return;
            }

            Pawn_ApparelTracker? apparel = __instance.apparel;
            if (apparel == null || apparel.WornApparelCount <= 0)
            {
                return;
            }

            if (!__instance.Spawned || __instance.Map == null)
            {
                return;
            }

            // 复制当前穿着列表后再逐个移除，避免遍历时被修改。
            List<Apparel> worn = apparel.WornApparel.ToList();
            foreach (Apparel item in worn)
            {
                apparel.Remove(item);

                if (item?.Destroyed != false)
                {
                    continue;
                }

                GenPlace.TryPlaceThing(
                    item,
                    __instance.Position,
                    __instance.Map,
                    ThingPlaceMode.Near);
            }

            __instance.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
