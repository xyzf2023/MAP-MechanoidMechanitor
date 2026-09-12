using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 在 Pawn 的 ThingDef、PawnKindDef 或 HediffDef 上声明其提供的机体固有转换类能力。
    /// 当前仅允许声明 MechanoidMechanitorCapability.BuildingConversion 与
    /// MechanoidMechanitorCapability.Fusion，其余能力位即使写入也会被 C# 白名单过滤，
    /// 仍由各自权威系统提供。多个来源按并集处理，具体 AbilityDef 由统一能力同步层授予或移除。
    /// </summary>
    public sealed class DefModExtension_MechCapabilityProvider : DefModExtension
    {
        public List<MechanoidMechanitorCapability>? capabilities;
    }
}
