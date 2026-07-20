using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(CallBossgroupUtility), nameof(CallBossgroupUtility.BossgroupEverCallable))]
    public static class MechanoidMechanitorPurgeDirective_BossgroupCallablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BossgroupDef def, ref AcceptanceReport __result)
        {
            if (!__result
                || !MechanoidMechanitorBossgroupUtility.ShouldBlockPlayerSummon(def))
            {
                return;
            }

            __result = MechanoidMechanitorBossgroupUtility.DisabledReason;
        }
    }

    [HarmonyPatch(typeof(Command_CallBossgroup), "IsDisabled")]
    public static class MechanoidMechanitorPurgeDirective_BossgroupCommandPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, ref string reason)
        {
            if (!__result
                || !reason.NullOrEmpty()
                || !MechanoidMechanitorBossgroupUtility.HasAnyBlockedMechHiveBossgroup())
            {
                return;
            }

            reason = MechanoidMechanitorBossgroupUtility.DisabledReason;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_CallBossgroup), nameof(CompUseEffect_CallBossgroup.CanBeUsedBy))]
    public static class MechanoidMechanitorPurgeDirective_BossgroupUseCheckPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            CompUseEffect_CallBossgroup __instance,
            ref AcceptanceReport __result)
        {
            if (!__result
                || !MechanoidMechanitorBossgroupUtility.ShouldBlockPlayerSummon(
                    __instance.Props.bossgroupDef))
            {
                return;
            }

            __result = MechanoidMechanitorBossgroupUtility.DisabledReason;
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_CallBossgroup), nameof(CompUseEffect_CallBossgroup.DoEffect))]
    public static class MechanoidMechanitorPurgeDirective_BossgroupEffectPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CompUseEffect_CallBossgroup __instance)
        {
            if (!MechanoidMechanitorBossgroupUtility.ShouldBlockPlayerSummon(
                    __instance.Props.bossgroupDef))
            {
                return true;
            }

            Messages.Message(
                MechanoidMechanitorBossgroupUtility.DisabledReason,
                MessageTypeDefOf.RejectInput,
                historical: false);
            return false;
        }
    }
}
