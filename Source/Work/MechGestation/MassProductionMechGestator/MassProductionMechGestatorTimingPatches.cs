using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Bill_Mech), nameof(Bill_Mech.BillTick))]
    public static class MassProductionMechGestatorTimingPatches
    {
        public static bool AllowVanillaBillTickOnce;

        [HarmonyPrefix]
        public static bool Prefix(Bill_Mech __instance)
        {
            if (AllowVanillaBillTickOnce)
            {
                return true;
            }

            if (__instance is Bill_ProductionMech
                && __instance.billStack?.billGiver is Building_MassProductionMechGestator)
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Building_WorkTableAutonomous), "get_CurrentBillFormingPercent")]
    public static class Patch_MassProductionMechGestator_CurrentBillFormingPercent
    {
        [HarmonyPrefix]
        public static bool Prefix(Building_WorkTableAutonomous __instance, ref float __result)
        {
            if (__instance is not Building_MassProductionMechGestator gestator)
            {
                return true;
            }

            if (gestator.ActiveMechBill is not Bill_ProductionMech { State: FormingState.Forming })
            {
                return true;
            }

            CompMassProductionMechGestator? comp = gestator.MassProductionGestationComp;
            if (comp == null || !comp.TimerInitialized)
            {
                return true;
            }

            __result = comp.FormingPercent;
            return false;
        }
    }

    [HarmonyPatch(typeof(Bill_Mech), nameof(Bill_Mech.AppendInspectionData))]
    public static class Patch_MassProductionMechGestator_AppendInspectionData
    {
        [HarmonyPrefix]
        public static bool Prefix(Bill_Mech __instance, StringBuilder sb)
        {
            if (__instance is not Bill_ProductionMech
                || __instance.billStack?.billGiver is not Building_MassProductionMechGestator gestator)
            {
                return true;
            }

            FormingState state = __instance.State;
            if (state != FormingState.Forming && state != FormingState.Preparing)
            {
                return true;
            }

            CompMassProductionMechGestator? comp = gestator.MassProductionGestationComp;
            int displayTicks = comp != null && comp.TimerInitialized
                ? comp.RemainingTicks
                : Building_MassProductionMechGestator.FixedFormingTicks;

            sb.AppendLine(
                "CurrentGestationCycle".Translate()
                + ": "
                + displayTicks.ToStringTicksToPeriod());
            sb.AppendLine(
                string.Concat(
                    string.Concat(
                        "RemainingGestationCycles".Translate() + ": ",
                        "1",
                        " (")
                    + "OfLower".Translate()
                    + " ",
                    "1",
                    ")"));
            return false;
        }
    }
}
