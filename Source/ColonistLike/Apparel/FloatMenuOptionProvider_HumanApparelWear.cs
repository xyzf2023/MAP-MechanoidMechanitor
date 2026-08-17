using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 通用人类服装穿着右键菜单：任何携带 CompHumanApparelUser 且允许 Wear 的机械 Pawn 都能使用。
    /// 不依赖具体种族 defName。
    /// </summary>
    public sealed class FloatMenuOptionProvider_HumanApparelWear : FloatMenuOptionProvider_Wear
    {
        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;

            return pawn != null
                && HumanApparelUtility.CanUseWearFloatMenu(pawn)
                && pawn.apparel != null;
        }
    }
}
