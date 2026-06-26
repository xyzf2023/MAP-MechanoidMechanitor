using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class HumanApparelUtility
    {
        public static bool TryGetApparelComp(Pawn? pawn, out CompHumanApparelUser? comp)
        {
            comp = pawn?.GetComp<CompHumanApparelUser>();
            return comp != null;
        }

        public static bool CanUseWearFloatMenu(Pawn? pawn)
        {
            return pawn != null
                && TryGetApparelComp(pawn, out CompHumanApparelUser? comp)
                && comp!.AllowWearFloatMenu;
        }

        public static bool CanRemoveApparel(Pawn? pawn)
        {
            return pawn != null
                && TryGetApparelComp(pawn, out CompHumanApparelUser? comp)
                && comp!.AllowRemoveApparel
                && pawn.apparel != null;
        }

        public static bool CanRenderHumanApparel(Pawn? pawn)
        {
            return pawn != null
                && pawn.apparel != null
                && TryGetApparelComp(pawn, out CompHumanApparelUser? comp)
                && comp!.EnableHumanApparelRendering;
        }

        public static BodyTypeDef ResolveApparelBodyType(Pawn pawn, BodyTypeDef fallback)
        {
            if (!TryGetApparelComp(pawn, out CompHumanApparelUser? comp)
                || comp!.ApparelBodyType == null
                || !comp.EnableHumanApparelRendering)
            {
                return fallback;
            }

            return comp.ApparelBodyType;
        }
    }
}
