using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 集中解析机体能力来源。当前支持 Pawn ThingDef、PawnKindDef 与 HediffDef。
    /// </summary>
    public static class MechCapabilityUtility
    {
        public static bool HasCapability(Pawn? pawn, string? capabilityId)
        {
            if (pawn == null || pawn.Destroyed || string.IsNullOrEmpty(capabilityId))
            {
                return false;
            }

            if (DefProvidesCapability(pawn.def, capabilityId)
                || DefProvidesCapability(pawn.kindDef, capabilityId))
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
                Hediff hediff = hediffs[i];
                if (DefProvidesCapability(hediff?.def, capabilityId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DefProvidesCapability(Def? def, string capabilityId)
        {
            DefModExtension_MechCapabilityProvider? extension =
                def?.GetModExtension<DefModExtension_MechCapabilityProvider>();
            List<string>? capabilities = extension?.capabilities;
            if (capabilities == null)
            {
                return false;
            }

            for (int i = 0; i < capabilities.Count; i++)
            {
                if (string.Equals(
                        capabilities[i],
                        capabilityId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
