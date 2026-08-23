using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5
{
    /// <summary>
    /// 只扩展闪耀世界毁灭者5“动手脚”指令的使用者资格。
    /// Lord、操作能力、目标状态、预约、路径和任务信号仍由原MOD处理。
    /// </summary>
    internal static class ModifySavingMechCompatibilityPatch
    {
        internal static void Postfix(Pawn? pawn, ref bool __result)
        {
            if (__result
                || pawn == null
                || !pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (CompColonistLikeFloatMenuUser
                    .PawnCanUseColonistLikeFloatMenu(pawn))
            {
                __result = true;
            }
        }
    }
}
