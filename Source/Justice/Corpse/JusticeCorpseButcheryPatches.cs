using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
    internal static class JusticeCorpseButcheryPatches
    {
        private const string AutonomousDirectiveCoreDefName =
            "MAP_AutonomousDirectiveCore";

        [HarmonyPostfix]
        private static IEnumerable<Thing> Postfix(
            IEnumerable<Thing> values,
            Corpse __instance)
        {
            foreach (Thing value in values)
            {
                yield return value;
            }

            Pawn? pawn = __instance.InnerPawn;

            if (pawn == null ||
                !JusticePawnUtility.IsPlayerJustice(pawn) ||
                pawn.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            ThingDef coreDef =
                DefDatabase<ThingDef>.GetNamed(AutonomousDirectiveCoreDefName);

            yield return ThingMaker.MakeThing(coreDef);
        }
    }
}
