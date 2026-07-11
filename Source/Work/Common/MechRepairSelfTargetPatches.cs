using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(WorkGiver_RepairMech), nameof(WorkGiver_RepairMech.HasJobOnThing))]
    public static class MechRepairSelfTargetPatches_HasJobOnThing
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Thing t, ref bool __result)
        {
            if (pawn != null && t != null && pawn == t)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(JobDriver_RepairMech), nameof(JobDriver_RepairMech.TryMakePreToilReservations))]
    public static class MechRepairSelfTargetPatches_TryMakePreToilReservations
    {
        [HarmonyPrefix]
        public static bool Prefix(JobDriver_RepairMech __instance, ref bool __result)
        {
            Pawn? pawn = __instance.pawn;
            Thing? target = __instance.job?.targetA.Thing;
            if (pawn != null && target != null && pawn == target)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
