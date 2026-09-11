using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 在 Pawn 的 ThingDef、PawnKindDef 或 HediffDef 上声明其提供的机体能力来源。
    /// 多个来源按并集处理，具体 AbilityDef 仍由统一能力同步层授予或移除。
    /// </summary>
    public sealed class DefModExtension_MechCapabilityProvider : DefModExtension
    {
        public List<string>? capabilities;
    }
}
