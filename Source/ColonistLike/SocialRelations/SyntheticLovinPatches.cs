using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生伴侣响应人类配偶原版 Lovin：仅窄范围补丁。
    /// </summary>
    public static class SyntheticLovinPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] SyntheticLovinPatches：";
        private const int ErrorKeyLovinToilStructureUnexpected = 879346702;
        private const int ErrorKeyGenerateLovinCooldownNotFound = 879346706;
        private const int ErrorKeyLovinMtbSinglePawnFactorNotFound = 879346707;

        internal static bool LovinMtbSinglePawnFactorAgePatchInstalled { get; private set; }

        [HarmonyPatch(
            typeof(LovePartnerRelationUtility),
            nameof(LovePartnerRelationUtility.GetPartnerInMyBed))]
        public static class Patch_LovePartnerRelationUtility_GetPartnerInMyBed
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn pawn, ref Pawn __result)
            {
                if (__result != null)
                {
                    return;
                }

                Pawn? partner =
                    SyntheticLovinUtility.TryFindEnabledSyntheticCompanionForRemoteLovin(pawn);
                if (partner != null)
                {
                    __result = partner;
                }
            }
        }

        [HarmonyPatch(typeof(ThinkNode_ChancePerHour_Lovin), "MtbHours")]
        public static class Patch_ThinkNode_ChancePerHour_Lovin_MtbHours
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn pawn, ref float __result)
            {
                if (__result <= 0f || DebugSettings.alwaysDoLovin)
                {
                    return;
                }

                Pawn? partner = LovePartnerRelationUtility.GetPartnerInMyBed(pawn);
                if (!SyntheticLovinUtility.IsFrequencyBoostRemoteEnabledSyntheticCompanion(
                        pawn,
                        partner))
                {
                    return;
                }

                __result /= 200f;
            }
        }

        [HarmonyPatch]
        public static class Patch_LovePartnerRelationUtility_LovinMtbSinglePawnFactor
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    LovinMtbSinglePawnFactorAgePatchInstalled = true;
                    return true;
                }

                LovinMtbSinglePawnFactorAgePatchInstalled = false;
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 LovePartnerRelationUtility.LovinMtbSinglePawnFactor(Pawn)，"
                    + "仿生伴侣 Lovin 有效年龄 Prefix 未应用。",
                    ErrorKeyLovinMtbSinglePawnFactorNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(LovePartnerRelationUtility),
                    "LovinMtbSinglePawnFactor",
                    new[] { typeof(Pawn) });
                return cachedTargetMethod;
            }

            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn, ref float __result)
            {
                if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
                {
                    return true;
                }

                if (!SyntheticLovinUtility.TryComputeSyntheticCompanionLovinMtbSinglePawnFactor(
                        pawn,
                        out float computed))
                {
                    return true;
                }

                __result = computed;
                return false;
            }
        }

        [HarmonyPatch(typeof(JobDriver_Lovin), "MakeNewToils")]
        public static class Patch_JobDriver_Lovin_MakeNewToils
        {
            [HarmonyPostfix]
            public static IEnumerable<Toil> Postfix(
                IEnumerable<Toil> __result,
                JobDriver_Lovin __instance)
            {
                return WrapRemoteLovinToils(__result, __instance);
            }

            private static IEnumerable<Toil> WrapRemoteLovinToils(
                IEnumerable<Toil> original,
                JobDriver_Lovin driver)
            {
                Pawn actor = driver.pawn;
                Job? job = driver.job;
                if (!SyntheticLovinUtility.TryGetLovinPartnerAndBed(
                    job,
                    out Pawn? partner,
                    out Building_Bed? bed)
                    || partner == null
                    || bed == null)
                {
                    foreach (Toil toil in original)
                    {
                        yield return toil;
                    }

                    yield break;
                }

                bool isSyntheticCompanion =
                    SyntheticLovinUtility.IsRemoteSyntheticCompanionLovinJob(actor, partner, bed);
                if (isSyntheticCompanion)
                {
                    Pawn humanSpouse = partner;
                    Building_Bed sharedBed = bed;
                    Pawn syntheticCompanion = actor;
                    driver.AddFailCondition(
                        () => SyntheticLovinUtility.ShouldFailRemoteSyntheticCompanionLovin(
                            syntheticCompanion,
                            humanSpouse,
                            sharedBed));

                    foreach (Toil toil in original)
                    {
                        yield return toil;
                    }

                    yield break;
                }

                bool isHumanInitiator =
                    SyntheticLovinUtility.IsRemoteHumanSpouseLovinJob(actor, partner, bed);
                if (!isHumanInitiator)
                {
                    foreach (Toil toil in original)
                    {
                        yield return toil;
                    }

                    yield break;
                }

                Pawn conceptionSpouse = actor;
                Pawn conceptionCompanion = partner;
                driver.AddFinishAction(
                    condition =>
                    {
                        if (condition == JobCondition.Succeeded)
                        {
                            SyntheticPregnancyUtility.TryConceiveAfterSuccessfulLovin(
                                conceptionSpouse,
                                conceptionCompanion);
                        }
                    });

                List<Toil> toils = new List<Toil>();
                foreach (Toil toil in original)
                {
                    toils.Add(toil);
                }

                int insertIndex = FindWaitInsertIndex(toils);
                if (insertIndex < 0)
                {
                    Log.ErrorOnce(
                        $"{LogPrefix}无法识别 JobDriver_Lovin 的瞬时初始化→最终 LayDown 结构，" +
                        "已回退原版 Toil 流程（远程入床等待未插入）。",
                        ErrorKeyLovinToilStructureUnexpected);
                    for (int i = 0; i < toils.Count; i++)
                    {
                        yield return toils[i];
                    }

                    yield break;
                }

                Toil waitForCompanion = CreateWaitForRemoteSyntheticCompanionInBedToil(
                    driver, actor, partner, bed);
                for (int i = 0; i < toils.Count; i++)
                {
                    if (i == insertIndex)
                    {
                        yield return waitForCompanion;
                    }

                    yield return toils[i];
                }
            }

            private static int FindWaitInsertIndex(List<Toil> toils)
            {
                for (int i = 0; i < toils.Count - 1; i++)
                {
                    if (toils[i].defaultCompleteMode == ToilCompleteMode.Instant
                        && toils[i + 1].defaultCompleteMode == ToilCompleteMode.Never)
                    {
                        return i + 1;
                    }
                }

                return -1;
            }

            private static Toil CreateWaitForRemoteSyntheticCompanionInBedToil(
                JobDriver_Lovin driver,
                Pawn humanSpouse,
                Pawn syntheticCompanion,
                Building_Bed bed)
            {
                Toil wait = Toils_LayDown.LayDown(
                    TargetIndex.B,
                    hasBed: true,
                    lookForOtherJobs: false,
                    canSleep: false,
                    gainRestAndHealth: false);
                wait.socialMode = RandomSocialMode.Off;
                wait.FailOn(
                    () => !SyntheticLovinUtility.IsRemoteSyntheticCompanionStillBoundForHumanWait(
                        humanSpouse,
                        syntheticCompanion,
                        bed));
                wait.AddPreTickIntervalAction(
                    delegate(int _)
                    {
                        if (SyntheticLovinUtility.IsRemoteSyntheticCompanionPhysicallyReadyInBed(
                            humanSpouse,
                            syntheticCompanion,
                            bed))
                        {
                            driver.ReadyForNextToil();
                        }
                    });
                return wait;
            }
        }

        [HarmonyPatch]
        public static class Patch_JobDriver_Lovin_GenerateRandomMinTicksToNextLovin
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    return true;
                }

                Log.ErrorOnce(
                    $"{LogPrefix}未找到 JobDriver_Lovin.GenerateRandomMinTicksToNextLovin，冷却补丁未应用。",
                    ErrorKeyGenerateLovinCooldownNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(JobDriver_Lovin),
                    "GenerateRandomMinTicksToNextLovin",
                    new[] { typeof(Pawn) });
                return cachedTargetMethod;
            }

            [HarmonyPostfix]
            public static void Postfix(JobDriver_Lovin __instance, Pawn pawn, ref int __result)
            {
                if (DebugSettings.alwaysDoLovin)
                {
                    return;
                }

                if (!SyntheticLovinUtility.IsBoundHumanSyntheticCompanionSpouseLovinDriver(__instance))
                {
                    return;
                }

                __result = Rand.RangeInclusive(1250, 3750);
            }
        }

        [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanUseBedEver))]
        public static class Patch_RestUtility_CanUseBedEver
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn p, ThingDef bedDef, ref bool __result)
            {
                if (p == null
                    || !MechanoidMechanitorCapabilityUtility.HasCapability(
                        p, MechanoidMechanitorCapability.SyntheticSpouseInteraction)
                    || p.CurJobDef != JobDefOf.Lovin)
                {
                    return true;
                }

                if (bedDef == null || !bedDef.IsBed || bedDef.building == null)
                {
                    __result = false;
                    return false;
                }

                if (!bedDef.building.bed_humanlike)
                {
                    __result = false;
                    return false;
                }

                if (p.BodySize > bedDef.building.bed_maxBodySize)
                {
                    __result = false;
                    return false;
                }

                if (ModsConfig.BiotechActive
                    && bedDef == ThingDefOf.DeathrestCasket
                    && !p.CanDeathrest())
                {
                    __result = false;
                    return false;
                }

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(
            typeof(PregnancyUtility),
            nameof(PregnancyUtility.PregnancyChanceForPartners))]
        public static class Patch_PregnancyUtility_PregnancyChanceForPartners
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn woman, Pawn man, ref float __result)
            {
                if (MechanoidMechanitorCapabilityUtility.HasCapability(
                        woman, MechanoidMechanitorCapability.SyntheticSpouseInteraction)
                    || MechanoidMechanitorCapabilityUtility.HasCapability(
                        man, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
                {
                    __result = 0f;
                    return false;
                }

                return true;
            }
        }
    }
}
