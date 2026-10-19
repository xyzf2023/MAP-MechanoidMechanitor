using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // Explicit installation keeps this backend separate from PatchAll and optional providers.
    internal static class MovementCostImmunityPatches
    {
        internal static void Initialize(Harmony harmony)
        {
            MovementCostImmunityCore.Install(harmony,
                MovementCostImmunityUtility.HasImmunity, "[MAP-机械族机械师] ");
            LongEventHandler.ExecuteWhenFinished(() => MovementCostImmunityCore.Verify());
        }
    }
}
