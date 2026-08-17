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

        public int factionOutpostRaidChancePercent =
            MechanoidMechanitorStoryConfiguration
                .DefaultFactionOutpostRaidChancePercent;

        public int factionOutpostSupportChancePercent =
            MechanoidMechanitorStoryConfiguration
                .DefaultFactionOutpostSupportChancePercent;

        public MechanoidMechanitorMechHiveRelationMode mechHiveRelationMode =
            MechanoidMechanitorMechHiveRelationMode.Default;

        public MechanoidMechanitorMechHiveNodeFrequency mechHiveNodeFrequency =
            MechanoidMechanitorMechHiveNodeFrequency.Off;

        public int mechHiveNodeRaidChancePercent =
            MechanoidMechanitorStoryConfiguration
                .DefaultMechHiveNodeRaidChancePercent;

        public int mechHiveNodeSupportChancePercent =
            MechanoidMechanitorStoryConfiguration
                .DefaultMechHiveNodeSupportChancePercent;

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
                factionOutpostRaidChancePercent = factionOutpostRaidChancePercent,
                factionOutpostSupportChancePercent = factionOutpostSupportChancePercent,
                mechHiveRelationMode = mechHiveRelationMode,
                mechHiveNodeFrequency = mechHiveNodeFrequency,
                mechHiveNodeRaidChancePercent = mechHiveNodeRaidChancePercent,
                mechHiveNodeSupportChancePercent = mechHiveNodeSupportChancePercent,
                purgeDirectiveEnabled = purgeDirectiveEnabled,
                symbiosisCovenantEnabled = symbiosisCovenantEnabled
            };
        }
    }
}
