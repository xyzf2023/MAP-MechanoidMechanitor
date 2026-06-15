using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(RitualRoleColonist), nameof(RitualRoleColonist.AppliesToPawn))]
    public static class GravshipRitualRoleColonist_AppliesToPawn_Patch
    {
        public static bool Prefix(
            RitualRoleColonist __instance,
            Pawn p,
            ref string? reason,
            TargetInfo selectedTarget,
            LordJob_Ritual ritual,
            RitualRoleAssignments assignments,
            Precept_Ritual precept,
            bool skipReason,
            ref bool __result)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return true;
            }

            if (!CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(p))
            {
                return true;
            }

            if (selectedTarget.Thing == null
                || selectedTarget.Thing.TryGetComp<CompPilotConsole>() == null)
            {
                return true;
            }

            if (__instance.id != "pilot" && __instance.id != "copilot")
            {
                return true;
            }

            if (p.Dead || p.Downed || p.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                if (!skipReason)
                {
                    reason = "Incapable".Translate();
                }

                __result = false;
                return false;
            }

            if (p.Faction == null || !p.Faction.IsPlayerSafe())
            {
                if (!skipReason)
                {
                    reason = "MessageRitualRoleMustBeColonist".Translate(__instance.Label);
                }

                __result = false;
                return false;
            }

            if (p.GuestStatus == GuestStatus.Prisoner)
            {
                if (!skipReason)
                {
                    reason = "MessageRitualRoleMustBeFree".Translate(__instance.Label);
                }

                __result = false;
                return false;
            }

            if (p.skills == null || p.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled)
            {
                if (!skipReason)
                {
                    reason = "MessageRitualRoleMustBeCapableOfGeneric".Translate(
                        __instance.LabelCap,
                        SkillDefOf.Intellectual.label);
                }

                __result = false;
                return false;
            }

            reason = null;
            __result = true;
            return false;
        }
    }
}
