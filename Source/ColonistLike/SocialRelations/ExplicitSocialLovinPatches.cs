using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人响应人类配偶原版 Lovin：仅窄范围补丁。
    /// 不修改机械体 ThinkTree；恋人配套 Job 由原版 JobDriver_Lovin 自动创建。
    /// </summary>
    public static class ExplicitSocialLovinPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] ExplicitSocialLovinPatches：";
        private const int ErrorKeyClaimBedTargetNotFound = 879346701;
        private const int ErrorKeyLovinToilStructureUnexpected = 879346702;

        /// <summary>
        /// 原版未找到床上伴侣时，把已开启「与配偶爱爱」的远程恋人配偶交给 ThinkNode / JobGiver。
        /// </summary>
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

                // 发起者资格（Humanlike、存活、非授权恋人等）在 TryFind 入口统一校验。
                Pawn? partner =
                    ExplicitSocialLovinUtility.TryFindEnabledLoverPartnerForRemoteLovin(pawn);
                if (partner != null)
                {
                    __result = partner;
                }
            }
        }

        /// <summary>
        /// 远程 Lovin：人类侧在最终倒计时前等待恋人入床；恋人侧全程校验配偶仍在对应 Lovin。
        /// 仅包装原版 MakeNewToils，不复制完整 JobDriver。
        /// </summary>
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
                if (!ExplicitSocialLovinUtility.TryGetLovinPartnerAndBed(
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

                bool isLoverCompanion =
                    ExplicitSocialLovinUtility.IsRemoteLoverCompanionLovinJob(actor, partner, bed);
                if (isLoverCompanion)
                {
                    // 覆盖 ClaimBed / GotoBed / 瞬时初始化 / 最终 LayDown；
                    // 配偶已中断时阻止进入瞬时 Toil，避免反向重新启动人类 Lovin。
                    Pawn humanSpouse = partner;
                    Building_Bed sharedBed = bed;
                    Pawn lover = actor;
                    driver.AddFailCondition(
                        () => ExplicitSocialLovinUtility.ShouldFailRemoteLoverCompanionLovin(
                            lover,
                            humanSpouse,
                            sharedBed));
                }

                bool isHumanInitiator =
                    ExplicitSocialLovinUtility.IsRemoteHumanSpouseLovinJob(actor, partner, bed);
                if (!isHumanInitiator)
                {
                    foreach (Toil toil in original)
                    {
                        yield return toil;
                    }

                    yield break;
                }

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

                Toil waitForLover = CreateWaitForRemoteLoverInBedToil(driver, actor, partner, bed);
                for (int i = 0; i < toils.Count; i++)
                {
                    if (i == insertIndex)
                    {
                        yield return waitForLover;
                    }

                    yield return toils[i];
                }
            }

            /// <summary>
            /// 原版结构：Claim(Instant) → Goto(PatherArrival) → 初始化(Instant) → LayDown(Never)。
            /// 在「Instant 后紧跟 Never」之间插入等待 Toil。
            /// </summary>
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

            /// <summary>
            /// 复用原版 LayDown 床上姿态；不递减 ticksLeft，恋人入床后再进入最终倒计时。
            /// </summary>
            private static Toil CreateWaitForRemoteLoverInBedToil(
                JobDriver_Lovin driver,
                Pawn humanSpouse,
                Pawn lover,
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
                    () => !ExplicitSocialLovinUtility.IsRemoteLoverStillBoundForHumanWait(
                        humanSpouse,
                        lover,
                        bed));
                wait.AddPreTickIntervalAction(
                    delegate(int _)
                    {
                        if (ExplicitSocialLovinUtility.IsRemoteLoverPhysicallyReadyInBed(
                            humanSpouse,
                            lover,
                            bed))
                        {
                            driver.ReadyForNextToil();
                        }
                    });
                return wait;
            }
        }

        /// <summary>
        /// 仅当授权恋人正在执行 JobDefOf.Lovin 时，绕过机械体不能用人床的门槛。
        /// 开关关闭后已开始的本次 Lovin 仍可完成；不依赖开关状态维持床检查。
        /// </summary>
        [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanUseBedEver))]
        public static class Patch_RestUtility_CanUseBedEver
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn p, ThingDef bedDef, ref bool __result)
            {
                if (p == null
                    || !ExplicitSocialRelationUtility.IsOptedIn(p)
                    || p.CurJobDef != JobDefOf.Lovin)
                {
                    return true;
                }

                if (bedDef == null || !bedDef.IsBed || bedDef.building == null)
                {
                    __result = false;
                    return false;
                }

                // 仍要求床适用于人类，且体型不超过上限。
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

        /// <summary>
        /// 临时安全门：后续恋人自定义怀孕完成前，任一参与者为授权恋人则怀孕概率为零。
        /// </summary>
        [HarmonyPatch(
            typeof(PregnancyUtility),
            nameof(PregnancyUtility.PregnancyChanceForPartners))]
        public static class Patch_PregnancyUtility_PregnancyChanceForPartners
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn woman, Pawn man, ref float __result)
            {
                if (ExplicitSocialRelationUtility.IsOptedIn(woman)
                    || ExplicitSocialRelationUtility.IsOptedIn(man))
                {
                    __result = 0f;
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// 恋人执行 Lovin 时跳过 ClaimBedIfNonMedical，避免永久占用配偶床位。
        /// 原版 Toils_Bed 在 ownership 非空时会认领；普通 Pawn 不受影响。
        /// </summary>
        [HarmonyPatch]
        public static class Patch_Pawn_Ownership_ClaimBedIfNonMedical
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    return true;
                }

                Log.ErrorOnce(
                    $"{LogPrefix}未找到 Pawn_Ownership.ClaimBedIfNonMedical，补丁未应用。",
                    ErrorKeyClaimBedTargetNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(Pawn_Ownership),
                    nameof(Pawn_Ownership.ClaimBedIfNonMedical),
                    new[] { typeof(Building_Bed) });
                return cachedTargetMethod;
            }

            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, ref bool __result)
            {
                if (___pawn == null
                    || !ExplicitSocialRelationUtility.IsOptedIn(___pawn)
                    || ___pawn.CurJobDef != JobDefOf.Lovin)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }
    }
}
