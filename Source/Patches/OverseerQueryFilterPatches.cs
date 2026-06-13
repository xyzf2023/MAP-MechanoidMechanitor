using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 查询层修复：无外部监管者的 MAP mechanitor 节点（如正义）。
    //
    // 此处不要删除或阻止原版 PawnRelationDefOf.Overseer direct relation。
    // JobDriver_ControlMech 接管机械体时会建立 reflexive Overseer 关系。
    // 正义控制隐者后，双方 directRelations 中都会保留该底层关系。
    // 对人类 mechanitor 无影响；但正义本身也是 mechanoid 节点，原版 GetOverseer()
    // 会从反向 relation 读到隐者，误将隐者当作正义的监管者。
    //
    // 本补丁只修正查询语义：HasNode && !RequiresExternalOverseer 时，
    // GetOverseer() 必须返回 null，使正义监管者列为空，而隐者.GetOverseer() 仍为正义。
    // 控制组与带宽逻辑不变。
    //
    // 不要用 RemoveDirectRelation 替代；不要阻止 ControlMech；不要让正义加入自己的控制组。
    // 隐者、普通机械体、由正义监管的机械体必须继续走原版 GetOverseer()
    // （RequiresExternalOverseer=true 时不做过滤）。
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetOverseer))]
    public static class Patch_MechanitorUtility_GetOverseer_MAPNodeFilter
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Pawn? __result)
        {
            if (__result == null || pawn == null)
            {
                return;
            }

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.HasNode(pawn))
            {
                return;
            }

            if (MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn))
            {
                return;
            }

            __result = null;
        }
    }
}
