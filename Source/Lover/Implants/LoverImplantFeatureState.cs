using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人植入体功能的状态控制。
    /// 与机械族机械师脑部植入体功能（MechanoidMechanitorBrainImplantFeatureState）完全独立，
    /// 两个状态类互不直接依赖。
    /// </summary>
    public static class LoverImplantFeatureState
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] LoverImplantFeatureState：";

        public static bool IsInitialized { get; private set; }

        public static bool EnabledForSession { get; private set; }

        public static bool RestartRequired
        {
            get
            {
                if (!IsInitialized)
                {
                    return false;
                }

                bool configured =
                    MAPMechanitorMod.Settings?.enableLoverImplants == true;

                return configured != EnabledForSession;
            }
        }

        internal static void InitializeFromSettings()
        {
            if (IsInitialized)
            {
                return;
            }

            IsInitialized = true;
            EnabledForSession =
                MAPMechanitorMod.Settings?.enableLoverImplants == true;

            if (!EnabledForSession)
            {
                return;
            }

            try
            {
                LoverRecipeImplantRegistrar.Register();
            }
            catch (Exception ex)
            {
                EnabledForSession = false;
                Log.Error(
                    $"{LogPrefix}初始化恋人植入体功能失败：{ex}");
            }
        }
    }
}
