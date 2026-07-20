using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(Command_CallBossgroup), "get_FloatMenuOptions")]
    public static class MechanoidMechanitor_CallBossgroupFloatMenuOptionsPatch
    {
        private static readonly AccessTools.FieldRef<Command_CallBossgroup, Pawn_MechanitorTracker>
            MechanitorRef =
                AccessTools.FieldRefAccess<Command_CallBossgroup, Pawn_MechanitorTracker>(
                    "mechanitor");

        [HarmonyPrefix]
        public static bool Prefix(
            Command_CallBossgroup __instance,
            ref IEnumerable<FloatMenuOption> __result)
        {
            __result = BuildFloatMenuOptions(MechanitorRef(__instance));
            return false;
        }

        private static IEnumerable<FloatMenuOption> BuildFloatMenuOptions(
            Pawn_MechanitorTracker mechanitor)
        {
            foreach (BossgroupDef bg in DefDatabase<BossgroupDef>.AllDefs)
            {
                if (MechanoidMechanitorBossgroupUtility.ShouldHideFromVanillaBossgroupCommand(bg))
                {
                    continue;
                }

                AcceptanceReport acceptanceReport =
                    CallBossgroupUtility.BossgroupEverCallable(mechanitor.Pawn, bg);
                if (!acceptanceReport)
                {
                    yield return new FloatMenuOption(
                        "CannotSummon".Translate(bg.boss.kindDef.label)
                        + ": "
                        + acceptanceReport.Reason,
                        null);
                    continue;
                }

                BossgroupDef localBg = bg;
                yield return new FloatMenuOption(
                    "Summon".Translate(localBg.boss.kindDef.label),
                    delegate
                    {
                        CallBossgroupUtility.TryStartSummonBossgroupJob(
                            localBg,
                            mechanitor.Pawn);
                    });
            }
        }
    }

    [HarmonyPatch(typeof(Command_CallBossgroup), "get_DescPostfix")]
    public static class MechanoidMechanitor_CallBossgroupDescPostfixPatch
    {
        private static readonly AccessTools.FieldRef<Command_CallBossgroup, Pawn_MechanitorTracker>
            MechanitorRef =
                AccessTools.FieldRefAccess<Command_CallBossgroup, Pawn_MechanitorTracker>(
                    "mechanitor");

        [HarmonyPrefix]
        public static bool Prefix(Command_CallBossgroup __instance, ref string __result)
        {
            Pawn_MechanitorTracker mechanitor = MechanitorRef(__instance);
            StringBuilder text = new StringBuilder();
            Dictionary<BossgroupDef, AcceptanceReport> source = DefDatabase<BossgroupDef>.AllDefs
                .Where(b => !MechanoidMechanitorBossgroupUtility
                    .ShouldHideFromVanillaBossgroupCommand(b))
                .ToDictionary(
                    b => b,
                    b => CallBossgroupUtility.BossgroupEverCallable(mechanitor.Pawn, b));

            foreach (KeyValuePair<BossgroupDef, AcceptanceReport> item in source.Where(
                         b => b.Value))
            {
                text.Append(
                    "\n\n"
                    + "ReadyToSummonThreat".Translate(item.Key.boss.kindDef.label)
                        .Colorize(ColorLibrary.Green)
                        .CapitalizeFirst());
            }

            foreach (KeyValuePair<BossgroupDef, AcceptanceReport> item2 in source.Where(
                         b => !b.Value))
            {
                text.Append(
                    "\n\n"
                    + ("CannotSummon".Translate(item2.Key.boss.kindDef.label)
                        + ": "
                        + item2.Value.Reason).Colorize(ColorLibrary.RedReadable));
            }

            __result = text.ToString();
            return false;
        }
    }

    [HarmonyPatch(typeof(CallBossgroupUtility), nameof(CallBossgroupUtility.BossgroupEverCallable))]
    public static class MechanoidMechanitor_CallBossgroupHideEverCallablePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(BossgroupDef def, ref AcceptanceReport __result)
        {
            if (!MechanoidMechanitorBossgroupUtility.ShouldHideFromVanillaBossgroupCommand(def))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}