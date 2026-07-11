using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorBrainImplantFeatureState
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] MechanoidMechanitorBrainImplantFeatureState：";

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
                    MAPMechanitorMod.Settings?.enableMechanoidMechanitorBrainImplants == true;
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
                MAPMechanitorMod.Settings?.enableMechanoidMechanitorBrainImplants == true;

            if (!EnabledForSession)
            {
                return;
            }

            try
            {
                MechanoidMechanitorRecipeImplantRegistrar.Register();
            }
            catch (Exception ex)
            {
                EnabledForSession = false;
                Log.Error($"{LogPrefix}初始化脑部植入体兼容功能失败：{ex}");
            }
        }
    }
}
