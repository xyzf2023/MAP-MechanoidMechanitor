using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 世界目标（据点 / 前哨 / 站点）被摧毁时，通知进行中的肃清评级任务记为成功。
    /// 仅当该 WorldObject 确实被某进行中任务锁定时才生效，对其他销毁事件无副作用。
    /// 该钩子为快速路径；QuestPart.Tick 也会兜底检测目标 Destroyed 状态，二者不冲突。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PurgeDirectiveQuestWorldObjectPatches
    {
        static PurgeDirectiveQuestWorldObjectPatches()
        {
            Harmony harmony = new Harmony("MAP.PurgeDirectiveRating.QuestWorldObject");
            harmony.Patch(
                AccessTools.Method(typeof(WorldObject), nameof(WorldObject.Destroy)),
                postfix: new HarmonyMethod(
                    typeof(PurgeDirectiveQuestWorldObjectPatches),
                    nameof(PostDestroy)));
        }

        public static void PostDestroy(WorldObject __instance)
        {
            PurgeDirectiveQuestPart.NotifyWorldObjectDestroyed(__instance);
        }
    }
}
