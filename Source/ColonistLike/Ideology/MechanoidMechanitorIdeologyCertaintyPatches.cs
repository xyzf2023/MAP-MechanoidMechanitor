using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师意识形态确定度锁定为 100%，并阻止外部转换/传教改写其意识形态。
    /// </summary>
    public static class MechanoidMechanitorIdeologyCertaintyPatches
    {
        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.Certainty), MethodType.Getter)]
        public static class Patch_Certainty_Getter
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn ___pawn, ref float __result)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return;
                }

                __result = 1f;
            }
        }

        [HarmonyPatch]
        public static class Patch_CertaintyChangeFactor
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTarget != null)
                {
                    return cachedTarget;
                }

                cachedTarget = AccessTools.PropertyGetter(
                    typeof(Pawn_IdeoTracker),
                    "CertaintyChangeFactor");
                return cachedTarget;
            }

            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, ref float __result)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                __result = 1f;
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.IdeoConversionAttempt))]
        public static class Patch_IdeoConversionAttempt
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, ref bool __result)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                __result = false;
                MechanoidMechanitorIdeologyAdaptationUtility.ForceCertaintyFull(___pawn);
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.OffsetCertainty))]
        public static class Patch_OffsetCertainty
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                MechanoidMechanitorIdeologyAdaptationUtility.ForceCertaintyFull(___pawn);
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.Reassure))]
        public static class Patch_Reassure
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                MechanoidMechanitorIdeologyAdaptationUtility.ForceCertaintyFull(___pawn);
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo))]
        public static class Patch_SetIdeo
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, Ideo ideo)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                if (MechanoidMechanitorIdeologyAdaptationUtility.IsIdeoMutationAllowed)
                {
                    return true;
                }

                if (___pawn.ideo?.Ideo == null)
                {
                    return true;
                }

                if (ideo == ___pawn.Ideo)
                {
                    return true;
                }

                return false;
            }

            [HarmonyPostfix]
            public static void Postfix(Pawn ___pawn)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return;
                }

                MechanoidMechanitorIdeologyAdaptationUtility.ForceCertaintyFull(___pawn);
            }
        }

        [HarmonyPatch(typeof(CompAbilityEffect_Convert), nameof(CompAbilityEffect_Convert.Apply))]
        public static class Patch_CompAbilityEffect_Convert_Apply
        {
            [HarmonyPrefix]
            public static bool Prefix(CompAbilityEffect_Convert __instance, LocalTargetInfo target)
            {
                if (!ModsConfig.IdeologyActive)
                {
                    return true;
                }

                Pawn? initiator = __instance.parent?.pawn;
                Pawn? recipient = target.Pawn;
                if (recipient != null
                    && MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(recipient))
                {
                    return false;
                }

                if (initiator == null
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(initiator)
                    || recipient?.ideo == null
                    || initiator.Ideo == null)
                {
                    return true;
                }

                float certaintyReduction =
                    InteractionWorker_ConvertIdeoAttempt.CertaintyReduction(initiator, recipient)
                    * __instance.Props.convertPowerFactor;
                float certainty = recipient.ideo.Certainty;
                if (recipient.ideo.IdeoConversionAttempt(certaintyReduction, initiator.Ideo))
                {
                    recipient.ideo.SetIdeo(initiator.Ideo);
                    Messages.Message(
                        __instance.Props.successMessage.Formatted(
                            initiator.Named("INITIATOR"),
                            recipient.Named("RECIPIENT"),
                            initiator.Ideo.name.Named("IDEO")),
                        new LookTargets(new Pawn[] { initiator, recipient }),
                        MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    if (recipient.needs?.mood != null && __instance.Props.failedThoughtRecipient != null)
                    {
                        recipient.needs.mood.thoughts.memories.TryGainMemory(
                            __instance.Props.failedThoughtRecipient,
                            initiator);
                    }

                    Messages.Message(
                        __instance.Props.failMessage.Formatted(
                            initiator.Named("INITIATOR"),
                            recipient.Named("RECIPIENT"),
                            initiator.Ideo.name.Named("IDEO"),
                            certainty.ToStringPercent().Named("CERTAINTYBEFORE"),
                            recipient.ideo.Certainty.ToStringPercent().Named("CERTAINTYAFTER")),
                        new LookTargets(new Pawn[] { initiator, recipient }),
                        MessageTypeDefOf.NeutralEvent);
                }

                if (__instance.Props.sound != null)
                {
                    __instance.Props.sound.PlayOneShot(
                        new TargetInfo(target.Cell, initiator.Map));
                }

                return false;
            }
        }
    }
}
