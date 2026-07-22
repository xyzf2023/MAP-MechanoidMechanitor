namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryConfigurationPreset
    {
        public MechanoidMechanitorOrdinaryFactionRelationsMode ordinaryFactionRelationsMode =
            MechanoidMechanitorOrdinaryFactionRelationsMode.Default;

        public MechanoidMechanitorMechHiveRelationMode mechHiveRelationMode =
            MechanoidMechanitorMechHiveRelationMode.Default;

        public bool purgeDirectiveEnabled;

        public bool symbiosisCovenantEnabled;

        public MechanoidMechanitorStoryConfiguration CreateRuntimeConfiguration()
        {
            return new MechanoidMechanitorStoryConfiguration
            {
                ordinaryFactionRelationsMode = ordinaryFactionRelationsMode,
                mechHiveRelationMode = mechHiveRelationMode,
                purgeDirectiveEnabled = purgeDirectiveEnabled,
                symbiosisCovenantEnabled = symbiosisCovenantEnabled
            };
        }
    }
}
