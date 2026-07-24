using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 隐藏原版 <see cref="FloatMenuOptionProvider_Mechanitor"/> 在“机械族机械师右键自己”时产生的
    /// 无意义或无法执行的选项（如对自己控制/维修），改用本 MOD 的手动自我修复菜单。
    /// 仅在该特定情形跳过原版提供器，其余原版机械师菜单行为完全保留。
    /// </summary>
    [HarmonyPatch(
        typeof(FloatMenuOptionProvider_Mechanitor),
        nameof(FloatMenuOptionProvider_Mechanitor.GetOptionsFor),
        new System.Type[] { typeof(Pawn), typeof(FloatMenuContext) })]
    public static class MechanoidMechanitorSelfRepairFloatMenuPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(
            Pawn clickedPawn,
            FloatMenuContext context,
            ref IEnumerable<FloatMenuOption> __result)
        {
            if (context == null || context.FirstSelectedPawn == null)
            {
                return true;
            }

            Pawn selectedPawn = context.FirstSelectedPawn;
            if (selectedPawn != clickedPawn)
            {
                return true;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(selectedPawn))
            {
                return true;
            }

            __result = Enumerable.Empty<FloatMenuOption>();
            return false;
        }
    }
}
