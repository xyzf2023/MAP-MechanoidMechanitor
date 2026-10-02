using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>为穿衣机械体接入服装架原版菜单，不放开其他建筑的机械体门槛。</summary>
    public sealed class FloatMenuOptionProvider_HumanApparelOutfitStand : FloatMenuOptionProvider_FromThing
    {
        private static readonly FloatMenuOptionProvider_FromThing OriginalProvider =
            new FloatMenuOptionProvider_FromThing();

        protected override bool MechanoidCanDo => true;
        protected override bool Multiselect => false;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            // 类殖民者菜单框架可能已经放行 FromThing；此时交给原提供器，避免重复菜单。
            return HumanApparelUtility.CanUseOutfitStand(pawn)
                && !OriginalProvider.SelectedPawnValid(pawn, context);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing is not Building_OutfitStand)
                yield break;

            foreach (FloatMenuOption option in base.GetOptionsFor(clickedThing, context))
                yield return option;
        }
    }
}
