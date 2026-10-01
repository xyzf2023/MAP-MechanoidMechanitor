using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimTalk
{
    internal static class RimTalkVocalLinkPatch
    {
        private static Func<object?>? getSettings;
        private static FieldInfo? allowNonHumanToTalk;

        internal static void Configure(Func<object?> settingsGetter, FieldInfo allowNonHumanField)
        {
            getSettings = settingsGetter;
            allowNonHumanToTalk = allowNonHumanField;
        }

        internal static void Disable()
        {
            getSettings = null;
            allowNonHumanToTalk = null;
        }

        public static void Postfix(Pawn? __0, ref bool __result)
        {
            // 使用参数位置绑定，避免依赖上游参数名；真实语音链接的原结果始终保留。
            if (__result || getSettings == null || allowNonHumanToTalk == null)
                return;
            if (Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
                return;
            if (!GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(__0)
                || __0!.health?.hediffSet == null
                || __0.RaceProps?.IsMechanoid != true
                || __0.Faction?.IsPlayerSafe() != true
                || __0.IsPrisoner || __0.IsSlave || __0.HostFaction != null
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(__0))
                return;

            try
            {
                // 每次读取当前开关，不缓存值或实例；关闭非人类对话时不得额外放行。
                object? settings = getSettings();
                if (settings != null && allowNonHumanToTalk.GetValue(settings) is bool allowed && allowed)
                    __result = true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] RimTalk 对话开关读取异常，本次保留原资格结果：" + ex,
                    1908263201);
            }
        }
    }
}
