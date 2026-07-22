using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 社交面板不依赖文化适配；阻止机械族机械师走原版随机人际关系生成。
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawnRelations")]
    public static class MechanoidMechanitorIdeology_GeneratePawnRelations_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref PawnGenerationRequest request)
        {
            if (pawn == null)
            {
                return true;
            }

            if (GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _))
            {
                return false;
            }

            if (ColonistLikeSocialTabUtility.HasSocialTab(pawn))
            {
                return false;
            }

            return true;
        }
    }
}
