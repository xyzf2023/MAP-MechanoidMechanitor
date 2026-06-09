using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider), nameof(FloatMenuOptionProvider.SelectedPawnValid))]
    public static class VanillaRelayFloatMenuPatches
    {
        [HarmonyPostfix]
        public static void SelectedPawnValid_Postfix(
            FloatMenuOptionProvider __instance,
            Pawn pawn,
            FloatMenuContext context,
            ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (pawn == null)
            {
                return;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (__instance is not FloatMenuOptionProvider_Mechanitor)
            {
                return;
            }

            if (!VanillaRelayControlUtility.IsVanillaRelayController(pawn))
            {
                return;
            }

            try
            {
                Traverse provider = Traverse.Create(__instance);
                bool drafted = provider.Property("Drafted").GetValue<bool>();
                bool undrafted = provider.Property("Undrafted").GetValue<bool>();
                bool requiresManipulation = provider.Property("RequiresManipulation").GetValue<bool>();

                bool draftedOk = drafted || !pawn.Drafted;
                bool undraftedOk = undrafted || pawn.Drafted;
                bool manipulationOk = !requiresManipulation
                    || pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true;

                if (draftedOk && undraftedOk && manipulationOk)
                {
                    __result = true;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[MAP-MechanoidMechanitor] Failed to evaluate mechanitor float menu validity for {pawn.LabelShort}: {ex}");
            }
        }
    }
}
