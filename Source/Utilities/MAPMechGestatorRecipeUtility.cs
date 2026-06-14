using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPMechGestatorRecipeUtility
    {
        private const string JusticePawnDefName = "MAP_Mech_Justice";
        private const string JusticeGestateDefName = "MAP_Gestate_Justice";
        private const string HermitGestateDefName = "MAP_Gestate_Hermit";

        public static bool IsPawnDisabledForGestationRecipe(RecipeDef? recipe, Pawn? pawn)
        {
            if (recipe == null || pawn == null)
            {
                return false;
            }

            if (pawn.def?.defName != JusticePawnDefName)
            {
                return false;
            }

            string defName = recipe.defName ?? string.Empty;
            return defName == JusticeGestateDefName || defName == HermitGestateDefName;
        }

        public static string GetDisabledReasonKey(RecipeDef? recipe)
        {
            string defName = recipe?.defName ?? string.Empty;
            if (defName == HermitGestateDefName)
            {
                return "MAP_MechanoidMechanitor.Bill.Reason.JusticeCannotGestateHermit";
            }

            return "MAP_MechanoidMechanitor.Bill.Reason.JusticeCannotGestateJustice";
        }
    }
}
