using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed partial class GameComponent_MechanoidMechanitorRegistry
    {
        private Pawn? legacyScenarioProtagonistForMigration;

        partial void ExposeLegacyScenarioProtagonistMigration()
        {
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Scribe_References.Look(
                    ref legacyScenarioProtagonistForMigration,
                    "mechanoidMechanitorScenarioProtagonist");
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MergeLegacyScenarioProtagonistIntoHost();
                legacyScenarioProtagonistForMigration = null;
            }
        }

        private void MergeLegacyScenarioProtagonistIntoHost()
        {
            Pawn? legacy = legacyScenarioProtagonistForMigration;

            if (mechanicalConsciousnessHost == null && legacy != null)
            {
                mechanicalConsciousnessHost = legacy;
                return;
            }

            if (mechanicalConsciousnessHost != null
                && legacy != null
                && !ReferenceEquals(mechanicalConsciousnessHost, legacy))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 旧存档中剧本主角与机械意识宿主引用不一致；保留机械意识宿主。");
            }
        }
    }
}
