namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryConfigurationPreset
    {
        public MechanoidMechanitorOrdinaryFactionRelationsMode ordinaryFactionRelationsMode =
            MechanoidMechanitorOrdinaryFactionRelationsMode.Default;

        public MechanoidMechanitorMechHiveRelationMode mechHiveRelationMode =
            MechanoidMechanitorMechHiveRelationMode.Default;

        public MechanoidMechanitorMechHiveNodeFrequency mechHiveNodeFrequency =
            MechanoidMechanitorMechHiveNodeFrequency.Off;

        public bool purgeDirectiveEnabled;

        public bool symbiosisCovenantEnabled;

        public MechanoidMechanitorStoryConfiguration CreateRuntimeConfiguration()
        {
            return new MechanoidMechanitorStoryConfiguration
            {
                ordinaryFactionRelationsMode = ordinaryFactionRelationsMode,
                mechHiveRelationMode = mechHiveRelationMode,
                mechHiveNodeFrequency = mechHiveNodeFrequency,
                purgeDirectiveEnabled = purgeDirectiveEnabled,
                symbiosisCovenantEnabled = symbiosisCovenantEnabled
            };
        }
    }
}
