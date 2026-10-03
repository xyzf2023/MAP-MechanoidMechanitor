using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.WorkTab
{
    internal static class WorkTabPawnListPatch
    {
        internal static bool Enabled { get; set; }

        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            if (Enabled && __result != null)
                __result = WorkTabPawnListUtility.AppendEligiblePawns(__result);
        }
    }
}
