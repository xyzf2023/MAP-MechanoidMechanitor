using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorStoryConfigurationLabels
    {
        public static string LabelFor(
            MechanoidMechanitorOrdinaryFactionRelationsMode mode)
        {
            return mode switch
            {
                MechanoidMechanitorOrdinaryFactionRelationsMode.Default =>
                    "MAP_MechanoidMechanitor.Story.OrdinaryFactionRelationsMode.Default"
                        .Translate(),
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllHostile =>
                    "MAP_MechanoidMechanitor.Story.OrdinaryFactionRelationsMode.AllHostile"
                        .Translate(),
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile =>
                    "MAP_MechanoidMechanitor.Story.OrdinaryFactionRelationsMode.AllPermanentHostile"
                        .Translate(),
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral =>
                    "MAP_MechanoidMechanitor.Story.OrdinaryFactionRelationsMode.AllPermanentNeutral"
                        .Translate(),
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly =>
                    "MAP_MechanoidMechanitor.Story.OrdinaryFactionRelationsMode.AllAlly"
                        .Translate(),
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly =>
                    "MAP_MechanoidMechanitor.Story.OrdinaryFactionRelationsMode.AllPermanentAlly"
                        .Translate(),
                MechanoidMechanitorOrdinaryFactionRelationsMode.Custom =>
                    "MAP_MechanoidMechanitor.Story.OrdinaryFactionRelationsMode.Custom"
                        .Translate(),
                _ => mode.ToString()
            };
        }

        public static string LabelFor(MechanoidMechanitorFactionRelationOption option)
        {
            return option switch
            {
                MechanoidMechanitorFactionRelationOption.Default =>
                    "MAP_MechanoidMechanitor.Story.FactionRelationOption.Default".Translate(),
                MechanoidMechanitorFactionRelationOption.Hostile =>
                    "MAP_MechanoidMechanitor.Story.FactionRelationOption.Hostile".Translate(),
                MechanoidMechanitorFactionRelationOption.PermanentHostile =>
                    "MAP_MechanoidMechanitor.Story.FactionRelationOption.PermanentHostile"
                        .Translate(),
                MechanoidMechanitorFactionRelationOption.PermanentNeutral =>
                    "MAP_MechanoidMechanitor.Story.FactionRelationOption.PermanentNeutral"
                        .Translate(),
                MechanoidMechanitorFactionRelationOption.Ally =>
                    "MAP_MechanoidMechanitor.Story.FactionRelationOption.Ally".Translate(),
                MechanoidMechanitorFactionRelationOption.PermanentAlly =>
                    "MAP_MechanoidMechanitor.Story.FactionRelationOption.PermanentAlly"
                        .Translate(),
                _ => option.ToString()
            };
        }

        public static string LabelFor(MechanoidMechanitorMechHiveRelationMode mode)
        {
            return mode switch
            {
                MechanoidMechanitorMechHiveRelationMode.Default =>
                    "MAP_MechanoidMechanitor.Story.MechHiveRelationMode.Default".Translate(),
                MechanoidMechanitorMechHiveRelationMode.Neutral =>
                    "MAP_MechanoidMechanitor.Story.MechHiveRelationMode.Neutral".Translate(),
                MechanoidMechanitorMechHiveRelationMode.PermanentNeutral =>
                    "MAP_MechanoidMechanitor.Story.MechHiveRelationMode.PermanentNeutral"
                        .Translate(),
                MechanoidMechanitorMechHiveRelationMode.Ally =>
                    "MAP_MechanoidMechanitor.Story.MechHiveRelationMode.Ally".Translate(),
                _ => mode.ToString()
            };
        }

        public static string EnabledLabel =>
            "MAP_MechanoidMechanitor.Story.Option.Enabled".Translate();

        public static string DisabledLabel =>
            "MAP_MechanoidMechanitor.Story.Option.Disabled".Translate();
    }
}
