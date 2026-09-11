using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 为具体机械体声明建筑形态。可挂在 ThingDef、PawnKindDef 或提供能力的 HediffDef 上。
    /// “是否拥有建筑转换能力”仍由 MechCapabilityProvider 独立决定。
    /// </summary>
    public sealed class DefModExtension_MechBuildingConversion : DefModExtension
    {
        public ThingDef? buildingFormDef;
        public ThingDef? buildingStuff;
        public int placementSearchRadius = 8;
    }
}
