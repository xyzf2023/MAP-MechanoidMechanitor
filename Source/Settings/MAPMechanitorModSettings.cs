using Verse;

namespace MAP_MechanoidMechanitor
{
    public class MAPMechanitorModSettings : ModSettings
    {
        public bool addMechanoidMechanitorsToWorkTab = false;
        public bool enablePortraitDisplayForAllSaves = false;
        public bool enableMechanoidMechanitorBrainImplants = false;

        /// <summary>
        /// 默认关闭。开启后，恋人孕育的子嗣在受孕时额外继承配偶全部异种基因。
        /// </summary>
        public bool loverOffspringInheritXenogenes = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref addMechanoidMechanitorsToWorkTab,
                "addMechanoidMechanitorsToWorkTab",
                false);
            Scribe_Values.Look(
                ref enablePortraitDisplayForAllSaves,
                "enablePortraitDisplayForAllSaves",
                false);
            Scribe_Values.Look(
                ref enableMechanoidMechanitorBrainImplants,
                "enableMechanoidMechanitorBrainImplants",
                false);
            Scribe_Values.Look(
                ref loverOffspringInheritXenogenes,
                "loverOffspringInheritXenogenes",
                false);
        }
    }
}
