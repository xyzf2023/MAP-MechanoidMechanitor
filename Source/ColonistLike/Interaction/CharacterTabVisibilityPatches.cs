using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ITab_Pawn_Character), nameof(ITab_Pawn_Character.IsVisible), MethodType.Getter)]
    [HarmonyAfter("Fortified")]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_ITab_Pawn_Character_IsVisible_CommanderFaction
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            Pawn? pawn = ColonistLikeInspectTabUtility.ResolvePawn(
                Find.Selector?.SingleSelectedThing);
            if (pawn == null)
            {
                return;
            }

            // Fortified 会对全部 HumanlikeMech 强制隐藏角色页。
            // 这里只为已经获得 MAP 授权且角色数据完整的玩家 Pawn 最终放行。
            if (ColonistLikeInspectTabUtility.ShouldShowCharacterTab(pawn))
            {
                __result = true;
                return;
            }

            if (!__result || pawn.Faction == Faction.OfPlayer)
            {
                return;
            }

            if (MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.CharacterTab)
                || MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.IndividualSkills))
            {
                __result = false;
            }
        }
    }
}
