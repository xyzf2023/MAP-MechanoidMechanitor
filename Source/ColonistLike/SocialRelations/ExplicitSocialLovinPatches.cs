using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

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

                // 发起者必须是人类配偶一侧，不能是挂载授权组件的恋人本人。
                if (pawn == null || ExplicitSocialRelationUtility.IsOptedIn(pawn))
                {
                    return;
                }

                Pawn? partner =
                    ExplicitSocialLovinUtility.TryFindEnabledLoverPartnerForRemoteLovin(pawn);
                if (partner != null)
                {
                    __result = partner;
                }
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
