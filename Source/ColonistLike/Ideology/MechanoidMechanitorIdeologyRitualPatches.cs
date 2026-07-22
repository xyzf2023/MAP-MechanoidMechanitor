using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Full 模式下文化仪式参与资格与质量统计适配。
    /// </summary>
    public static class MechanoidMechanitorIdeologyRitualPatches
    {
        private static readonly AccessTools.FieldRef<RitualRoleAssignments, TargetInfo>
            RitualTargetField =
                AccessTools.FieldRefAccess<RitualRoleAssignments, TargetInfo>("ritualTarget");

        [HarmonyPatch(typeof(RitualRoleColonist), nameof(RitualRoleColonist.AppliesToPawn))]
        public static class Patch_RitualRoleColonist_AppliesToPawn
        {
            [HarmonyPostfix]
            public static void Postfix(
                RitualRoleColonist __instance,
                Pawn p,
                ref bool __result,
                ref string reason,
                bool skipReason)
            {
                if (__result
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(p))
                {
                    return;
                }

                if (!p.Faction.IsPlayerSafe() || p.GuestStatus == GuestStatus.Prisoner)
                {
                    return;
                }

                if (__instance.requiredWorkType != null
                    && p.WorkTypeIsDisabled(__instance.requiredWorkType))
                {
                    return;
                }

                if (__instance.usedSkill != null
                    && (p.skills == null
                        || p.skills.GetSkill(__instance.usedSkill).TotallyDisabled))
                {
                    return;
                }

                __result = true;
                reason = null!;
            }
        }

        [HarmonyPatch(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.CanEverSpectate))]
        public static class Patch_CanEverSpectate
        {
            [HarmonyPostfix]
            public static void Postfix(
                RitualRoleAssignments __instance,
                Pawn pawn,
                ref bool __result)
            {
                if (__result
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(pawn)
                    || pawn.IsPrisoner)
                {
                    return;
                }

                Precept_Ritual? ritual = __instance.Ritual;
                TargetInfo ritualTarget = RitualTargetField(__instance);
                if (ritual != null
                    && ritual.ritualOnlyForIdeoMembers
                    && pawn.Ideo != ritual.ideo
                    && !ritual.def.allowSpectatorsFromOtherIdeos)
                {
                    return;
                }

                if (ritual?.behavior?.def.spectatorFilter != null
                    && !ritual.behavior.def.spectatorFilter.Allowed(pawn))
                {
                    return;
                }

                if (ritual != null
                    && !ritual.behavior.PawnCanFillRole(pawn, null, out _, ritualTarget))
                {
                    return;
                }

                __result = GatheringsUtility.ShouldPawnKeepAttendingRitual(
                    pawn,
                    ritual,
                    ritual?.behavior.def.spectatorsIgnoreBleeding ?? false);
            }
        }

        [HarmonyPatch]
        public static class Patch_PawnNotAssignableReason_HumanlikeSpectate
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase? TargetMethod()
            {
                return cachedTarget ??= AccessTools.Method(
                    typeof(RitualRoleAssignments),
                    nameof(RitualRoleAssignments.PawnNotAssignableReason),
                    new[]
                    {
                        typeof(Pawn),
                        typeof(RitualRole),
                        typeof(Precept_Ritual),
                        typeof(RitualRoleAssignments),
                        typeof(TargetInfo),
                        typeof(bool).MakeByRefType()
                    });
            }

            [HarmonyPostfix]
            public static void Postfix(
                Pawn p,
                RitualRole role,
                ref string __result)
            {
                if (__result == null
                    || role != null
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(p))
                {
                    return;
                }

                string humanlikeReason =
                    "MessageRitualRoleMustBeHumanlike".Translate("Spectators".Translate());
                if (__result == humanlikeReason)
                {
                    __result = null!;
                }
            }
        }

        [HarmonyPatch(typeof(RitualOutcomeComp_ParticipantCount))]
        public static class Patch_ParticipantCount_Counts
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase? TargetMethod()
            {
                return cachedTarget ??= AccessTools.Method(
                    typeof(RitualOutcomeComp_ParticipantCount),
                    "Counts");
            }

            [HarmonyPostfix]
            public static void Postfix(
                RitualRoleAssignments assignments,
                Pawn p,
                ref bool __result)
            {
                if (__result
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(p))
                {
                    return;
                }

                if (assignments != null
                    && assignments.Ritual == null
                    && assignments.Required(p))
                {
                    return;
                }

                RitualRole? ritualRole = assignments?.RoleForPawn(p);
                if (ritualRole != null && !ritualRole.countsAsParticipant)
                {
                    return;
                }

                __result = true;
            }
        }

        [HarmonyPatch(
            typeof(RitualOutcomeComp_NumParticipantsWithTag),
            nameof(RitualOutcomeComp_NumParticipantsWithTag.GetQualityFactor))]
        public static class Patch_NumParticipantsWithTag_QualityFactor
        {
            [HarmonyPostfix]
            public static void Postfix(
                RitualOutcomeComp_NumParticipantsWithTag __instance,
                RitualRoleAssignments assignments,
                ref QualityFactor __result)
            {
                if (!ModsConfig.IdeologyActive || __result == null || assignments == null)
                {
                    return;
                }

                int num = 0;
                foreach (Pawn pawn in assignments.Participants)
                {
                    if (pawn.RaceProps.Humanlike
                        || MechanoidMechanitorIdeologyAdaptationUtility
                            .CanServeAsIdeologyRoleOrRitualParticipant(pawn))
                    {
                        num++;
                    }
                }

                float maxValue = __instance.curve.Points[__instance.curve.PointsCount - 1].x;
                float quality = __instance.curve.Evaluate(num);
                __result.count = Mathf.Min(num, maxValue) + " / " + maxValue;
                __result.quality = quality;
                __result.positive = num > 0;
            }
        }
    }
}
