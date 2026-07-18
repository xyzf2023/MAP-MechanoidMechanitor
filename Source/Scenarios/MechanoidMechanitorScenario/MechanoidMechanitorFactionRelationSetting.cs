using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorFactionRelationSetting : IExposable
    {
        public Faction? faction;

        public MechanoidMechanitorFactionRelationOption relationOption =
            MechanoidMechanitorFactionRelationOption.Default;

        public MechanoidMechanitorFactionRelationSetting()
        {
        }

        public MechanoidMechanitorFactionRelationSetting(
            Faction faction,
            MechanoidMechanitorFactionRelationOption relationOption)
        {
            this.faction = faction;
            this.relationOption = relationOption;
        }

        public MechanoidMechanitorFactionRelationSetting CreateCopy()
        {
            return new MechanoidMechanitorFactionRelationSetting
            {
                faction = faction,
                relationOption = relationOption
            };
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(
                ref relationOption,
                "relationOption",
                MechanoidMechanitorFactionRelationOption.Default);
        }
    }
}
