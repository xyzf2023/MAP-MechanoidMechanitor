using Verse;

namespace MAP_MechanoidMechanitor
{
    public class MAPMechanitorModSettings : ModSettings
    {
        public bool addJusticeToWorkTab = false;
        public bool enablePortraitDisplayForAllSaves = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref addJusticeToWorkTab, "addJusticeToWorkTab", false);
            Scribe_Values.Look(
                ref enablePortraitDisplayForAllSaves,
                "enablePortraitDisplayForAllSaves",
                false);
        }
    }
}
