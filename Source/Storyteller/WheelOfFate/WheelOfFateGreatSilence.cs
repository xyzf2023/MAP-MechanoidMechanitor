using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅限制叙事者的自然周期生成，保留任务、队列和外部逻辑的事件执行入口。</summary>
    [HarmonyPatch(typeof(RimWorld.Storyteller), nameof(RimWorld.Storyteller.MakeIncidentsForInterval),
        new[] { typeof(StorytellerComp), typeof(List<IIncidentTarget>) })]
    internal static class WheelOfFateGreatSilence
    {
        private static bool Prefix(RimWorld.Storyteller __instance, StorytellerComp comp,
            ref IEnumerable<FiringIncident> __result)
        {
            if (__instance != Find.Storyteller) return true;
            GameComponent_WheelOfFateThemes? state = GameComponent_WheelOfFateThemes.Current;
            // 在生成前更新，避免本 tick 主题到期或刚切入时仍使用旧状态。
            state?.UpdateTheme();
            if (state?.ActiveTheme?.suppressRandomIncidents != true) return true;

            // 固定时间剧情、重要任务引导及袭击信标的专用威胁属于其他逻辑。
            // 黑衣人和废料虫袭通过通知入队，任务组件由无参重载单独处理，均不在拦截范围。
            if (IsScriptedComponent(comp)) return true;

            // 不枚举原生成器，避免随机抽选、生成副作用或写入事件历史。
            __result = Array.Empty<FiringIncident>();
            return false;
        }

        private static void Postfix(RimWorld.Storyteller __instance, StorytellerComp comp,
            ref IEnumerable<FiringIncident> __result)
        {
            if (__instance != Find.Storyteller || IsScriptedComponent(comp)) return;
            StoryThemeDef? theme = GameComponent_WheelOfFateThemes.Current?.ActiveTheme;
            if (theme != null && (theme.onlyPositiveRandomIncidents || theme.blockPositiveRandomIncidents))
                __result = AllowedIncidents(__result, theme);
        }

        // 主随机池在抽选前过滤；此处覆盖疾病、远行队等独立随机组件。
        // 只枚举一次，不重试或执行事件，也不拦截 TryFire 和事件队列。
        private static IEnumerable<FiringIncident> AllowedIncidents(
            IEnumerable<FiringIncident> incidents, StoryThemeDef theme)
        {
            foreach (FiringIncident incident in incidents)
                if (incident?.def != null && theme.AllowsRandomIncident(incident.def))
                    yield return incident;
        }

        private static bool IsScriptedComponent(StorytellerComp comp) =>
            comp is StorytellerComp_SingleOnceFixed
            || comp is StorytellerComp_ImportantQuest
            || comp is StorytellerComp_ThreatsGenerator;
    }
}
