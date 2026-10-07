using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MechServiceEnergyContext
    {
        private static readonly AccessTools.FieldRef<Need, Pawn> PawnField =
            AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        internal static bool IsServicing(Need_MechEnergy energy) =>
            PawnField(energy)?.jobs?.curDriver is JobDriver_UseMechServiceStation driver
            && driver.IsPoweredService;

        internal static bool IsCharging(Need_MechEnergy energy) =>
            PawnField(energy)?.jobs?.curDriver is JobDriver_UseMechServiceStation driver
            && driver.IsPoweredService && driver.Station!.NeedsCharge(PawnField(energy));

        // 原版检查面板与第三方能量提示共用同一速率；只读当前服务状态，不补充能量。
        internal static bool TryGetServiceEnergyRate(Pawn? pawn, out string rate)
        {
            rate = string.Empty;
            if (pawn?.needs?.energy is not Need_MechEnergy energy || energy.MaxLevel <= 0f
                || pawn.jobs?.curDriver is not JobDriver_UseMechServiceStation driver
                || !driver.IsPoweredService || driver.Station is not CompMechServiceStation station)
                return false;

            int percent = Mathf.Max(0, Mathf.RoundToInt(station.Props.energyPerTick * 60000f / energy.MaxLevel * 100f));
            rate = station.NeedsCharge(pawn) && percent > 0
                ? "+" + "PerDay".Translate(percent + "%")
                : "-" + "PerDay".Translate("0%");
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    internal static class MechServiceEnergyInspectPatch
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (__instance.needs?.energy is not Need_MechEnergy energy
                || string.IsNullOrEmpty(__result)
                || !MechServiceEnergyContext.TryGetServiceEnergyRate(__instance, out string rate)) return;

            // 只替换原版能量行的显示；实际充电仍只由 ServiceTick 写入。
            string prefix = "MechEnergy".Translate() + ": " + energy.CurLevelPercentage.ToStringPercent();
            string original = prefix + " (-" + "PerDay".Translate((energy.FallPerDay / energy.MaxLevel).ToStringPercent()) + ")";
            __result = __result.Replace(original, prefix + " (" + rate + ")");
        }
    }

    [HarmonyPatch(typeof(Need_MechEnergy), nameof(Need_MechEnergy.FallPerDay), MethodType.Getter)]
    internal static class MechServiceEnergyFallPatch
    {
        public static void Postfix(Need_MechEnergy __instance, ref float __result)
        {
            if (MechServiceEnergyContext.IsServicing(__instance)) __result = 0f;
        }
    }

    [HarmonyPatch(typeof(Need_MechEnergy), nameof(Need_MechEnergy.GUIChangeArrow), MethodType.Getter)]
    internal static class MechServiceEnergyArrowPatch
    {
        public static void Postfix(Need_MechEnergy __instance, ref int __result)
        {
            if (MechServiceEnergyContext.IsServicing(__instance))
                __result = MechServiceEnergyContext.IsCharging(__instance) ? 1 : 0;
        }
    }

    [HarmonyPatch(typeof(Need_MechEnergy), nameof(Need_MechEnergy.NeedInterval))]
    internal static class MechServiceEnergyShutdownPatch
    {
        private static readonly AccessTools.FieldRef<Need_MechEnergy, bool> SelfShutdownField =
            AccessTools.FieldRefAccess<Need_MechEnergy, bool>("selfShutdown");

        public static void Prefix(Need_MechEnergy __instance)
        {
            // 原版只识别 MechCharge；已由整备台补入能量时解除低电标记，
            // 仍让原版 NeedInterval 负责移除休眠 Hediff 和其他正常更新。
            if (__instance.CurLevel > 0f && MechServiceEnergyContext.IsServicing(__instance))
                SelfShutdownField(__instance) = false;
        }
    }
}
