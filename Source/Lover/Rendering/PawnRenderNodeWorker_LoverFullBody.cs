using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人完整机械体节点的可见性控制：正常状态显示完整身体；真正躺床 Lovin 时隐藏。
    /// 图像本身由 PawnRenderNode_AnimalPart_Body 从 PawnKind.bodyGraphicData（Lover/Lover）获取，
    /// 不再声称这是 Humanlike 身体渲染。
    /// 继承原版 Body Worker，保留其 NoBody、posture、duty drawBodyOverride 等基本行为。
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
