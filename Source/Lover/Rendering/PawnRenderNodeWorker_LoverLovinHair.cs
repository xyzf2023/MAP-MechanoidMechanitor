using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人 Lovin 专用头发节点：正常情况下隐藏；真正躺床 Lovin 时显示（作为 Head 子节点）。
    /// 继承原版 FlipWhenCrawling Worker，保留其爬行翻转等行为。
    /// </summary>
    public class PawnRenderNodeWorker_LoverLovinHair : PawnRenderNodeWorker_FlipWhenCrawling
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
