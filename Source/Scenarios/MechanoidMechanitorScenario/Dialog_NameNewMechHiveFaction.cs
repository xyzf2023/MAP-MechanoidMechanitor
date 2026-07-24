using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Dialog_NameNewMechHiveFaction
        : Dialog_NamePlayerFaction
    {
        public Dialog_NameNewMechHiveFaction()
        {
            nameGenerator = () => NewMechHiveNameUtility.GenerateFactionName(IsValidName, curName);
            curName = nameGenerator();
        }
    }
}
