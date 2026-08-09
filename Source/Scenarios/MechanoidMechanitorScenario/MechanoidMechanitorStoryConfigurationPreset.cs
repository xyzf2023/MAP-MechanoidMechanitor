namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryConfigurationPreset
    {
        public MechanoidMechanitorOrdinaryFactionRelationsMode ordinaryFactionRelationsMode =
            MechanoidMechanitorOrdinaryFactionRelationsMode.Default;

        public MechanoidMechanitorFactionOutpostFrequency factionOutpostFrequency =
            MechanoidMechanitorFactionOutpostFrequency.Off;

        public int hostileFactionOutpostWeight = 100;

        public int allyFactionOutpostWeight = 100;

        public int neutralFactionOutpostWeight = 100;

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
                factionOutpostFrequency = factionOutpostFrequency,
                hostileFactionOutpostWeight = hostileFactionOutpostWeight,
                allyFactionOutpostWeight = allyFactionOutpostWeight,
                neutralFactionOutpostWeight = neutralFactionOutpostWeight,
                mechHiveRelationMode = mechHiveRelationMode,
                mechHiveNodeFrequency = mechHiveNodeFrequency,
                purgeDirectiveEnabled = purgeDirectiveEnabled,
                symbiosisCovenantEnabled = symbiosisCovenantEnabled
            };
        }
    }
}
