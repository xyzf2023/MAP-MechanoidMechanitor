using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人 Lovin 专用头部节点：正常情况下隐藏；真正躺床 Lovin 时显示。
    /// 继承原版 Head Worker，保留 HeadStump、BaseHeadOffsetAt、facing、crawling 等基本处理。
    /// </summary>
    public class PawnRenderNodeWorker_LoverLovinHead : PawnRenderNodeWorker_Head
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
            {
                return false;
            }

            return LoverRenderUtility.ShouldDrawLovinHeadOnly(parms);
        }
    }
}
