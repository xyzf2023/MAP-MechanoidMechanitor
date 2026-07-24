using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Dialog_NameNewMechHiveFactionAndSettlement
        : Dialog_NamePlayerFactionAndSettlement
    {
        public Dialog_NameNewMechHiveFactionAndSettlement(Settlement settlement)
            : base(settlement)
        {
            nameGenerator = () => NewMechHiveNameUtility.GenerateFactionName(IsValidName, curName);
            curName = nameGenerator();
            secondNameGenerator =
                () => NewMechHiveNameUtility.GenerateSettlementName(IsValidSecondName, curSecondName);
            curSecondName = secondNameGenerator();
        }
    }
}
