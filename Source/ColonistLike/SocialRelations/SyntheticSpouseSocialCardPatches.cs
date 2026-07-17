using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 社交卡配偶按钮与生育方式按钮补丁。检查 SyntheticSpouseInteraction / SyntheticPregnancy。
    /// </summary>
    public static class SyntheticSpouseSocialCardPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] SyntheticSpouseSocialCardPatches：";
        private const int ErrorKeyCanDrawTryRomanceNotFound = 879346602;
        private const int ErrorKeyDrawTryRomanceNotFound = 879346603;
        private const int ErrorKeyDrawPregnancyApproachNotFound = 879346604;

        [HarmonyPatch]
        public static class Patch_SocialCardUtility_CanDrawTryRomance
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    return true;
                }

                Log.ErrorOnce(
                    $"{LogPrefix}未找到 SocialCardUtility.CanDrawTryRomance，补丁未应用。",
                    ErrorKeyCanDrawTryRomanceNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(SocialCardUtility),
                    "CanDrawTryRomance",
                    new[] { typeof(Pawn) });
                return cachedTargetMethod;
            }

            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn, ref bool __result)
            {
                if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
                {
                    return true;
                }

                __result = SyntheticSpouseUtility.CanShowAssignSpouseButton(pawn);
                return false;
            }
        }

        [HarmonyPatch]
        public static class Patch_SocialCardUtility_DrawTryRomance
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    return true;
                }

                Log.ErrorOnce(
                    $"{LogPrefix}未找到 SocialCardUtility.DrawTryRomance，补丁未应用。",
                    ErrorKeyDrawTryRomanceNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(SocialCardUtility),
                    "DrawTryRomance",
                    new[] { typeof(Rect), typeof(Pawn) });
                return cachedTargetMethod;
            }

            [HarmonyPrefix]
            public static bool Prefix(Rect buttonRect, Pawn pawn)
            {
                if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
                {
                    return true;
                }

                SyntheticSpouseUtility.DrawAssignSpouseButton(buttonRect, pawn);
                return false;
            }
        }

        [HarmonyPatch]
        public static class Patch_SocialCardUtility_DrawPregnancyApproach
        {
            private static MethodBase? TargetMethod()
            {
                MethodBase? method = AccessTools.Method(
                    typeof(SocialCardUtility),
                    "DrawPregnancyApproach");
                if (method == null)
                {
                    Log.ErrorOnce(
                        $"{LogPrefix}未找到 SocialCardUtility.DrawPregnancyApproach，" +
                        "仿生生育方式按钮补丁未应用。",
                        ErrorKeyDrawPregnancyApproachNotFound);
                }

                return method;
            }

            [HarmonyPrefix]
            public static bool Prefix(
                object entry,
                Rect rect,
                Pawn selPawnForSocialInfo)
            {
                if (!TryResolveSyntheticSpouseEntry(
                    entry,
                    selPawnForSocialInfo,
                    out Pawn? syntheticCompanion,
                    out Pawn? spouse)
                    || syntheticCompanion == null
                    || spouse == null)
                {
                    return true;
                }

                if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    syntheticCompanion, MechanoidMechanitorCapability.SyntheticPregnancy))
                {
                    return true;
                }

                SyntheticPregnancyUIUtility.DrawApproachButton(rect, syntheticCompanion, spouse);
                return false;
            }
        }

        private static bool TryResolveSyntheticSpouseEntry(
            object? entry,
            Pawn? selectedPawn,
            out Pawn? syntheticCompanion,
            out Pawn? spouse)
        {
            syntheticCompanion = null;
            spouse = null;
            if (entry == null || selectedPawn?.relations == null)
            {
                return false;
            }

            Pawn? otherPawn = Traverse.Create(entry).Field<Pawn>("otherPawn").Value;
            if (otherPawn?.relations == null)
            {
                return false;
            }

            if (!selectedPawn.relations.DirectRelationExists(
                    PawnRelationDefOf.Spouse,
                    otherPawn)
                || !otherPawn.relations.DirectRelationExists(
                    PawnRelationDefOf.Spouse,
                    selectedPawn))
            {
                return false;
            }

            if (MechanoidMechanitorCapabilityUtility.HasCapability(
                selectedPawn, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
            {
                syntheticCompanion = selectedPawn;
                spouse = otherPawn;
                return true;
            }

            if (MechanoidMechanitorCapabilityUtility.HasCapability(
                otherPawn, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
            {
                syntheticCompanion = otherPawn;
                spouse = selectedPawn;
                return true;
            }

            return false;
        }
    }
}
