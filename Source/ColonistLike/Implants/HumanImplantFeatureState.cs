using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 类人植入体（月亮等授权机械体）功能的状态控制。
    /// 与恋人旧植入体系统（LoverImplantFeatureState）以及机械族机械师脑部植入体系统相互独立。
    /// </summary>
    public static class HumanImplantFeatureState
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] HumanImplantFeatureState：";

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
                    MAPMechanitorMod.Settings?.enableMoonImplants == true;

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
                MAPMechanitorMod.Settings?.enableMoonImplants == true;

            if (!EnabledForSession)
            {
                return;
            }

            try
            {
                HumanImplantRecipeRegistrar.Register();
            }
            catch (Exception ex)
            {
                EnabledForSession = false;
                Log.Error(
                    $"{LogPrefix}初始化类人植入体功能失败：{ex}");
            }
        }
    }
}
