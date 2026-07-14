using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Bill_Mech), nameof(Bill_Mech.BillTick))]
    public static class MassProductionMechGestatorTimingPatches
    {
        [ThreadStatic]
        private static Stack<Bill_Mech>? vanillaBillTickAllowanceStack;

        internal static void RunWithVanillaBillTickAllowed(Bill_Mech bill, Action action)
        {
            if (bill == null)
            {
                throw new ArgumentNullException(nameof(bill));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            Stack<Bill_Mech> stack = vanillaBillTickAllowanceStack ??= new Stack<Bill_Mech>();
            stack.Push(bill);
            try
            {
                action();
            }
            finally
            {
                if (stack.Count == 0)
                {
                    Log.Error(
                        "[MAP-MechanoidMechanitor] vanilla BillTick allowance stack was empty when unwinding for "
                        + bill.ToStringSafe()
                        + ".");
                }
                else
                {
                    Bill_Mech popped = stack.Pop();
                    if (!ReferenceEquals(popped, bill))
                    {
                        Log.Error(
                            "[MAP-MechanoidMechanitor] vanilla BillTick allowance stack mismatch. Expected "
                            + bill.ToStringSafe()
                            + ", popped "
                            + popped.ToStringSafe()
                            + ".");
                    }
                }
            }
        }

        internal static bool IsVanillaBillTickAllowed(Bill_Mech bill)
        {
            Stack<Bill_Mech>? stack = vanillaBillTickAllowanceStack;
            return stack != null
                && stack.Count > 0
                && ReferenceEquals(stack.Peek(), bill);
        }

        [HarmonyPrefix]
        public static bool Prefix(Bill_Mech __instance)
        {
            if (IsVanillaBillTickAllowed(__instance))
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

    [HarmonyPatch(typeof(Bill_Autonomous), nameof(Bill_Autonomous.PawnAllowedToStartAnew))]
    public static class Patch_MassProductionMechGestator_PawnAllowedToStartAnew
    {
        [HarmonyPostfix]
        public static void Postfix(Bill_Autonomous __instance, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            if (__instance.billStack?.billGiver is not Building_MassProductionMechGestator gestator)
            {
                return;
            }

            CompMassProductionMechGestator? comp = gestator.MassProductionGestationComp;
            if (comp != null && (comp.ReleasePending || comp.SettlementCommitted))
            {
                __result = false;
            }
        }
    }
}
