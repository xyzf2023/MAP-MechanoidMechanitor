using Verse;

namespace MAP_MechanoidMechanitor
{
    public class MAPMechanitorModSettings : ModSettings
    {
        public bool addJusticeToWorkTab = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref addJusticeToWorkTab, "addJusticeToWorkTab", false);
        }
    }
}
