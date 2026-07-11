using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider), nameof(FloatMenuOptionProvider.SelectedPawnValid))]
    public static class HumanWeaponUserFloatMenuPatches
    {
        [HarmonyPostfix]
        public static void Postfix(
            FloatMenuOptionProvider __instance,
            Pawn pawn,
            FloatMenuContext context,
            ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (!CompHumanWeaponUser.PawnCanUseHumanWeapons(pawn))
            {
                return;
            }

            bool allowed = false;

            if (__instance is FloatMenuOptionProvider_Equip
                && CompHumanWeaponUser.PawnAllowsEquipFloatMenu(pawn))
            {
                allowed = true;
            }
            else if (__instance is FloatMenuOptionProvider_DropEquipment
                && CompHumanWeaponUser.PawnAllowsDropEquipmentFloatMenu(pawn))
            {
                allowed = true;
            }

            if (!allowed)
            {
                return;
            }

            __result = true;
        }
    }
}
