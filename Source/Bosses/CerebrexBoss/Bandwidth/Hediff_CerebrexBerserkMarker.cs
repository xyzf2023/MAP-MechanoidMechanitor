using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 隐藏标记：仅用于区分“由主脑带宽干扰技能造成的狂暴”与其他来源的狂暴。
    /// 不提供任何属性效果，且对玩家不可见。
    /// </summary>
    public sealed class Hediff_CerebrexBerserkMarker : HediffWithComps
    {
        public override bool Visible => false;
    }
}
