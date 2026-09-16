using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MovementCostImmunity : CompProperties
    {
        public CompProperties_MovementCostImmunity()
        {
            compClass = typeof(CompMovementCostImmunity);
        }
    }

    /// <summary>
    /// 常驻地形、可通行物体与雪沙减速豁免，与机动作战提供同一能力。
    /// 仅作能力标记，无 Tick、注册表或存档状态；保留开门等待与不可通行限制。
    /// </summary>
    public sealed class CompMovementCostImmunity : ThingComp
    {
    }
}
