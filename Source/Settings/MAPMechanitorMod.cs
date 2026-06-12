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
                "将正义显示在工作标签页",
                ref Settings!.addJusticeToWorkTab,
                "启用后，正义会被追加显示到原版\"工作\"标签页中，方便调整工作优先级。若与修改工作标签页/工作优先级界面的 MOD 冲突，请关闭此项。");
            listing.End();
        }
    }
}
