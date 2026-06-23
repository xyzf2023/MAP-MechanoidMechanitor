using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(ITab_Pawn_Character),
        nameof(ITab_Pawn_Character.IsVisible),
        MethodType.Getter)]
    public static class Patch_ITab_Pawn_Character_IsVisible_CommanderFaction
    {
        private const string JusticeDefName = "MAP_Mech_Justice";
        private const string HermitDefName = "MAP_Mech_Hermit";

        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            Thing? selectedThing = Find.Selector.SingleSelectedThing;
            Pawn? pawn = selectedThing as Pawn;
            if (pawn == null && selectedThing is Corpse corpse)
            {
                pawn = corpse.InnerPawn;
            }

            if (pawn == null || pawn.Faction == Faction.OfPlayer)
            {
                return;
            }

            string? defName = pawn.def?.defName;
            if (defName == JusticeDefName || defName == HermitDefName)
            {
                __result = false;
            }
        }
    }
}
