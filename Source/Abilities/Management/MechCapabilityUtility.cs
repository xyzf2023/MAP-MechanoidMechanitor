using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 将 Pawn 的 ThingDef、PawnKindDef 与 HediffDef 上声明的
    /// DefModExtension_MechCapabilityProvider 解析为统一的能力位。
    /// 这里只是 MechanoidMechanitorCapabilityUtility 的一种数据来源，
    /// 不构成独立的字符串能力判断层，业务代码不得直接据此判定具体能力。
    /// </summary>
    internal static class MechCapabilityUtility
    {
        internal static MechanoidMechanitorCapability GetProvidedCapabilities(
            Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return MechanoidMechanitorCapability.None;
            }

            MechanoidMechanitorCapability capabilities =
                GetProvidedCapabilities(pawn.def)
                | GetProvidedCapabilities(pawn.kindDef);

            List<Hediff>? hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return capabilities;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                capabilities |= GetProvidedCapabilities(hediffs[i]?.def);
            }

            return capabilities;
        }

        internal static bool HasProvidedCapability(
            Pawn? pawn,
            MechanoidMechanitorCapability capability)
        {
            if (pawn == null
                || pawn.Destroyed
                || capability == MechanoidMechanitorCapability.None)
            {
                return false;
            }

            if (DefProvidesCapability(pawn.def, capability)
                || DefProvidesCapability(pawn.kindDef, capability))
            {
                return true;
            }

            List<Hediff>? hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return false;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                if (DefProvidesCapability(hediffs[i]?.def, capability))
                {
                    return true;
                }
            }

            return false;
        }

        private static MechanoidMechanitorCapability GetProvidedCapabilities(Def? def)
        {
            DefModExtension_MechCapabilityProvider? extension =
                def?.GetModExtension<DefModExtension_MechCapabilityProvider>();
            List<MechanoidMechanitorCapability>? capabilities =
                extension?.capabilities;
            if (capabilities == null)
            {
                return MechanoidMechanitorCapability.None;
            }

            MechanoidMechanitorCapability result = MechanoidMechanitorCapability.None;
            for (int i = 0; i < capabilities.Count; i++)
            {
                result |= capabilities[i];
            }

            return result;
        }

        private static bool DefProvidesCapability(
            Def? def,
            MechanoidMechanitorCapability capability)
        {
            return (GetProvidedCapabilities(def) & capability) == capability;
        }
    }
}
