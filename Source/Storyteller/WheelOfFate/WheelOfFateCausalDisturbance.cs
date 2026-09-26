using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>成功发生事件后立即尝试一次连带事件；运行时防重入，不产生存档队列。</summary>
    [HarmonyPatch(typeof(RimWorld.Storyteller), nameof(RimWorld.Storyteller.TryFire))]
    internal static class WheelOfFateCausalDisturbance
    {
        private static bool firingLinkedIncident;

        private static void Postfix(RimWorld.Storyteller __instance, FiringIncident fi, bool __result)
        {
            if (!__result || firingLinkedIncident || __instance != Find.Storyteller
                || fi?.parms?.target == null) return;
            GameComponent_WheelOfFateThemes? state = GameComponent_WheelOfFateThemes.Current;
            state?.UpdateTheme();
            if (state?.ActiveTheme?.triggerLinkedIncident != true) return;

            // 保护整个选取和执行过程，包含其他补丁或 Worker 引发的嵌套事件。
            firingLinkedIncident = true;
            try
            {
                StorytellerComp_WheelOfFateRandomMain? comp = __instance.storytellerComps
                    .OfType<StorytellerComp_WheelOfFateRandomMain>().FirstOrDefault();
                if (comp == null) return;
                if (comp.UniformCandidates(fi.parms.target, excluded: fi.def)
                    .TryRandomElement(out var linked))
                    __instance.TryFire(linked);
            }
            catch (Exception exception)
            {
                // 原事件已经成功并记入历史，连带失败不改变其返回值，也不重试。
                Log.Error($"[MAP-机械族机械师] 因果扰动连带事件失败：{exception}");
            }
            finally
            {
                firingLinkedIncident = false;
            }
        }
    }
}
