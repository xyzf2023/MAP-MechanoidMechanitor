using MAP_MechanoidMechanitor.Scenarios;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class MAPMechanitorMod : Mod
    {
        public static MAPMechanitorModSettings? Settings;

        public MAPMechanitorMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MAPMechanitorModSettings>();
        }

        public override string SettingsCategory() => "[MAP]机械族机械师";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled(
                "将机械族机械师显示在工作标签页",
                ref Settings!.addMechanoidMechanitorsToWorkTab,
                "启用后，符合条件的机械族机械师会被追加显示到原版“工作”标签页中，方便调整工作优先级。若与修改工作标签页或工作优先级界面的 MOD 冲突，请关闭此项。");

            bool previousEnablePortraitDisplayForAllSaves =
                Settings.enablePortraitDisplayForAllSaves;
            listing.CheckboxLabeled(
                "MAP_Settings_EnablePortraitDisplayForAllSaves_Label".Translate(),
                ref Settings.enablePortraitDisplayForAllSaves,
                "MAP_Settings_EnablePortraitDisplayForAllSaves_Description".Translate());
            if (Settings.enablePortraitDisplayForAllSaves
                != previousEnablePortraitDisplayForAllSaves)
            {
                MechanoidMechanitorScenarioFreeColonistUtility.NotifyColonistDisplaysDirtyIfReady();
            }

            listing.CheckboxLabeled(
                "MAP_Settings_EnableMechanoidMechanitorBrainImplants_Label".Translate(),
                ref Settings.enableMechanoidMechanitorBrainImplants,
                "MAP_Settings_EnableMechanoidMechanitorBrainImplants_Description".Translate());
            if (MechanoidMechanitorBrainImplantFeatureState.RestartRequired)
            {
                listing.Label(
                    "MAP_Settings_EnableMechanoidMechanitorBrainImplants_RestartRequired"
                        .Translate());
            }

            listing.CheckboxLabeled(
                "MAP_Settings_SyntheticOffspringInheritXenogenes_Label".Translate(),
                ref Settings.syntheticOffspringInheritXenogenes,
                "MAP_Settings_SyntheticOffspringInheritXenogenes_Description".Translate());

            listing.End();
        }
    }
}
