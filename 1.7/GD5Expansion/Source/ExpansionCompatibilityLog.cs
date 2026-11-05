using System;
using System.Reflection;
using Verse;

namespace MAP_MechanoidMechanitor.GD5Expansion;

// 独立桥接程序集通过反射复用主 MOD 的日志设置，不增加编译依赖或独立开关。
internal static class ExpansionCompatibilityLog
{
    private static FieldInfo? settingsField;
    private static FieldInfo? startupLoggingField;
    private static FieldInfo? compatibilityLoggingField;

    internal static void Configure(ModContentPack map)
    {
        settingsField = null;
        startupLoggingField = null;
        compatibilityLoggingField = null;
        try
        {
            Type? modType = ReflectionTargets.FindType(map, "MAP_MechanoidMechanitor.MAPMechanitorMod");
            if (modType?.Assembly.GetName().Name != "MAP-MechanoidMechanitor") return;

            FieldInfo? settings = modType.GetField("Settings",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (settings == null || settings.FieldType.Assembly != modType.Assembly
                || settings.FieldType.FullName != "MAP_MechanoidMechanitor.MAPMechanitorModSettings") return;

            startupLoggingField = FindBooleanSetting(settings.FieldType, "enableStartupDetailedLogging");
            compatibilityLoggingField = FindBooleanSetting(settings.FieldType, "enableCompatibilityDetailedLogging");
            settingsField = settings;
        }
        catch (Exception)
        {
            // 日志设置解析失败时按关闭处理，不能阻断兼容初始化或回滚。
            settingsField = null;
            startupLoggingField = null;
            compatibilityLoggingField = null;
        }
    }

    internal static void Startup(string message)
    {
        if (IsEnabled(startupLoggingField))
            Log.Message(ExpansionCompatibilityBootstrap.LogPrefix + message);
    }

    internal static void Warning(string brief, string detail, Exception exception)
    {
        // 与主兼容层一致：关闭详细日志时保留简短失败提示，仅开启时拼接异常。
        Log.Warning(ExpansionCompatibilityBootstrap.LogPrefix + brief
            + (IsEnabled(compatibilityLoggingField) ? "\n" + detail + "\n" + exception : string.Empty));
    }

    private static FieldInfo? FindBooleanSetting(Type settingsType, string name)
    {
        FieldInfo? field = settingsType.GetField(name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        return field?.FieldType == typeof(bool) ? field : null;
    }

    private static bool IsEnabled(FieldInfo? option)
    {
        if (settingsField == null || option == null) return false;
        try
        {
            // 只缓存字段，每次读取当前设置实例，避免重置设置或修改开关后沿用旧值。
            object? settings = settingsField.GetValue(null);
            return settings != null && option.GetValue(settings) is true;
        }
        catch (Exception)
        {
            // 设置未就绪或结构变化时保持静默，失败提示仍由调用方输出。
            return false;
        }
    }
}
