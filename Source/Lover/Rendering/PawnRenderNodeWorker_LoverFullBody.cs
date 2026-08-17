using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人整身贴图节点：正常情况下显示；真正躺床 Lovin 时隐藏（改由 Lovin Head/Hair 节点显示）。
    /// 继承原版 Body Worker，保留其 NoBody、posture、bed_showSleeperBody 等基本行为。
    /// </summary>
    public class PawnRenderNodeWorker_LoverFullBody : PawnRenderNodeWorker_Body
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
            {
                return false;
            }

            return !LoverRenderUtility.ShouldDrawLovinHeadOnly(parms);
        }
    }
}
