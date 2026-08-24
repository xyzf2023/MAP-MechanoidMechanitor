using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人渲染辅助：判断当前是否处于“真正躺床 Lovin”的 Lovin 专用头部贴图独占模式。
    /// 核心原则：正在执行 Lovin 且当前真正已经在床上躺卧，才切换为仅显示 Lovin 专用头部贴图。
    /// </summary>
    internal static class LoverRenderUtility
    {
        /// <summary>
        /// 是否应仅绘制 Lovin 专用头部贴图（隐藏整身贴图）。
        /// 仅当：非空 Pawn、非肖像、正在执行 Lovin、已躺在床上、当前 posture 处于躺卧状态。
        /// </summary>
        internal static bool ShouldDrawLovinHeadOnly(PawnDrawParms parms)
        {
            if (parms.pawn == null)
            {
                return false;
            }

            // 肖像中始终使用正常完整身体，不切成 Lovin 头部模式。
            if (parms.Portrait)
            {
                return false;
            }

            // 只有真正在执行 Lovin 才考虑切换；普通睡觉、走向床铺等都不切换。
            if (parms.pawn.CurJobDef != JobDefOf.Lovin)
            {
                return false;
            }

            // 必须已经处于床上躺卧状态，避免“走在路上飘着一个头”。
            if (parms.bed == null)
            {
                return false;
            }

            if (!parms.posture.InBed())
            {
                return false;
            }

            return true;
        }
    }
}
