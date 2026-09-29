using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimSkyBlock
{
    internal static class RimSkyBlockImperialTaskPatch
    {
        // __0、__1 按已校验的参数位置绑定，不依赖第三方参数名。
        public static void Postfix(Caravan __0, SkillDef __1, ref Pawn? __result)
        {
            if (__0 == null || __1 == null) return;

            int bestLevel = __result?.skills?.GetSkill(__1)?.Level ?? int.MinValue;
            foreach (Pawn pawn in __0.PawnsListForReading)
            {
                if (!RimSkyBlockCompatibilityUtility.IsPlayerMechanitor(pawn)
                    || pawn.Downed
                    || !MechanoidMechanitorSkillUtility.TryGetPreferredSkillLevel(pawn, __1, out int level))
                {
                    continue;
                }

                // 同等级保留上游结果。仅替换代表，经验、奖惩和结束信号仍由空岛执行一次。
                if (__result == null || level > bestLevel)
                {
                    __result = pawn;
                    bestLevel = level;
                }
            }
        }
    }
}
