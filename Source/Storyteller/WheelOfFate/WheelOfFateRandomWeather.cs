using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>只接管当前主题下的自然地表天气；天气状态与期限仍由原版保存。</summary>
    internal static class WheelOfFateRandomWeather
    {
        internal static StoryThemeDef? ThemeFor(Map map, WeatherDecider decider)
        {
            if (map == null || map.weatherManager == null || map.gameConditionManager == null
                || !map.Tile.Valid || map.Tile.LayerDef != PlanetLayerDefOf.Surface
                || map.IsPocketMap || map.generatorDef == null || map.generatorDef.isUnderground
                || TutorSystem.TutorialMode) return null;

            GameComponent_WheelOfFateThemes? state = GameComponent_WheelOfFateThemes.Current;
            state?.UpdateTheme();
            StoryThemeDef? theme = state?.ActiveTheme;
            if (theme?.randomizeNaturalWeather != true
                || theme.randomWeatherDurationTicks.min <= 0
                || theme.randomWeatherDurationTicks.max < theme.randomWeatherDurationTicks.min
                || theme.randomWeatherPool.NullOrEmpty()
                || decider.ForcedWeather != null) return null;
            return theme;
        }
    }

    [HarmonyPatch(typeof(WeatherDecider), nameof(WeatherDecider.StartNextWeather))]
    internal static class WheelOfFateRandomWeatherSelection
    {
        private static bool Prefix(WeatherDecider __instance, Map ___map, ref int ___curWeatherDuration)
        {
            StoryThemeDef? theme = WheelOfFateRandomWeather.ThemeFor(___map, __instance);
            if (theme == null) return true;

            // 显式候选池绕过温度、生态、降水量、禁雨与救火倾向；强制天气已在入口排除。
            // 去重避免重复 XML 条目隐式加权；无有效替代天气时保留原版回退。
            if (!theme.randomWeatherPool.Where(weather => weather != null
                    && weather != ___map.weatherManager.curWeather).Distinct()
                .TryRandomElement(out var next)) return true;

            // 走完整原版生命周期，尤其保留暴雨洪水的创建与销毁，不直接写 curWeather。
            ___map.weatherManager.TransitionTo(next);
            ___curWeatherDuration = theme.randomWeatherDurationTicks.RandomInRange;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeatherDecider), nameof(WeatherDecider.WeatherDeciderTick))]
    internal static class WheelOfFateRandomWeatherTiming
    {
        private static bool Prefix(WeatherDecider __instance, Map ___map, int ___curWeatherDuration)
        {
            StoryThemeDef? theme = WheelOfFateRandomWeather.ThemeFor(___map, __instance);
            if (theme == null) return true;

            // 不因温度不符、大火或亮度变化缩短自然天气；强制天气的过渡时间完全交回原版。
            // 已有天气或新地图初始化的原版期限仅在本次判断中限幅，不每 tick 重新抽期限。
            int duration = Math.Max(theme.randomWeatherDurationTicks.min,
                Math.Min(___curWeatherDuration, theme.randomWeatherDurationTicks.max));
            if (___map.weatherManager.curWeatherAge > duration)
                __instance.StartNextWeather();
            return false;
        }
    }
}
