using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>只保存玩家修改过的数值；未覆盖的参数始终读取当前 XML 默认值。</summary>
    public sealed class WheelOfFateSettings : IExposable
    {
        private List<WheelOfFateValueOverride> overrides = new List<WheelOfFateValueOverride>();
        private readonly Dictionary<string, WheelOfFateValueOverride> index =
            new Dictionary<string, WheelOfFateValueOverride>();

        private static string Identity(string theme, string key) => theme + "/" + key;

        public float Value(StoryThemeDef theme, string key, float xmlDefault,
            float minimum = 0f, float maximum = float.MaxValue)
        {
            return index.TryGetValue(Identity(theme.defName, key), out var entry)
                && StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(entry.value)
                ? Mathf.Clamp(entry.value, minimum, maximum) : xmlDefault;
        }

        public void SetValue(StoryThemeDef theme, string key, float value, float xmlDefault)
        {
            if (!StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(value)) return;
            string identity = Identity(theme.defName, key);
            if (Mathf.Approximately(value, xmlDefault))
            {
                if (index.TryGetValue(identity, out var old))
                {
                    overrides.Remove(old);
                    index.Remove(identity);
                }
                return;
            }
            if (!index.TryGetValue(identity, out var entry))
            {
                entry = new WheelOfFateValueOverride { themeDefName = theme.defName, parameter = key };
                overrides.Add(entry);
                index.Add(identity, entry);
            }
            entry.value = value;
        }

        public void ResetTheme(StoryThemeDef theme)
        {
            overrides.RemoveAll(entry => entry.themeDefName == theme.defName);
            foreach (string key in index.Where(pair => pair.Value.themeDefName == theme.defName)
                .Select(pair => pair.Key).ToList())
                index.Remove(key);
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref overrides, "overrides", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            overrides ??= new List<WheelOfFateValueOverride>();
            index.Clear();
            // 保留未加载 DLC/MOD 的有效名称，重新启用后仍能恢复覆盖值。
            overrides.RemoveAll(entry => entry == null || entry.themeDefName.NullOrEmpty()
                || entry.parameter.NullOrEmpty()
                || !StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(entry.value));
            foreach (var entry in overrides)
                index[Identity(entry.themeDefName, entry.parameter)] = entry;
            overrides = index.Values.ToList();
        }
    }

    public sealed class WheelOfFateValueOverride : IExposable
    {
        public string themeDefName = "";
        public string parameter = "";
        public float value;

        public void ExposeData()
        {
            Scribe_Values.Look(ref themeDefName, "themeDefName", "");
            Scribe_Values.Look(ref parameter, "parameter", "");
            Scribe_Values.Look(ref value, "value", 0f);
        }
    }
}
