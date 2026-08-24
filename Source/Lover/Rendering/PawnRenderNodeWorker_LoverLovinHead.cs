using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人 Lovin 专用头部贴图节点：仅在真正躺床执行 Lovin 时显示。
    /// 图像直接由 RenderTree 的 Lover/Loverhead Graphic_Multi 提供，不依赖 Humanlike HeadType。
    /// 普通 PawnRenderNode 不再自动在缺失头部时返回 null，因此必须显式检查 HasHead。
    /// </summary>
    public class PawnRenderNodeWorker_LoverLovinHead : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
            {
                return false;
            }

            if (!LoverRenderUtility.ShouldDrawLovinHeadOnly(parms))
            {
                return false;
            }

            return parms.pawn?.health?.hediffSet?.HasHead == true;
        }
    }
}
