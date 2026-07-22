namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryConfigurationPreset
    {
        public MechanoidMechanitorOrdinaryFactionRelationsMode ordinaryFactionRelationsMode =
            MechanoidMechanitorOrdinaryFactionRelationsMode.Default;

        public MechanoidMechanitorMechHiveRelationMode mechHiveRelationMode =
            MechanoidMechanitorMechHiveRelationMode.Default;

        public bool purgeDirectiveEnabled;

        public bool gainTrustRouteEnabled;

        public MechanoidMechanitorIdeologyAdaptationLevel ideologyAdaptationLevel =
            MechanoidMechanitorIdeologyAdaptationLevel.Basic;

        public MechanoidMechanitorStoryConfiguration CreateRuntimeConfiguration()
        {
            return new MechanoidMechanitorStoryConfiguration
            {
                ordinaryFactionRelationsMode = ordinaryFactionRelationsMode,
                mechHiveRelationMode = mechHiveRelationMode,
                purgeDirectiveEnabled = purgeDirectiveEnabled,
                gainTrustRouteEnabled = gainTrustRouteEnabled,
                ideologyAdaptationLevel = ideologyAdaptationLevel
            };
        }
    }
}
